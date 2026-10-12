$ErrorActionPreference = "Stop"

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_b2_aba_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$exe = Join-Path $tempDir "aba_test.exe"

try {
    Write-Host "Compiling tests/verify_b2_aba_invariant.ecs..."
    $cli = "$PSScriptRoot\..\src\ECSLang.CLI\bin\Release\net9.0\ECSLang.CLI.exe"
    & $cli build "$PSScriptRoot\verify_b2_aba_invariant.ecs" -o $exe --no-wait
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to compile verify_b2_aba_invariant.ecs"
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

    if (-not ($stdout -like "*has_Position(0) returned false as required for Null entity.*")) {
        Write-Error "TEST FAILED: Null entity check failed"
        exit 1
    }

    if (-not ($stdout -like "*has_Position(e1) returned false on stale handle as required.*")) {
        Write-Error "TEST FAILED: has_* -> false on stale handle check failed"
        exit 1
    }

    if ($exitCode -ne 1) {
        Write-Error "TEST FAILED: Expected exit code 1 (via rt_panic), but got $exitCode"
        exit 1
    }

    if (-not ($stdout -like "*Attempted to access entity with stale generation (ABA prevented).*")) {
        Write-Error "TEST FAILED: ABA diagnostic error message not found in stdout"
        exit 1
    }

    Write-Host "LEVEL 2 PROOF PASSED: Stale generation mutation aborted via rt_panic with exit code 1 and ABA prevention verified."
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
exit 0
