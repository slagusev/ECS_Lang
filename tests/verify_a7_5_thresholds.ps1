# ========================================================================
# Level 2 Invariant Test: Validation of Component Limits & Thresholds
# Verifies:
# 1. totalComps > 65535 triggers diagnostic error before codegen in seconds
# 2. totalComps > 1024 triggers warning "compile time grows quadratically with component count (see B7)"
# ========================================================================

param (
    [string]$CliPath = "src/ECSLang.CLI/bin/Debug/net9.0/ECSLang.CLI.exe"
)

$ErrorActionPreference = "Stop"
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_thresh_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

try {
    # -------------------------------------------------------------------------
    # Part 1: Error when totalComps > 65535 (65536 components)
    # -------------------------------------------------------------------------
    Write-Host "1. Testing threshold > 65535 (65536 components)..." -ForegroundColor Cyan
    $file65536 = Join-Path $tempDir "test_65536_comps.ecs"
    $sb = [System.Text.StringBuilder]::new()
    for ($i = 1; $i -le 65536; $i++) {
        [void]$sb.AppendLine("component C$i { v: i32 }")
    }
    [void]$sb.AppendLine("fn main(): i32 { return 0; }")
    [System.IO.File]::WriteAllText($file65536, $sb.ToString())

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $CliPath -ArgumentList "build `"$file65536`" --check" -NoNewWindow -Wait -PassThru -RedirectStandardOutput (Join-Path $tempDir "out65k.txt") -RedirectStandardError (Join-Path $tempDir "err65k.txt")
    $sw.Stop()

    $out65k = Get-Content (Join-Path $tempDir "out65k.txt") -Raw -ErrorAction SilentlyContinue
    $err65k = Get-Content (Join-Path $tempDir "err65k.txt") -Raw -ErrorAction SilentlyContinue
    $allOut65k = "$out65k`n$err65k"

    if ($proc.ExitCode -eq 0) {
        throw "FAIL: Expected compilation error for 65536 components, but compiler succeeded with exit code 0."
    }

    if ($allOut65k -notmatch "Maximum component limit of 65535 exceeded") {
        throw "FAIL: Expected diagnostic message 'Maximum component limit of 65535 exceeded', got:`n$allOut65k"
    }

    Write-Host "   [OK] 65536 components failed with diagnostic error in $($sw.ElapsedMilliseconds) ms." -ForegroundColor Green

    # -------------------------------------------------------------------------
    # Part 2: Warning when totalComps > 1024 (1025 components)
    # -------------------------------------------------------------------------
    Write-Host "2. Testing warning > 1024 (1025 components)..." -ForegroundColor Cyan
    $file1025 = Join-Path $tempDir "test_1025_comps.ecs"
    $sb2 = [System.Text.StringBuilder]::new()
    for ($i = 1; $i -le 1025; $i++) {
        [void]$sb2.AppendLine("component C$i { v: i32 }")
    }
    [void]$sb2.AppendLine("fn main(): i32 { return 0; }")
    [System.IO.File]::WriteAllText($file1025, $sb2.ToString())

    $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
    $proc2 = Start-Process -FilePath $CliPath -ArgumentList "build `"$file1025`" --check" -NoNewWindow -Wait -PassThru -RedirectStandardOutput (Join-Path $tempDir "out1025.txt") -RedirectStandardError (Join-Path $tempDir "err1025.txt")
    $sw2.Stop()
    
    $out1025 = Get-Content (Join-Path $tempDir "out1025.txt") -Raw -ErrorAction SilentlyContinue
    $err1025 = Get-Content (Join-Path $tempDir "err1025.txt") -Raw -ErrorAction SilentlyContinue
    $allOut1025 = "$out1025`n$err1025"

    if ($allOut1025 -notmatch "compile time grows quadratically with component count \(see B7\)") {
        throw "FAIL: Expected warning 'compile time grows quadratically with component count (see B7)', got:`n$allOut1025"
    }

    if ($allOut1025 -notmatch "Project defines \d+ components \(> 1024\)") {
        throw "FAIL: Expected (> 1024) in warning, got:`n$allOut1025"
    }

    Write-Host "   [OK] 1025 components emitted warning 'compile time grows quadratically with component count (see B7)'." -ForegroundColor Green

    Write-Host "`nLEVEL 2 PROOF PASSED: Component thresholds (65535 error, 1024 warning) verified." -ForegroundColor Green
}
finally {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}
