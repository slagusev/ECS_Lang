param (
    [string]$CliPath = "src/ECSLang.CLI/bin/Debug/net9.0/ECSLang.CLI.exe"
)

$files = Get-ChildItem -Path examples -Filter *.ecs | Sort-Object Name
$total = $files.Count
$builtCount = 0
$runnedCount = 0
$skippedRunCount = 0

# Documented skip list for interactive GUI / blocking network servers
$skipRun = @{
    "14_pure_dod_network.ecs"    = "Blocking TCP server loop (requires active client)"
    "15_raylib_2d_graphics.ecs"  = "Interactive Raylib 2D GUI window loop"
    "16_gui_widgets.ecs"         = "Interactive GUI window loop (mouse/keyboard input)"
    "17_bouncing_balls.ecs"      = "Interactive Raylib graphics window loop"
    "18_arcade_void_defender.ecs"= "Interactive Raylib game loop"
    "raylib_media_test.ecs"      = "Interactive Raylib audio/video window"
}

Write-Host "=== Validating All $total Examples (Level 1 Proof Suite) ===" -ForegroundColor Cyan

foreach ($f in $files) {
    $name = $f.Name
    $exePath = [System.IO.Path]::ChangeExtension($f.FullName, ".exe")
    $objPath = [System.IO.Path]::ChangeExtension($f.FullName, ".obj")

    # 1. Build step
    & $CliPath build $f.FullName --no-wait 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [FAIL-BUILD] $name" -ForegroundColor Red
        throw "Build failed for $name"
    }
    Write-Host "  [BUILT]    $name" -ForegroundColor Green
    $builtCount++

    # 2. Run step (Level 1)
    if ($skipRun.ContainsKey($name)) {
        $reason = $skipRun[$name]
        Write-Host "  [SKIP-RUN] $name ($reason)" -ForegroundColor DarkYellow
        $skippedRunCount++
    } else {
        & $exePath 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  [FAIL-RUN] $name (ExitCode: $LASTEXITCODE)" -ForegroundColor Red
            throw "Execution failed for $name with exit code $LASTEXITCODE"
        }
        Write-Host "  [RUNNED]   $name (ExitCode: 0)" -ForegroundColor Green
        $runnedCount++
    }

    if (Test-Path $exePath) { Remove-Item $exePath -Force }
    if (Test-Path $objPath) { Remove-Item $objPath -Force }
}

Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "  BUILT:       $builtCount / $total" -ForegroundColor Green
Write-Host "  RUNNED:      $runnedCount / $($total - $skippedRunCount) (ExitCode: 0)" -ForegroundColor Green
Write-Host "  SKIPPED RUN: $skippedRunCount (Documented GUI/Network)" -ForegroundColor Yellow
