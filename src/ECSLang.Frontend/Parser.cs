using ECSLang.Core;
using ECSLang.Core.AST;

namespace ECSLang.Frontend;

public sealed partial class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly DiagnosticsBag _diagnostics;
    private int _index;

    public Parser(IReadOnlyList<Token> tokens, DiagnosticsBag diagnostics)
    {
        _tokens = tokens;
        _diagnostics = diagnostics;
    }

    private Token Current => _index < _tokens.Count ? _tokens[_index] : _tokens[^1];
    private Token Peek(int offset) => (_index + offset < _tokens.Count) ? _tokens[_index + offset] : _tokens[^1];

    private Token Advance()
    {
        var token = Current;
        if (_index < _tokens.Count - 1)
            _index++;
        return token;
    }

    private Token Match(TokenType type, string? errorMessage = null)
    {
        if (Current.Type == type)
            return Advance();

        _diagnostics.ReportError(
            errorMessage ?? $"Expected token '{type}' but found '{Current.Type}' ('{Current.Text}').",
            Current.Span);
        return Current;
    }

    private bool Check(TokenType type) => Current.Type == type;

    public ProgramNode ParseProgram()
    {
        var declarations = new List<DeclarationNode>();
        var startSpan = Current.Span;

        while (!Check(TokenType.EndOfFile))
        {
            if (Check(TokenType.Fn))
            {
                declarations.Add(ParseFunctionDeclaration());
            }
            else if (Check(TokenType.Component))
            {
                declarations.Add(ParseComponentDeclaration());
            }
            else if (Check(TokenType.Struct))
            {
                declarations.Add(ParseStructDeclaration());
            }
            else if (Check(TokenType.Resource))
            {
                declarations.Add(ParseResourceDeclaration());
            }
            else if (Check(TokenType.Event))
            {
                declarations.Add(ParseEventDeclaration());
            }
            else if (Check(TokenType.Enum))
            {
                declarations.Add(ParseEnumDeclaration());
            }
            else if (Check(TokenType.System))
            {
                declarations.Add(ParseSystemDeclaration());
            }
            else if (Check(TokenType.Pipeline))
            {
                declarations.Add(ParsePipelineDeclaration());
            }
            else if (Check(TokenType.Import))
            {
                declarations.Add(ParseImportDirective());
            }
            else if (Check(TokenType.Impl))
            {
                declarations.Add(ParseImplDeclaration());
            }
            else if (Check(TokenType.Trait))
            {
                declarations.Add(ParseTraitDeclaration());
            }
            else
            {
                _diagnostics.ReportError($"Unexpected token '{Current.Text}' at file root.", Current.Span);
                Advance();
            }
        }

        return new ProgramNode(declarations, startSpan);
    }
}
