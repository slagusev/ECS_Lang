# Stress test runner for B5.1 concurrent string arena allocations under lock
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

$testSrc = "tests/stress_parallel_string_arena.ecs"
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_arena_stress_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

try {
    Write-Host "Compiling $testSrc..." -ForegroundColor Cyan
    Invoke-EcsCompiler @("build", $testSrc, "-o", (Join-Path $tempDir "stress_test.exe"), "-O3")

    $exePath = Join-Path $tempDir "stress_test.exe"
    if (-not (Test-Path $exePath)) {
        throw "Failed to compile $testSrc to $exePath"
    }

    Write-Host "Running string arena stress test binary: $exePath..." -ForegroundColor Cyan
    $output = & $exePath
    $output | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    if ($LASTEXITCODE -ne 0) {
        throw "Stress test FAILED with exit code $LASTEXITCODE"
    }

    $outStr = $output -join "`n"
    if ($outStr -notmatch "STRESS TEST COMPLETED") {
        throw "Stress test did not finish successfully"
    }

    Write-Host "Parallel String Arena Stress Test: OK" -ForegroundColor Green
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
