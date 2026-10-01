using System.Runtime.InteropServices;

namespace ECSLang.Core;

public enum PlatformKind
{
    Windows,
    Linux,
    MacOS
}

public enum CpuArchitecture
{
    X64,
    Arm64
}

public sealed class TargetProfile
{
    public PlatformKind Platform { get; }
    public CpuArchitecture Architecture { get; }
    public string Triple { get; }
    public string ExecutableExtension { get; }
    public string ObjectExtension { get; }

    public bool IsWindows => Platform == PlatformKind.Windows;
    public bool IsLinux => Platform == PlatformKind.Linux;
    public bool IsMacOS => Platform == PlatformKind.MacOS;

    public TargetProfile(PlatformKind platform, CpuArchitecture architecture, string triple, string exeExt, string objExt)
    {
        Platform = platform;
        Architecture = architecture;
        Triple = triple;
        ExecutableExtension = exeExt;
        ObjectExtension = objExt;
    }

    public static TargetProfile WindowsX64 { get; } = new(PlatformKind.Windows, CpuArchitecture.X64, "x86_64-pc-windows-msvc", ".exe", ".obj");
    public static TargetProfile WindowsArm64 { get; } = new(PlatformKind.Windows, CpuArchitecture.Arm64, "aarch64-pc-windows-msvc", ".exe", ".obj");
    public static TargetProfile LinuxX64 { get; } = new(PlatformKind.Linux, CpuArchitecture.X64, "x86_64-unknown-linux-gnu", "", ".o");
    public static TargetProfile LinuxArm64 { get; } = new(PlatformKind.Linux, CpuArchitecture.Arm64, "aarch64-unknown-linux-gnu", "", ".o");
    public static TargetProfile MacosX64 { get; } = new(PlatformKind.MacOS, CpuArchitecture.X64, "x86_64-apple-darwin", "", ".o");
    public static TargetProfile MacosArm64 { get; } = new(PlatformKind.MacOS, CpuArchitecture.Arm64, "arm64-apple-darwin", "", ".o");

    public static TargetProfile HostDefault
    {
        get
        {
            var arch = RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 
                ? CpuArchitecture.Arm64 
                : CpuArchitecture.X64;

            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                return arch == CpuArchitecture.Arm64 ? WindowsArm64 : WindowsX64;
            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                return arch == CpuArchitecture.Arm64 ? MacosArm64 : MacosX64;
            return arch == CpuArchitecture.Arm64 ? LinuxArm64 : LinuxX64;
        }
    }

    public static TargetProfile Parse(string input)
    {
        var lower = input.Trim().ToLowerInvariant();
        if (lower is "windows" or "win" or "win64" or "x86_64-pc-windows-msvc" or "x86_64-windows")
            return WindowsX64;
        if (lower is "win-arm64" or "aarch64-pc-windows-msvc")
            return WindowsArm64;
        if (lower is "linux" or "linux64" or "x86_64-unknown-linux-gnu" or "x86_64-linux")
            return LinuxX64;
        if (lower is "linux-arm64" or "aarch64-unknown-linux-gnu" or "aarch64-linux")
            return LinuxArm64;
        if (lower is "macos" or "darwin" or "osx" or "arm64-apple-darwin" or "macos-arm64")
            return MacosArm64;
        if (lower is "macos-x64" or "x86_64-apple-darwin")
            return MacosX64;

        var platform = lower.Contains("windows") || lower.Contains("msvc") ? PlatformKind.Windows
            : (lower.Contains("darwin") || lower.Contains("apple") || lower.Contains("macos") ? PlatformKind.MacOS : PlatformKind.Linux);
        var arch = lower.StartsWith("aarch64") || lower.StartsWith("arm64") ? CpuArchitecture.Arm64 : CpuArchitecture.X64;
        var exeExt = platform == PlatformKind.Windows ? ".exe" : "";
        var objExt = platform == PlatformKind.Windows ? ".obj" : ".o";
        return new TargetProfile(platform, arch, input.Trim(), exeExt, objExt);
    }

    public override string ToString() => Triple;
}
