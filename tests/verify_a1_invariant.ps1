$ErrorActionPreference = "Stop"

$exe = "$PSScriptRoot\a1_test.exe"
if (-not (Test-Path $exe)) {
    Write-Host "Compiling tests/a1_no_resurrect_dead_entity.ecs..."
    & dotnet run --project "$PSScriptRoot\..\src\ECSLang.CLI" -- build "$PSScriptRoot\a1_no_resurrect_dead_entity.ecs" -o $exe
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to compile a1_no_resurrect_dead_entity.ecs"
        exit 1
    }
}

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

if ($exitCode -ne 1) {
    Write-Error "TEST FAILED: Expected exit code 1 (via rt_panic), but got $exitCode"
    exit 1
}

if (-not ($stdout -like "*Attempted to mutate despawned or dead entity*")) {
    Write-Error "TEST FAILED: Diagnostic error message not found in stdout"
    exit 1
}

Write-Host "LEVEL 2 PROOF PASSED: Dead entity mutation aborted via rt_panic with exit code 1 and correct diagnostic."
exit 0
