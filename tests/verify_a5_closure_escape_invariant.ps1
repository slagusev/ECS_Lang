$ErrorActionPreference = "Stop"

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_a5_closure_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$exe = Join-Path $tempDir "out.exe"

function Assert-NegativeTest {
    param(
        [string]$TestFile,
        [string]$ExpectedPattern
    )

    Write-Host "Verifying negative test $TestFile..."
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "dotnet"
    $psi.Arguments = "run --project `"$PSScriptRoot\..\src\ECSLang.CLI`" -- build `"$PSScriptRoot\$TestFile`" -o `"$exe`" --no-wait"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    $allOutput = "$stdout`n$stderr"
    if ($proc.ExitCode -eq 0) {
        Write-Error "TEST FAILED: $TestFile should have failed compilation, but exited with 0!"
        exit 1
    }

    if (-not ($allOutput -like "*$ExpectedPattern*")) {
        Write-Error "TEST FAILED: $TestFile output did not match expected pattern '*$ExpectedPattern*'. Output was:`n$allOutput"
        exit 1
    }
    Write-Host "PASSED: $TestFile rejected as expected."
}

try {
    # 1. Escape via return
    Assert-NegativeTest "a5_escape_return_forbidden.ecs" "Capturing closure cannot"

    # 2. Escape via parameter to user function
    Assert-NegativeTest "a5_escape_param_forbidden.ecs" "Capturing closure cannot"

    # 3. Escape via struct field
    Assert-NegativeTest "a5_escape_struct_forbidden.ecs" "Capturing closure cannot"

    # 4. Escape via collection push
    Assert-NegativeTest "a5_escape_collection_forbidden.ecs" "Capturing closure cannot"

    # 5. Allowed closure test (positive test)
    Write-Host "Compiling allowed closure test tests/a5_closure_allowed.ecs..."
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "dotnet"
    $psi.Arguments = "run --project `"$PSScriptRoot\..\src\ECSLang.CLI`" -- build `"$PSScriptRoot\a5_closure_allowed.ecs`" -o `"$exe`" --no-wait"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    if ($proc.ExitCode -ne 0) {
        Write-Error "TEST FAILED: a5_closure_allowed.ecs failed compilation:`n$stdout`n$stderr"
        exit 1
    }

    Write-Host "Running $exe..."
    $psiRun = New-Object System.Diagnostics.ProcessStartInfo
    $psiRun.FileName = $exe
    $psiRun.UseShellExecute = $false
    $psiRun.RedirectStandardOutput = $true
    $psiRun.RedirectStandardError = $true

    $pRun = [System.Diagnostics.Process]::Start($psiRun)
    $runOut = $pRun.StandardOutput.ReadToEnd()
    $pRun.WaitForExit()

    if ($pRun.ExitCode -ne 0) {
        Write-Error "TEST FAILED: Runtime exit code was $($pRun.ExitCode)"
        exit 1
    }

    if (-not ($runOut -like "*42*") -or -not ($runOut -like "*142*")) {
        Write-Error "TEST FAILED: Runtime output incorrect:`n$runOut"
        exit 1
    }

    Write-Host "PASSED: a5_closure_allowed.ecs compiled and ran correctly with expected output."
    Write-Host "LEVEL 2 PROOF PASSED: Type-level prohibition of capturing closures across escaping boundaries verified."
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
