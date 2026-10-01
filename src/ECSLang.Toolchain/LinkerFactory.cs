using System.Runtime.InteropServices;
using ECSLang.Core;

namespace ECSLang.Toolchain;

public static class LinkerFactory
{
    public static ILinker Create(TargetProfile target, DiagnosticsBag diagnostics)
    {
        if (target.IsWindows)
        {
            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return new MsvcLinker(diagnostics);
            }
            return new ClangGccLinker(diagnostics, target);
        }

        return new ClangGccLinker(diagnostics, target);
    }
}
