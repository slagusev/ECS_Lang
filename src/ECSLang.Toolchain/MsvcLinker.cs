using System.Diagnostics;
using ECSLang.Core;

namespace ECSLang.Toolchain;

public sealed class MsvcLinker
{
    private readonly DiagnosticsBag _diagnostics;

    public MsvcLinker(DiagnosticsBag diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public bool Link(string objFilePath, string outputExePath, CompilerOptions? options = null)
    {
        var (linkExe, libPaths) = ResolveMsvcPaths();
        if (string.IsNullOrEmpty(linkExe) || !File.Exists(linkExe))
        {
            _diagnostics.ReportError("MSVC link.exe not found on this machine. Please install Visual Studio C++ Build Tools.", SourceSpan.None);
            return false;
        }

        var args = new List<string>
        {
            "/NOLOGO",
            "/SUBSYSTEM:CONSOLE",
            "/ENTRY:mainCRTStartup",
            $"\"{objFilePath}\"",
            $"/OUT:\"{outputExePath}\""
        };

        if (options?.GenerateDebugInfo == true)
        {
            args.Add("/DEBUG");
            string pdbPath = Path.ChangeExtension(outputExePath, ".pdb");
            args.Add($"/PDB:\"{pdbPath}\"");
        }

        if (options?.IsRelease == true)
        {
            args.Add("/OPT:REF");
            args.Add("/OPT:ICF");
        }

        foreach (var libPath in libPaths)
        {
            if (Directory.Exists(libPath))
            {
                args.Add($"/LIBPATH:\"{libPath}\"");
            }
        }

        // Standard C Runtime and Windows libraries
        args.Add("msvcrt.lib");
        args.Add("vcruntime.lib");
        args.Add("ucrt.lib");
        args.Add("kernel32.lib");

        // Native Raylib & Win32 graphics libraries
        string? raylibDir = FindNativeRaylibDir();
        if (!string.IsNullOrEmpty(raylibDir))
        {
            args.Add($"/LIBPATH:\"{raylibDir}\"");
            args.Add("raylib.lib");
            args.Add("user32.lib");
            args.Add("gdi32.lib");
            args.Add("winmm.lib");
            args.Add("shell32.lib");
            args.Add("opengl32.lib");
            args.Add("/EXPORT:NvOptimusEnablement");
            args.Add("/EXPORT:AmdPowerXpressRequestHighPerformance");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = linkExe,
            Arguments = string.Join(" ", args),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            _diagnostics.ReportError("Failed to start link.exe process.", SourceSpan.None);
            return false;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            _diagnostics.ReportError($"Linker failed with exit code {process.ExitCode}:\n{stdout}\n{stderr}", SourceSpan.None);
            return false;
        }

        return true;
    }

    private static (string? LinkExe, List<string> LibPaths) ResolveMsvcPaths()
    {
        var libPaths = new List<string>();

        // 1. Find Visual Studio via vswhere or default path
        string vsPath = ResolveVsPath();
        if (string.IsNullOrEmpty(vsPath))
            return (null, libPaths);

        string msvcRoot = Path.Combine(vsPath, "VC", "Tools", "MSVC");
        if (!Directory.Exists(msvcRoot))
            return (null, libPaths);

        // Find latest MSVC toolset version
        var latestMsvcDir = Directory.GetDirectories(msvcRoot)
            .OrderByDescending(d => Path.GetFileName(d))
            .FirstOrDefault();

        if (latestMsvcDir == null)
            return (null, libPaths);

        string linkExe = Path.Combine(latestMsvcDir, "bin", "Hostx64", "x64", "link.exe");
        string msvcLib = Path.Combine(latestMsvcDir, "lib", "x64");
        libPaths.Add(msvcLib);

        // 2. Find Windows SDK Lib paths
        string sdkRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Windows Kits", "10", "Lib");

        if (Directory.Exists(sdkRoot))
        {
            var latestSdk = Directory.GetDirectories(sdkRoot)
                .OrderByDescending(d => Path.GetFileName(d))
                .FirstOrDefault();

            if (latestSdk != null)
            {
                libPaths.Add(Path.Combine(latestSdk, "ucrt", "x64"));
                libPaths.Add(Path.Combine(latestSdk, "um", "x64"));
            }
        }

        return (linkExe, libPaths);
    }

    private static string ResolveVsPath()
    {
        string vswherePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");

        if (File.Exists(vswherePath))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = vswherePath,
                    Arguments = "-latest -property installationPath",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string path = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        return path;
                }
            }
            catch
            {
                // Fallback below
            }
        }

        // Common default fallback
        string defaultVs = @"C:\Program Files\Microsoft Visual Studio\2022\Community";
        return Directory.Exists(defaultVs) ? defaultVs : "";
    }

    private static string? FindNativeRaylibDir()
    {
        var candidates = new List<string?>
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var startDir in candidates)
        {
            string? current = startDir;
            while (!string.IsNullOrEmpty(current))
            {
                string candidate = Path.Combine(current, "native", "raylib", "lib");
                if (Directory.Exists(candidate))
                    return candidate;
                current = Path.GetDirectoryName(current);
            }
        }

        return null;
    }
}
