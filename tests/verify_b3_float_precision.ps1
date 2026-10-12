# Invariant Level 2 Verification Script for Step B3: Round-Trip Faithful Floating-Point Formatting
$ErrorActionPreference = "Stop"

$cliDll = "$PSScriptRoot\..\src\ECSLang.CLI\bin\Release\net9.0\ECSLang.CLI.dll"
$cliExe = "$PSScriptRoot\..\src\ECSLang.CLI\bin\Release\net9.0\ECSLang.CLI.exe"
$src = "$PSScriptRoot\verify_b3_float_precision.ecs"
$exe = "$PSScriptRoot\verify_b3_float_precision.exe"

function Invoke-Cli {
    param([string[]]$CompilerArgs)
    if ([System.OperatingSystem]::IsLinux() -or [System.OperatingSystem]::IsMacOS() -or (-not (Test-Path $cliExe))) {
        & dotnet $cliDll @CompilerArgs
    } else {
        & $cliExe @CompilerArgs
    }
}

Remove-Item -Path $exe -ErrorAction SilentlyContinue

Write-Host "Compiling $src..."
Invoke-Cli @("build", $src)
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to compile verify_b3_float_precision.ecs"
    exit 1
}

try {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    $exitCode = $proc.ExitCode
    Write-Host "Process Exit Code: $exitCode"
    Write-Host "Output:`n$stdout"

    if ($exitCode -ne 0) {
        Write-Error "TEST FAILED: Process exited with non-zero code $exitCode"
        exit 1
    }

    if (-not ($stdout -like "*B3 float precision invariants verified: f32 (%.9g) and f64 (%.17g) PASSED*")) {
        Write-Error "TEST FAILED: Verification pass banner missing from output"
        exit 1
    }

    Write-Host "[OK] Level 2 Float Precision Invariant Verified." -ForegroundColor Green
} finally {
    Remove-Item -Path "$PSScriptRoot\verify_b3_float_precision.exe", "$PSScriptRoot\verify_b3_float_precision.obj", "$PSScriptRoot\verify_b3_float_precision.pdb", "$PSScriptRoot\verify_b3_float_precision.exp", "$PSScriptRoot\verify_b3_float_precision.lib" -Force -ErrorAction SilentlyContinue
}
