using System.Diagnostics;
using ECSLang.Core;

namespace ECSLang.Toolchain;

public sealed class ClangGccLinker : ILinker
{
    private readonly DiagnosticsBag _diagnostics;
    private readonly TargetProfile _target;

    public ClangGccLinker(DiagnosticsBag diagnostics, TargetProfile? target = null)
    {
        _diagnostics = diagnostics;
        _target = target ?? TargetProfile.HostDefault;
    }

    public bool Link(string objFilePath, string outputExePath, CompilerOptions? options = null)
    {
        bool isCrossCompiling = _target.Platform != TargetProfile.HostDefault.Platform;
        string? compilerExe = FindCompilerExecutable(isCrossCompiling);

        if (string.IsNullOrEmpty(compilerExe))
        {
            if (isCrossCompiling)
            {
                // Preserve generated target object file for user
                string targetObj = Path.ChangeExtension(outputExePath, _target.ObjectExtension);
                try
                {
                    File.Copy(objFilePath, targetObj, true);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[ECS-Lang] Cross-compilation target: {_target.Triple}");
                    Console.WriteLine($"[ECS-Lang] Target object file preserved at: {targetObj}");
                    Console.WriteLine($"[ECS-Lang] No host cross-linker found (install Clang or lld to link directly).");
                    Console.WriteLine($"[ECS-Lang] To link on {_target.Platform}, run:");
                    string extraLibs = _target.IsLinux ? " -ldl -lrt" : "";
                    Console.WriteLine($"  clang {Path.GetFileName(targetObj)} -o {Path.GetFileName(outputExePath)} -lm -lpthread{extraLibs}");
                    Console.ResetColor();
                    return true;
                }
                catch (Exception ex)
                {
                    _diagnostics.ReportError($"Failed to copy cross-compiled object file to '{targetObj}': {ex.Message}", SourceSpan.None);
                    return false;
                }
            }

            _diagnostics.ReportError(
                $"No suitable C/C++ linker (clang, gcc, or lld) was found on this system for target '{_target.Triple}'. " +
                "Please install clang/gcc or use '--emit-ir' / '-c' to produce LLVM IR/object files.",
                SourceSpan.None);
            return false;
        }

        var args = new List<string>
        {
            $"\"{objFilePath}\"",
            "-o",
            $"\"{outputExePath}\""
        };

        if (compilerExe.Contains("clang", StringComparison.OrdinalIgnoreCase))
        {
            args.Add($"--target={_target.Triple}");
        }

        if (options?.GenerateDebugInfo == true)
        {
            args.Add("-g");
        }

        if (options?.IsRelease == true)
        {
            args.Add("-O3");
            if (_target.IsLinux)
            {
                args.Add("-Wl,--gc-sections");
            }
            else if (_target.IsMacOS)
            {
                args.Add("-Wl,-dead_strip");
            }
        }

        // Standard runtime libraries for POSIX systems
        args.Add("-lm");
        args.Add("-lpthread");

        if (_target.IsLinux)
        {
            args.Add("-ldl");
            args.Add("-lrt");
        }

        // Graphics and system frameworks if Raylib is used
        string? raylibDir = FindNativeRaylibDir();
        if (!string.IsNullOrEmpty(raylibDir))
        {
            args.Add($"-L\"{raylibDir}\"");
            args.Add("-lraylib");

            if (_target.IsMacOS)
            {
                args.Add("-framework OpenGL");
                args.Add("-framework Cocoa");
                args.Add("-framework IOKit");
                args.Add("-framework CoreVideo");
            }
            else if (_target.IsLinux)
            {
                args.Add("-lGL");
                args.Add("-lX11");
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = compilerExe,
            Arguments = string.Join(" ", args),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _diagnostics.ReportError($"Failed to start '{compilerExe}'.", SourceSpan.None);
                return false;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                _diagnostics.ReportError($"Linker '{compilerExe}' failed with exit code {process.ExitCode}:\n{stdout}\n{stderr}", SourceSpan.None);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _diagnostics.ReportError($"Failed to execute linker '{compilerExe}': {ex.Message}", SourceSpan.None);
            return false;
        }
    }

    private string? FindCompilerExecutable(bool isCrossCompiling)
    {
        string[] candidates = isCrossCompiling
            ? new[] { "clang", "clang-20", "clang-19", "clang-18", "clang-17", $"{_target.Triple}-gcc", $"{_target.Triple}-clang", "lld" }
            : new[] { "clang", "gcc", "clang-20", "clang-19", "clang-18", "clang-17", "g++", "lld" };

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathDirs = pathEnv.Split(Path.PathSeparator);

        foreach (var candidate in candidates)
        {
            string exeName = OperatingSystem.IsWindows() ? $"{candidate}.exe" : candidate;
            foreach (var dir in pathDirs)
            {
                try
                {
                    string fullPath = Path.Combine(dir, exeName);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch
                {
                    // Ignore path lookup errors
                }
            }
        }

        return null;
    }

    private static string? FindNativeRaylibDir()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(appDir, "native"),
            Path.Combine(Directory.GetCurrentDirectory(), "native"),
            Path.Combine(Directory.GetCurrentDirectory(), "lib"),
            "/usr/local/lib",
            "/usr/lib",
            "/opt/homebrew/lib"
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir))
            {
                if (File.Exists(Path.Combine(dir, "libraylib.a")) ||
                    File.Exists(Path.Combine(dir, "libraylib.so")) ||
                    File.Exists(Path.Combine(dir, "libraylib.dylib")) ||
                    File.Exists(Path.Combine(dir, "raylib.lib")))
                {
                    return dir;
                }
            }
        }

        return null;
    }
}
