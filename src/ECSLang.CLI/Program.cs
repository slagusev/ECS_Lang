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
        var options = new CompilerOptions();
        bool explicitWait = false;

        for (int i = 2; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "-o" && i + 1 < args.Length)
            {
                outputExe = Path.GetFullPath(args[++i]);
            }
            else if (arg is "--emit-ir" or "--emit-llvm")
            {
                emitIrPath = Path.Combine(outputDir, $"{baseName}.ll");
            }
            else if (arg is "--release" or "-r")
            {
                options.IsRelease = true;
                options.OptimizationLevel = OptimizationLevel.O3;
                options.NoWaitOnExit = true;
            }
            else if (arg is "-O0") options.OptimizationLevel = OptimizationLevel.O0;
            else if (arg is "-O1") options.OptimizationLevel = OptimizationLevel.O1;
            else if (arg is "-O2") options.OptimizationLevel = OptimizationLevel.O2;
            else if (arg is "-O3") options.OptimizationLevel = OptimizationLevel.O3;
            else if (arg is "-Os") options.OptimizationLevel = OptimizationLevel.Os;
            else if (arg is "-Oz") options.OptimizationLevel = OptimizationLevel.Oz;
            else if (arg is "-g" or "--debug")
            {
                options.GenerateDebugInfo = true;
            }
            else if (arg is "--no-wait")
            {
                options.NoWaitOnExit = true;
            }
            else if (arg is "--wait-key" or "--pause")
            {
                options.NoWaitOnExit = false;
                explicitWait = true;
            }
        }

        // When running via 'ecs run', do not block waiting for Enter unless explicitly requested
        if (command == "run" && !explicitWait)
        {
            options.NoWaitOnExit = true;
        }

        var diagnostics = new DiagnosticsBag();

        string modeTag = options.IsRelease ? "Release, -O3" : $"{options.OptimizationLevel}";
        if (options.GenerateDebugInfo) modeTag += ", DebugInfo";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[ECS-Lang] Compiling {Path.GetFileName(inputPath)} [{modeTag}]...");
        Console.ResetColor();

        // 1. Load project and modules with cycle detection
        var loader = new ProjectLoader(diagnostics);
        var programAst = loader.Load(inputPath);
        if (programAst == null || diagnostics.HasErrors)
        {
            diagnostics.PrintToConsole();
            return 1;
        }

        if (loader.LoadedFiles.Count > 1)
        {
            Console.WriteLine($"[ECS-Lang] Resolved {loader.LoadedFiles.Count} module(s): {string.Join(", ", loader.LoadedFiles.Select(Path.GetFileName))}");
        }

        // 4. LLVM Codegen
        string tempObj = Path.Combine(Path.GetTempPath(), $"{baseName}_{Guid.NewGuid():N}.obj");
        var codegen = new LlvmCodeGenerator(diagnostics);
        bool codegenSuccess = codegen.Compile(programAst, tempObj, emitIrPath, options);
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
        bool linkSuccess = linker.Link(tempObj, outputExe, options);

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
        Console.WriteLine("  ecs build <file.ecs> [options]");
        Console.WriteLine("  ecs run   <file.ecs> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o <path>       Specify output executable path");
        Console.WriteLine("  --release, -r   Build in Release mode (-O3, linker optimizations, no wait-key)");
        Console.WriteLine("  -O0 .. -O3      LLVM optimization level (default: -O0)");
        Console.WriteLine("  -Os, -Oz        Optimize for code size");
        Console.WriteLine("  -g, --debug     Generate debug information (CodeView PDB / DWARF)");
        Console.WriteLine("  --no-wait       Do not wait for Enter key on exit");
        Console.WriteLine("  --wait-key      Wait for Enter key before exiting console");
        Console.WriteLine("  --emit-ir       Emit LLVM IR (.ll) file");
    }
}
