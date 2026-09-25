using System.Diagnostics;
using ECSLang.Codegen.LLVM;
using ECSLang.Core;
using ECSLang.Frontend;
using ECSLang.Toolchain;

namespace ECSLang.CLI;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        if (command is not ("build" or "run"))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Unknown command: {command}");
            Console.ResetColor();
            PrintHelp();
            return 1;
        }

        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing input source file.");
            Console.ResetColor();
            return 1;
        }

        string inputPath = Path.GetFullPath(args[1]);
        if (!File.Exists(inputPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Source file '{inputPath}' does not exist.");
            Console.ResetColor();
            return 1;
        }

        string baseName = Path.GetFileNameWithoutExtension(inputPath);
        string outputDir = Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
        string outputExe = Path.Combine(outputDir, $"{baseName}.exe");
        string? emitIrPath = null;

        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length)
            {
                outputExe = Path.GetFullPath(args[++i]);
            }
            else if (args[i] is "--emit-ir" or "--emit-llvm")
            {
                emitIrPath = Path.Combine(outputDir, $"{baseName}.ll");
            }
        }

        var diagnostics = new DiagnosticsBag();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[ECS-Lang] Compiling {Path.GetFileName(inputPath)}...");
        Console.ResetColor();

        // 1. Read source
        string sourceText = File.ReadAllText(inputPath);

        // 2. Lexer
        var lexer = new Lexer(sourceText, inputPath, diagnostics);
        var tokens = lexer.TokenizeAll();
        if (diagnostics.HasErrors)
        {
            diagnostics.PrintToConsole();
            return 1;
        }

        // 3. Parser
        var parser = new Parser(tokens, diagnostics);
        var programAst = parser.ParseProgram();
        if (diagnostics.HasErrors)
        {
            diagnostics.PrintToConsole();
            return 1;
        }

        // 4. LLVM Codegen
        string tempObj = Path.Combine(Path.GetTempPath(), $"{baseName}_{Guid.NewGuid():N}.obj");
        var codegen = new LlvmCodeGenerator(diagnostics);
        bool codegenSuccess = codegen.Compile(programAst, tempObj, emitIrPath);
        if (!codegenSuccess || diagnostics.HasErrors)
        {
            diagnostics.PrintToConsole();
            return 1;
        }

        if (emitIrPath != null)
        {
            Console.WriteLine($"[ECS-Lang] Emitted LLVM IR to {emitIrPath}");
        }

        // 5. Linker
        var linker = new MsvcLinker(diagnostics);
        bool linkSuccess = linker.Link(tempObj, outputExe);

        // Cleanup temp obj file
        try { if (File.Exists(tempObj)) File.Delete(tempObj); } catch { }

        if (!linkSuccess || diagnostics.HasErrors)
        {
            diagnostics.PrintToConsole();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[ECS-Lang] Successfully compiled to {outputExe}");
        Console.ResetColor();

        // 6. Run if requested
        if (command == "run")
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[ECS-Lang] Running {outputExe}...");
            Console.WriteLine("------------------------------------------");
            Console.ResetColor();

            var psi = new ProcessStartInfo
            {
                FileName = outputExe,
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit();
            int exitCode = proc?.ExitCode ?? 0;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("------------------------------------------");
            Console.WriteLine($"[ECS-Lang] Process exited with code {exitCode}");
            Console.ResetColor();
            return exitCode;
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("ECS-Lang Native Compiler (LLVM)");
        Console.WriteLine("Usage:");
        Console.WriteLine("  ecs build <file.ecs> [-o <out.exe>] [--emit-ir]");
        Console.WriteLine("  ecs run   <file.ecs> [--emit-ir]");
    }
}
