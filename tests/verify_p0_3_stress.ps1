# P0.3S Stress Verification Suite
# Runs 2 parallel worker systems x 100,000 event emissions (total 200,000) for 3 runs in Release -O3.

$ErrorActionPreference = "Stop"
$CliPath = "src/ECSLang.CLI/bin/Debug/net9.0/ECSLang.CLI.exe"
$SourcePath = "tests/stress_emit_parallel.ecs"

Write-Host "=== P0.3S Stress Test: Parallel Event Emission under dedicated emit_lock ===" -ForegroundColor Cyan
Write-Host "Compiling $SourcePath in Release mode (-O3)..." -ForegroundColor DarkGray
& $CliPath build $SourcePath -r --no-wait 2>&1 | Out-Null
$exePath = "tests/stress_emit_parallel.exe"

if (-not (Test-Path $exePath)) {
    throw "Compiled binary not found: $exePath"
}

$runs = 3
$results = @()

for ($i = 1; $i -le $runs; $i++) {
    $out = & $exePath
    $timeMatch = [regex]::Match($out, "TOTAL_TIME:\s*([0-9\.]+)\s*ms")
    $eventsMatch = [regex]::Match($out, "TOTAL_EVENTS:\s*([0-9]+)")

    if (-not $eventsMatch.Success) {
        throw "Run #$i failed to output TOTAL_EVENTS: $out"
    }

    $eventCount = [int]::Parse($eventsMatch.Groups[1].Value)
    $elapsedMs = if ($timeMatch.Success) { $timeMatch.Groups[1].Value } else { "N/A" }

    Write-Host "  Run #$i : Events = $eventCount / 200000, Time = $elapsedMs ms" -ForegroundColor Green
    if ($eventCount -ne 200000) {
        throw "P0.3S INVARIANT VIOLATED in run #$($i): expected 200000, got $eventCount"
    }
    $results += [PSCustomObject]@{ Run = $i; Events = $eventCount; TimeMs = $elapsedMs }
}

Remove-Item -Path $exePath -ErrorAction SilentlyContinue

Write-Host "`nLEVEL 4 PROOF PASSED: P0.3S parallel event emission stress verified across all $runs runs." -ForegroundColor Green
