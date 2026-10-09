$ErrorActionPreference = "Stop"

$tempDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "ecslang_b1_pause_" + [System.Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($tempDir) | Out-Null

try {
    $srcFile = "$PSScriptRoot\..\examples\08_ecs_basics.ecs"
    $outLL = "$tempDir\out.ll"

    Write-Host "Compiling 08_ecs_basics.ecs with --target x86_64-unknown-linux-gnu --emit-llvm..."
    $cli = "$PSScriptRoot\..\src\ECSLang.CLI\bin\Release\net9.0\ECSLang.CLI.exe"
    & $cli build $srcFile --target x86_64-unknown-linux-gnu --emit-llvm -o $outLL
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Compilation for Linux target failed"
        exit 1
    }

    # Find the emitted .ll file
    $emittedLL = "$PSScriptRoot\..\examples\08_ecs_basics.ll"
    if (-not (Test-Path $emittedLL)) {
        $emittedLL = $outLL
    }
    
    $irContent = Get-Content $emittedLL -Raw

    if ($irContent -match "define void @ecs_spin_acquire") {
        Write-Host "Found @ecs_spin_acquire definition in IR."
    } else {
        Write-Error "TEST FAILED: @ecs_spin_acquire not found in Linux IR"
        exit 1
    }

    if ($irContent -match "call void @llvm\.x86\.sse2\.pause\(\)") {
        Write-Host "Found call void @llvm.x86.sse2.pause() in spinlock backoff loop."
    } else {
        Write-Error "TEST FAILED: llvm.x86.sse2.pause call not found in IR"
        exit 1
    }

    if ($irContent -match "declare void @llvm\.x86\.sse2\.pause\(\)") {
        Write-Host "Found declare void @llvm.x86.sse2.pause() in IR."
    } else {
        Write-Error "TEST FAILED: declare void @llvm.x86.sse2.pause not found in IR"
        exit 1
    }

    Write-Host "LEVEL 3 PROOF PASSED: Hardware pause instruction is verified in ecs_spin_acquire backoff loop."
    exit 0
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    $emittedLL = "$PSScriptRoot\..\examples\08_ecs_basics.ll"
    if (Test-Path $emittedLL) {
        Remove-Item $emittedLL -Force -ErrorAction SilentlyContinue
    }
}
