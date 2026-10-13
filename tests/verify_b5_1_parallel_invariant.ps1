# Verification script for B5.1 parallel vs sequential invariant
param (
    [string]$CliPath = "src/ECSLang.CLI/bin/Release/net9.0/ECSLang.CLI.exe"
)

$ErrorActionPreference = "Stop"

$cliDll = [System.IO.Path]::ChangeExtension($CliPath, ".dll")
if (-not (Test-Path $cliDll)) {
    throw "Compiler binary not found: $cliDll. Run dotnet build first."
}

function Invoke-EcsCompiler {
    param([string[]]$CompilerArgs)
    if ([System.OperatingSystem]::IsLinux() -or [System.OperatingSystem]::IsMacOS() -or (-not (Test-Path $CliPath))) {
        & dotnet $cliDll @CompilerArgs
    } else {
        & $CliPath @CompilerArgs
    }
}

$testSrc = "tests/verify_b5_1_parallel_invariant.ecs"
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_b51_inv_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

try {
    Write-Host "Compiling $testSrc..." -ForegroundColor Cyan
    Invoke-EcsCompiler @("build", $testSrc, "-o", (Join-Path $tempDir "inv_test.exe"), "-O3")

    $exePath = Join-Path $tempDir "inv_test.exe"
    if (-not (Test-Path $exePath)) {
        throw "Failed to compile $testSrc to $exePath"
    }

    Write-Host "Running invariant test binary: $exePath..." -ForegroundColor Cyan
    $output = & $exePath
    $output | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    if ($LASTEXITCODE -ne 0) {
        throw "B5.1 parallel invariant test FAILED with exit code $LASTEXITCODE"
    }

    if ($output -notmatch "B5.1 Parallel Invariant: PASSED") {
        throw "B5.1 parallel invariant test did not produce success banner"
    }

    Write-Host "B5.1 Parallel Invariant Test: OK" -ForegroundColor Green
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
