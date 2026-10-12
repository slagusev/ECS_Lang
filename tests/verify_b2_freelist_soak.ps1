$ErrorActionPreference = "Stop"

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_b2_soak_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$exe = Join-Path $tempDir "soak_test.exe"

try {
    Write-Host "Compiling tests/verify_b2_freelist_soak.ecs (Release / O3)..."
    $cli = "$PSScriptRoot\..\src\ECSLang.CLI\bin\Release\net9.0\ECSLang.CLI.exe"
    & $cli build "$PSScriptRoot\verify_b2_freelist_soak.ecs" -o $exe -O3 --no-wait
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to compile verify_b2_freelist_soak.ecs"
        exit 1
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    $sw.Stop()

    $exitCode = $proc.ExitCode
    Write-Host "Process Exit Code: $exitCode, Elapsed: $($sw.ElapsedMilliseconds) ms"
    Write-Host "Output:`n$stdout"

    if ($exitCode -ne 0) {
        Write-Error "TEST FAILED: Expected exit code 0, got $exitCode"
        exit 1
    }

    if (-not ($stdout -like "*Completed 100,000 cycles.*")) {
        Write-Error "TEST FAILED: Soak test did not reach completion output"
        exit 1
    }

    Write-Host "LEVEL 2 PROOF PASSED: 100,000 spawn/despawn cycles completed in $($sw.ElapsedMilliseconds) ms without memory explosion."
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
exit 0
