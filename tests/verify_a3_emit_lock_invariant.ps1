$ErrorActionPreference = "Stop"

$tempDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "ecslang_a3_emit_lock_" + [System.Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($tempDir) | Out-Null

try {
    $srcFile = "$PSScriptRoot\..\examples\10_ecs_events_and_observers.ecs"
    $outLL = "$tempDir\events.ll"
    $outExe = "$tempDir\events.exe"

    Write-Host "Compiling 10_ecs_events_and_observers.ecs to LLVM IR..."
    $cli = "$PSScriptRoot\..\src\ECSLang.CLI\bin\Release\net9.0\ECSLang.CLI.exe"
    & $cli build $srcFile --no-wait --emit-llvm -o $outExe
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to compile 10_ecs_events_and_observers.ecs"
        exit 1
    }

    $emittedLL = "$PSScriptRoot\..\examples\10_ecs_events_and_observers.ll"
    $irContent = Get-Content $emittedLL -Raw

    if ($irContent -match "emit_lock_slot = getelementptr inbounds nuw %struct\.EcsWorld, ptr %world, i32 0, i32 11") {
        Write-Host "Found emit_lock_slot (field 11) in world_emit_*."
    } else {
        Write-Error "TEST FAILED: emit_lock_slot not found at field 11 in world_emit_*"
        exit 1
    }

    if ($irContent -match "call void @AcquireSRWLockExclusive\(ptr %emit_lock_slot\)") {
        Write-Host "Found AcquireSRWLockExclusive on emit_lock_slot."
    } else {
        Write-Error "TEST FAILED: AcquireSRWLockExclusive not found on emit_lock_slot"
        exit 1
    }

    if ($irContent -match "call void @ReleaseSRWLockExclusive\(ptr %emit_lock_slot\)") {
        Write-Host "Found ReleaseSRWLockExclusive on emit_lock_slot."
    } else {
        Write-Error "TEST FAILED: ReleaseSRWLockExclusive not found on emit_lock_slot"
        exit 1
    }

    if ($irContent -match "swap_emit_lock_slot = getelementptr inbounds nuw %struct\.EcsWorld, ptr %world, i32 0, i32 11") {
        Write-Host "Found swap_emit_lock_slot (field 11) in world_swap_events."
    } else {
        Write-Error "TEST FAILED: swap_emit_lock_slot not found in world_swap_events"
        exit 1
    }

    # Execute binary to verify Level 1 runtime proof (piped into Out-Null so wait_key doesn't block)
    Write-Host "Running 10_ecs_events_and_observers.exe..."
    & $outExe 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Execution of 10_ecs_events_and_observers failed with exit code $LASTEXITCODE"
        exit 1
    }

    Write-Host "LEVEL 2 & 3 PROOFS PASSED: Dedicated emit_lock synchronization and runtime execution verified."
    exit 0
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    $emittedLL = "$PSScriptRoot\..\examples\10_ecs_events_and_observers.ll"
    if (Test-Path $emittedLL) {
        Remove-Item $emittedLL -Force -ErrorAction SilentlyContinue
    }
}
