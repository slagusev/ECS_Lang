$ErrorActionPreference = "Stop"

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_a4_sys_world_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$exe = Join-Path $tempDir "illegal_sys.exe"

try {
    $cli = "$PSScriptRoot\..\src\ECSLang.CLI\bin\Release\net9.0\ECSLang.CLI.exe"
    Write-Host "Compiling negative test tests/a4_world_in_system_forbidden.ecs..."
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $cli
    $psi.Arguments = "build `"$PSScriptRoot\a4_world_in_system_forbidden.ecs`" -o `"$exe`" --no-wait"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    $exitCode = $proc.ExitCode
    $allOutput = "$stdout`n$stderr"
    Write-Host "Compiler Exit Code: $exitCode"
    Write-Host "Output:`n$allOutput"

    if ($exitCode -eq 0) {
        Write-Error "TEST FAILED: Compilation should have failed, but succeeded with exit code 0!"
        exit 1
    }

    if (-not ($allOutput -like "*Calling 'world.set_Position' is forbidden inside system bodies*")) {
        Write-Error "TEST FAILED: Expected diagnostic error message regarding 'world.set_Position' forbidden not found!"
        exit 1
    }

    if (-not ($allOutput -like "*restricted to 'world.emit_*' only*")) {
        Write-Error "TEST FAILED: Expected suggestion regarding 'world.emit_*' not found!"
        exit 1
    }

    Write-Host "LEVEL 2 PROOF (PART 1) PASSED: Direct world mutation rejected at compile time."

    Write-Host "Compiling negative test tests/a4_world_has_in_system_forbidden.ecs..."
    $psi2 = New-Object System.Diagnostics.ProcessStartInfo
    $psi2.FileName = $cli
    $psi2.Arguments = "build `"$PSScriptRoot\a4_world_has_in_system_forbidden.ecs`" -o `"$exe`" --no-wait"
    $psi2.UseShellExecute = $false
    $psi2.RedirectStandardOutput = $true
    $psi2.RedirectStandardError = $true

    $proc2 = [System.Diagnostics.Process]::Start($psi2)
    $stdout2 = $proc2.StandardOutput.ReadToEnd()
    $stderr2 = $proc2.StandardError.ReadToEnd()
    $proc2.WaitForExit()

    $exitCode2 = $proc2.ExitCode
    $allOutput2 = "$stdout2`n$stderr2"
    Write-Host "Compiler Exit Code: $exitCode2"
    Write-Host "Output:`n$allOutput2"

    if ($exitCode2 -eq 0) {
        Write-Error "TEST FAILED: Query compilation should have failed, but succeeded with exit code 0!"
        exit 1
    }

    if (-not ($allOutput2 -like "*Calling 'world.has_Position' is forbidden inside system bodies*")) {
        Write-Error "TEST FAILED: Expected diagnostic error message regarding 'world.has_Position' forbidden not found!"
        exit 1
    }

    Write-Host "LEVEL 2 PROOF PASSED: Both direct mutation and query of world in system body rejected at compile time."
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
exit 0
