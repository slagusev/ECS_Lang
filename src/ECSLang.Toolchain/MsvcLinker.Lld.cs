namespace ECSLang.Toolchain;

public sealed partial class MsvcLinker
{
    /// <summary>
    /// Attempts to locate the fast multithreaded lld-link linker in PATH, LLVM installation dirs,
    /// or embedded Visual Studio LLVM toolsets.
    /// Returns null if lld-link is not found, allowing seamless fallback to MSVC link.exe.
    /// </summary>
    internal static string? TryResolveLldLink(string? vsPath)
    {
        try
        {
            // 1. Check PATH environment variable
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var searchDirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var dir in searchDirs)
                {
                    try
                    {
                        var candidate = Path.Combine(dir, "lld-link.exe");
                        if (File.Exists(candidate))
                            return candidate;
                    }
                    catch
                    {
                        // Ignore individual path access issues
                    }
                }
            }

            // 2. Check LLVM_HOME / LLVM_PATH
            var llvmEnv = Environment.GetEnvironmentVariable("LLVM_HOME") ?? Environment.GetEnvironmentVariable("LLVM_PATH");
            if (!string.IsNullOrEmpty(llvmEnv))
            {
                var candidate = Path.Combine(llvmEnv, "bin", "lld-link.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            // 3. Check standard Program Files / LLVM installation
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles))
            {
                var candidate = Path.Combine(programFiles, "LLVM", "bin", "lld-link.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFilesX86))
            {
                var candidate = Path.Combine(programFilesX86, "LLVM", "bin", "lld-link.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            // 4. Check Visual Studio embedded LLVM toolsets if VS path is resolved
            if (!string.IsNullOrEmpty(vsPath) && Directory.Exists(vsPath))
            {
                var vsCandidates = new[]
                {
                    Path.Combine(vsPath, "VC", "Tools", "Llvm", "x64", "bin", "lld-link.exe"),
                    Path.Combine(vsPath, "VC", "Tools", "Llvm", "bin", "lld-link.exe")
                };

                foreach (var candidate in vsCandidates)
                {
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }
        catch
        {
            // Any lookup error results in safe null fallback
        }

        return null;
    }
}
