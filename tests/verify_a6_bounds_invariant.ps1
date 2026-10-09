$ErrorActionPreference = "Stop"

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_a6_bounds_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$exe = Join-Path $tempDir "bounds_test.exe"

try {
    Write-Host "Compiling tests/a6_out_of_bounds_guard.ecs..."
    $cli = "$PSScriptRoot\..\src\ECSLang.CLI\bin\Release\net9.0\ECSLang.CLI.exe"
    & $cli build "$PSScriptRoot\a6_out_of_bounds_guard.ecs" -o $exe --no-wait
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to compile a6_out_of_bounds_guard.ecs"
        exit 1
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

    if (-not ($stdout -like "*Attempted to mutate out of bounds entity*")) {
        Write-Error "TEST FAILED: Diagnostic error message not found in stdout"
        exit 1
    }

    Write-Host "LEVEL 2 PROOF PASSED: Out of bounds entity access aborted via rt_panic with exit code 1 and correct diagnostic."
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
exit 0
