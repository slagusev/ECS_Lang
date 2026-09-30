using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Frontend;

/// <summary>
/// Loads multi-file ECS-Lang projects by recursively resolving and parsing import directives,
/// performing cycle detection, and merging AST declarations in topological dependency order.
/// </summary>
public sealed class ProjectLoader
{
    private readonly DiagnosticsBag _diagnostics;
    private readonly Dictionary<string, ProgramNode> _loadedAsts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _inProgressStack = new();
    private readonly List<string> _topologicalOrder = new();

    public IReadOnlyList<string> LoadedFiles => _topologicalOrder;

    public ProjectLoader(DiagnosticsBag diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public static ProgramNode? LoadProject(string entryFilePath, DiagnosticsBag diagnostics)
    {
        var loader = new ProjectLoader(diagnostics);
        return loader.Load(entryFilePath);
    }

    public ProgramNode? Load(string entryFilePath)
    {
        string fullPath = Path.GetFullPath(entryFilePath);
        if (!File.Exists(fullPath))
        {
            _diagnostics.ReportError($"Source file '{entryFilePath}' does not exist.", SourceSpan.None);
            return null;
        }

        LoadRecursive(fullPath, SourceSpan.None);

        if (_diagnostics.HasErrors)
        {
            return null;
        }

        // Merge declarations in topological post-order (dependencies first)
        var mergedDeclarations = new List<DeclarationNode>();
        foreach (var path in _topologicalOrder)
        {
            if (_loadedAsts.TryGetValue(path, out var ast))
            {
                foreach (var decl in ast.Declarations)
                {
                    if (decl is not ImportDirective)
                    {
                        mergedDeclarations.Add(decl);
                    }
                }
            }
        }

        var entryAst = _loadedAsts.TryGetValue(fullPath, out var ea) ? ea : null;
        var entrySpan = entryAst?.Span ?? SourceSpan.None;
        return new ProgramNode(mergedDeclarations, entrySpan);
    }

    private void LoadRecursive(string filePath, SourceSpan importSpan)
    {
        // Detect circular dependency
        int cycleIndex = _inProgressStack.FindIndex(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
        if (cycleIndex >= 0)
        {
            var cyclePath = _inProgressStack.Skip(cycleIndex).Append(filePath).Select(Path.GetFileName);
            _diagnostics.ReportError(
                $"Circular dependency detected: {string.Join(" -> ", cyclePath)}",
                importSpan);
            return;
        }

        // If already completely processed, no need to traverse again
        if (_topologicalOrder.Any(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // Parse file if not yet parsed
        if (!_loadedAsts.TryGetValue(filePath, out var programAst))
        {
            string sourceText;
            try
            {
                sourceText = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                _diagnostics.ReportError($"Failed to read file '{filePath}': {ex.Message}", importSpan);
                return;
            }

            var lexer = new Lexer(sourceText, filePath, _diagnostics);
            var tokens = lexer.TokenizeAll();
            if (_diagnostics.HasErrors)
            {
                return;
            }

            var parser = new Parser(tokens, _diagnostics);
            programAst = parser.ParseProgram();
            if (_diagnostics.HasErrors)
            {
                return;
            }

            _loadedAsts[filePath] = programAst;
        }

        _inProgressStack.Add(filePath);

        string currentDir = Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();

        // Recursively resolve imports
        foreach (var decl in programAst.Declarations)
        {
            if (decl is ImportDirective importDecl)
            {
                string rawPath = importDecl.ModulePath;
                if (string.IsNullOrWhiteSpace(rawPath))
                    continue;

                string resolved = Path.GetFullPath(Path.Combine(currentDir, rawPath));
                if (!File.Exists(resolved) && File.Exists(resolved + ".ecs"))
                {
                    resolved += ".ecs";
                }

                if (!File.Exists(resolved))
                {
                    _diagnostics.ReportError(
                        $"Module not found: '{rawPath}' (searched at '{resolved}').",
                        importDecl.Span);
                    continue;
                }

                LoadRecursive(resolved, importDecl.Span);
            }
        }

        _inProgressStack.RemoveAt(_inProgressStack.Count - 1);

        if (!_topologicalOrder.Any(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase)))
        {
            _topologicalOrder.Add(filePath);
        }
    }
}
