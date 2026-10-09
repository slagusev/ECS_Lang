using System.Diagnostics;
using ECSLang.Codegen.LLVM;
using ECSLang.Core;
using ECSLang.Frontend;
using ECSLang.Semantics;
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
        string? customOutputExe = null;
        string? emitIrPath = null;
        var options = new CompilerOptions();
        bool explicitWait = false;
        bool emitObjOnly = false;
        bool checkOnly = false;

        for (int i = 2; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "-o" && i + 1 < args.Length)
            {
                customOutputExe = Path.GetFullPath(args[++i]);
            }
            else if (arg is "--target" or "-t" && i + 1 < args.Length)
            {
                options.Target = TargetProfile.Parse(args[++i]);
            }
            else if (arg == "--os" && i + 1 < args.Length)
            {
                string osStr = args[++i].ToLowerInvariant();
                if (osStr is "win" or "windows") options.Target = TargetProfile.WindowsX64;
                else if (osStr is "linux") options.Target = TargetProfile.LinuxX64;
                else if (osStr is "macos" or "darwin" or "osx") options.Target = TargetProfile.MacosArm64;
            }
            else if (arg is "-c" or "--emit-obj")
            {
                emitObjOnly = true;
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
            else if (arg is "--check" or "--syntax-only")
            {
                checkOnly = true;
            }
        }

        string outputExe = customOutputExe ?? Path.Combine(outputDir, $"{baseName}{options.Target.ExecutableExtension}");

        // When running via 'ecs run', do not block waiting for Enter unless explicitly requested
        if (command == "run" && !explicitWait)
        {
            options.NoWaitOnExit = true;
        }

        var diagnostics = new DiagnosticsBag();

        string modeTag = $"{options.Target.Triple}, " + (options.IsRelease ? "Release, -O3" : $"{options.OptimizationLevel}");
        if (options.GenerateDebugInfo) modeTag += ", DebugInfo";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[ECS-Lang] Compiling {Path.GetFileName(inputPath)} [{modeTag}]...");
        Console.ResetColor();

        var swTotal = Stopwatch.StartNew();

        // 1. Frontend (Parsing & Module Resolution)
        var swFrontend = Stopwatch.StartNew();
        var loader = new ProjectLoader(diagnostics);
        var programAst = loader.Load(inputPath);
        swFrontend.Stop();

        if (programAst == null || diagnostics.HasErrors)
        {
            diagnostics.PrintToConsole();
            return 1;
        }

        if (loader.LoadedFiles.Count > 1)
        {
            Console.WriteLine($"[ECS-Lang] Resolved {loader.LoadedFiles.Count} module(s): {string.Join(", ", loader.LoadedFiles.Select(Path.GetFileName))}");
        }

        // 2. Semantics (Type Checking & DAG Dependency Analysis)
        var swSemantics = Stopwatch.StartNew();
        var typeChecker = new TypeChecker(diagnostics);
        typeChecker.CheckProgram(programAst);
        swSemantics.Stop();

        if (diagnostics.HasErrors)
        {
            diagnostics.PrintToConsole();
            return 1;
        }

        if (checkOnly)
        {
            diagnostics.PrintToConsole();
            return diagnostics.HasErrors ? 1 : 0;
        }

        // 3. LLVM Codegen
        var swCodegen = Stopwatch.StartNew();
        string finalObj = customOutputExe != null && emitObjOnly
            ? customOutputExe
            : Path.Combine(outputDir, $"{baseName}{options.Target.ObjectExtension}");
        string tempObj = emitObjOnly ? finalObj : Path.Combine(Path.GetTempPath(), $"{baseName}_{Guid.NewGuid():N}{options.Target.ObjectExtension}");
        var codegen = new LlvmCodeGenerator(diagnostics, typeChecker);
        bool codegenSuccess = codegen.Compile(programAst, tempObj, emitIrPath, options);
        swCodegen.Stop();

        if (!codegenSuccess || diagnostics.HasErrors)
        {
            diagnostics.PrintToConsole();
            return 1;
        }

        if (emitIrPath != null)
        {
            Console.WriteLine($"[ECS-Lang] Emitted LLVM IR to {emitIrPath}");
        }

        if (emitObjOnly)
        {
            swTotal.Stop();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[ECS-Lang] Successfully compiled object file to {finalObj}");
            Console.ResetColor();
            PrintCompilationMetrics(swFrontend.Elapsed.TotalMilliseconds, swSemantics.Elapsed.TotalMilliseconds, swCodegen.Elapsed.TotalMilliseconds, 0.0, swTotal.Elapsed.TotalMilliseconds);
            return 0;
        }

        // 4. Linker
        var swLinker = Stopwatch.StartNew();
        var outDir = Path.GetDirectoryName(Path.GetFullPath(outputExe));
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }
        var linker = LinkerFactory.Create(options.Target, diagnostics);
        bool linkSuccess = linker.Link(tempObj, outputExe, options);
        swLinker.Stop();

        // Cleanup temp obj file
        try { if (File.Exists(tempObj)) File.Delete(tempObj); } catch { }

        if (!linkSuccess || diagnostics.HasErrors)
        {
            diagnostics.PrintToConsole();
            return 1;
        }

        swTotal.Stop();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[ECS-Lang] Successfully compiled to {outputExe}");
        Console.ResetColor();

        PrintCompilationMetrics(
            swFrontend.Elapsed.TotalMilliseconds,
            swSemantics.Elapsed.TotalMilliseconds,
            swCodegen.Elapsed.TotalMilliseconds,
            swLinker.Elapsed.TotalMilliseconds,
            swTotal.Elapsed.TotalMilliseconds);

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
        Console.WriteLine("  --target, -t    Specify target triple (e.g. x86_64-unknown-linux-gnu, arm64-apple-darwin)");
        Console.WriteLine("  --os <platform> Target operating system (win, linux, macos)");
        Console.WriteLine("  --release, -r   Build in Release mode (-O3, linker optimizations, no wait-key)");
        Console.WriteLine("  -O0 .. -O3      LLVM optimization level (default: -O0)");
        Console.WriteLine("  -Os, -Oz        Optimize for code size");
        Console.WriteLine("  -g, --debug     Generate debug information (CodeView PDB / DWARF)");
        Console.WriteLine("  --no-wait       Do not wait for Enter key on exit");
        Console.WriteLine("  --wait-key      Wait for Enter key before exiting console");
        Console.WriteLine("  --check         Validate syntax and semantics without LLVM codegen");
        Console.WriteLine("  -c, --emit-obj  Emit object file (.obj / .o) without linking");
        Console.WriteLine("  --emit-ir       Emit LLVM IR (.ll) file");
    }

    private static void PrintCompilationMetrics(double frontendMs, double semanticsMs, double codegenMs, double linkerMs, double totalMs)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("[ECS-Lang] Compilation metrics:");
        Console.WriteLine($"  - Frontend  : {frontendMs.ToString("F2", ci)} ms");
        Console.WriteLine($"  - Semantics : {semanticsMs.ToString("F2", ci)} ms");
        Console.WriteLine($"  - Codegen   : {codegenMs.ToString("F2", ci)} ms");
        Console.WriteLine($"  - Linker    : {linkerMs.ToString("F2", ci)} ms");
        Console.WriteLine($"  - Total Time: {totalMs.ToString("F2", ci)} ms");
        Console.ResetColor();
    }
}
