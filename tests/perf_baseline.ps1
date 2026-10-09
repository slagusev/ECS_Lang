# Performance Baseline Measurement Suite for ECSLang
# Fixed methodology:
#   - Binary: tests/particles_100k.ecs
#   - Build: Release (-r --no-wait, -O3)
#   - Warm-up: 1 run (discarded)
#   - Samples: N = 5 runs
#   - Metrics: Min and Median for SPAWN_100K and TICK_100_FRAMES
#   - Output: Appended to tests/perf_baseline.csv and printed to console

param (
    [int]$Iterations = 5,
    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"
$CliPath = "src/ECSLang.CLI/bin/Debug/net9.0/ECSLang.CLI.exe"
$SourcePath = "tests/particles_100k.ecs"
$CsvPath = "tests/perf_baseline.csv"

if (-not (Test-Path $CliPath)) {
    Write-Host "ECSLang CLI binary not found. Running dotnet build..." -ForegroundColor Yellow
    dotnet build -c Debug 2>&1 | Out-Null
}

Write-Host "=== ECSLang Perf Baseline ($Iterations runs, Release -O3) ===" -ForegroundColor Cyan

# 1. Compile benchmark binary once in Release mode with codegen timing
Write-Host "Compiling $SourcePath in Release mode..." -ForegroundColor DarkGray
$swBuild = [System.Diagnostics.Stopwatch]::StartNew()
& $CliPath build $SourcePath -r --no-wait 2>&1 | Out-Null
$swBuild.Stop()
$buildTimeMs = $swBuild.Elapsed.TotalMilliseconds
Write-Host "  Codegen/Build: $($buildTimeMs.ToString('F2')) ms" -ForegroundColor DarkGray
$exePath = "tests/particles_100k.exe"

if (-not (Test-Path $exePath)) {
    throw "Compiled executable not found at $exePath"
}

# 2. Warm-up run (discarded)
Write-Host "Running 1 warm-up iteration..." -ForegroundColor DarkGray
& $exePath | Out-Null

# 3. Measurement samples
$spawnTimes = @()
$tickTimes = @()

for ($i = 1; $i -le $Iterations; $i++) {
    $out = & $exePath
    $spawnMatch = [regex]::Match($out, "SPAWN_100K:\s*([0-9\.]+)\s*ms")
    $tickMatch = [regex]::Match($out, "TICK_100_FRAMES:\s*([0-9\.]+)\s*ms")

    if ($spawnMatch.Success -and $tickMatch.Success) {
        $spawnVal = [double]::Parse($spawnMatch.Groups[1].Value, [System.Globalization.CultureInfo]::InvariantCulture)
        $tickVal = [double]::Parse($tickMatch.Groups[1].Value, [System.Globalization.CultureInfo]::InvariantCulture)
        $spawnTimes += $spawnVal
        $tickTimes += $tickVal
        Write-Host "  Run #$i : Spawn = $($spawnVal.ToString('F2')) ms, Tick = $($tickVal.ToString('F2')) ms" -ForegroundColor DarkGray
    } else {
        throw "Failed to parse benchmark output in run #$i : $out"
    }
}

# 4. Statistical analysis (Min & Median)
$sortedSpawn = $spawnTimes | Sort-Object
$sortedTick = $tickTimes | Sort-Object

$spawnMin = $sortedSpawn[0]
$tickMin = $sortedTick[0]

$mid = [math]::Floor($Iterations / 2)
if ($Iterations % 2 -eq 0) {
    $spawnMed = ($sortedSpawn[$mid - 1] + $sortedSpawn[$mid]) / 2.0
    $tickMed = ($sortedTick[$mid - 1] + $sortedTick[$mid]) / 2.0
} else {
    $spawnMed = $sortedSpawn[$mid]
    $tickMed = $sortedTick[$mid]
}

# 5. Git commit info
$commitHash = (& git rev-parse --short HEAD).Trim()
$dateStr = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")

# 6. CSV record
if (-not (Test-Path $CsvPath)) {
    "Commit,Date,Runs,BuildMs,SpawnMinMs,SpawnMedianMs,TickMinMs,TickMedianMs,Notes" | Out-File -FilePath $CsvPath -Encoding utf8
} else {
    $header = (Get-Content $CsvPath -TotalCount 1)
    if ($header -notmatch "BuildMs") {
        # Migrate header
        $lines = Get-Content $CsvPath
        $lines[0] = "Commit,Date,Runs,BuildMs,SpawnMinMs,SpawnMedianMs,TickMinMs,TickMedianMs,Notes"
        $lines | Set-Content $CsvPath -Encoding utf8
    }
}

"$commitHash,$dateStr,$Iterations,$($buildTimeMs.ToString('F2')),$($spawnMin.ToString('F2')),$($spawnMed.ToString('F2')),$($tickMin.ToString('F2')),$($tickMed.ToString('F2')),`"$Notes`"" | Out-File -FilePath $CsvPath -Append -Encoding utf8

Write-Host "`n--- Benchmark Results ---" -ForegroundColor Green
Write-Host "Commit        : $commitHash"
Write-Host "Codegen/Build : $($buildTimeMs.ToString('F2')) ms"
Write-Host "Samples       : $Iterations"
Write-Host "Spawn (Min)   : $($spawnMin.ToString('F2')) ms"
Write-Host "Spawn (Median): $($spawnMed.ToString('F2')) ms"
Write-Host "Tick (Min)    : $($tickMin.ToString('F2')) ms"
Write-Host "Tick (Median) : $($tickMed.ToString('F2')) ms"
Write-Host "Recorded to   : $CsvPath"
