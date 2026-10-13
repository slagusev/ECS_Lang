param (
    [string]$CliDll = "src/ECSLang.CLI/bin/Release/net9.0/ECSLang.CLI.dll",
    [string]$ClangCmd = ""
)

$ErrorActionPreference = "Stop"

if (-not $ClangCmd) {
    if (Get-Command "clang-20" -ErrorAction SilentlyContinue) {
        $ClangCmd = "clang-20"
    } elseif (Get-Command "clang" -ErrorAction SilentlyContinue) {
        $ClangCmd = "clang"
    } else {
        throw "Neither clang-20 nor clang found on PATH"
    }
}

$clangVerStr = (& $ClangCmd --version | Select-Object -First 1)
$isLegacyClang = $false
if ($clangVerStr -match 'clang version (\d+)') {
    if ([int]$Matches[1] -lt 19) {
        $isLegacyClang = $true
    }
}

Write-Host "=== Starting ECSLang ThreadSanitizer (TSan) Verification Suite ===" -ForegroundColor Cyan
Write-Host "Compiler DLL: $CliDll"
Write-Host "Clang: $clangVerStr"

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_tsan_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

# Compile headless Raylib stubs for console and profiler runtime calls
$stubsSrc = Join-Path $tempDir "raylib_headless_stubs.c"
$stubsObj = Join-Path $tempDir "raylib_headless_stubs.o"
@'
#include <stdint.h>
void InitWindow(int32_t w, int32_t h, const char* t) {}
int8_t IsWindowReady(void) { return 0; }
int8_t WindowShouldClose(void) { return 1; }
void CloseWindow(void) {}
void SetTargetFPS(int32_t fps) {}
int32_t GetFPS(void) { return 60; }
float GetFrameTime(void) { return 0.016f; }
double GetTime(void) { return 0.0; }
void BeginDrawing(void) {}
void EndDrawing(void) {}
void ClearBackground(int32_t c) {}
void DrawText(const char* t, int32_t x, int32_t y, int32_t s, int32_t c) {}
void DrawRectangle(int32_t x, int32_t y, int32_t w, int32_t h, int32_t c) {}
void DrawRectangleLines(int32_t x, int32_t y, int32_t w, int32_t h, int32_t c) {}
void DrawCircle(int32_t x, int32_t y, float r, int32_t c) {}
void DrawLine(int32_t x1, int32_t y1, int32_t x2, int32_t y2, int32_t c) {}
int8_t IsKeyDown(int32_t k) { return 0; }
int8_t IsKeyPressed(int32_t k) { return 0; }
int8_t IsKeyReleased(int32_t k) { return 0; }
int8_t IsKeyUp(int32_t k) { return 0; }
int32_t GetMouseX(void) { return 0; }
int32_t GetMouseY(void) { return 0; }
int8_t IsMouseButtonDown(int32_t b) { return 0; }
int8_t IsMouseButtonPressed(int32_t b) { return 0; }
void LoadTexture(void* out, const char* p) {}
void DrawTexture(void* t, int32_t x, int32_t y, int32_t c) {}
void DrawTexturePro(void* t, void* src, void* dst, int64_t o, float rot, int32_t tint) {}
void UnloadTexture(void* t) {}
void InitAudioDevice(void) {}
void CloseAudioDevice(void) {}
int8_t IsAudioDeviceReady(void) { return 0; }
void LoadSound(void* out, const char* p) {}
void PlaySound(void* s) {}
void StopSound(void* s) {}
void PauseSound(void* s) {}
void ResumeSound(void* s) {}
int8_t IsSoundPlaying(void* s) { return 0; }
void SetSoundVolume(void* s, float v) {}
void UnloadSound(void* s) {}
void BeginMode2D(void* c) {}
void EndMode2D(void) {}
'@ | Out-File -FilePath $stubsSrc -Encoding ascii

& $ClangCmd -fsanitize=thread -c $stubsSrc -o $stubsObj
if ($LASTEXITCODE -ne 0) {
    throw "Failed to compile Raylib headless stubs for TSan"
}

$testList = @(
    # Multithreaded string arena stress test (50,000 iterations under concurrent string formatting)
    @{ Name = "stress_parallel_string_arena"; Path = "tests/stress_parallel_string_arena.ecs"; ExpectedExit = 0 },
    # Parallel vs Sequential invariant test (100,000 entities)
    @{ Name = "verify_b5_1_parallel_invariant"; Path = "tests/verify_b5_1_parallel_invariant.ecs"; ExpectedExit = 0 },
    # Parallel pipeline benchmark
    @{ Name = "13_multithreading_benchmark"; Path = "examples/13_multithreading_benchmark.ecs"; ExpectedExit = 0 },
    # Parallel auto DAG test
    @{ Name = "parallel_auto_test"; Path = "examples/parallel_auto_test.ecs"; ExpectedExit = 0 },
    # OLAP big data analyzer
    @{ Name = "olap_bigdata_analyzer"; Path = "examples/olap_bigdata_analyzer.ecs"; ExpectedExit = 0 }
)

$passed = 0
$failed = 0
$results = @()

foreach ($t in $testList) {
    $tName = $t.Name
    $tSrc = $t.Path
    $expected = $t.ExpectedExit
    Write-Host "[TEST] $tName ($tSrc)..." -NoNewline

    $tempSrc = Join-Path $tempDir "$tName.ecs"
    Copy-Item -Path $tSrc -Destination $tempSrc -Force

    $llPath = Join-Path $tempDir "$tName.ll"
    $binPath = Join-Path $tempDir "$tName.tsan"

    # Step A: Compile with ECSLang to Linux-target LLVM IR (skip linking via --emit-obj)
    $buildOut = & dotnet $CliDll build $tempSrc --target x86_64-unknown-linux-gnu --emit-ir --emit-obj --no-wait 2>&1
    if ($LASTEXITCODE -ne 0 -or (-not (Test-Path $llPath))) {
        Write-Host " [BUILD FAIL]" -ForegroundColor Red
        Write-Host $buildOut
        $flatOut = ($buildOut -join ' ') -replace '[\r\n]+', ' '
        Write-Host "::error title=$tName Build Failed::$flatOut"
        $failed++
        $results += "| $tName | ❌ Build Failed | - |"
        continue
    }

    # Step B: Compile with Clang ThreadSanitizer
    if ($isLegacyClang) {
        $rawContent = [System.IO.File]::ReadAllText($llPath)
        $cleanContent = $rawContent.Replace(" nuw ", " ")
        [System.IO.File]::WriteAllText($llPath, $cleanContent)
    }

    $clangOut = & $ClangCmd -fsanitize=thread $llPath $stubsObj -o $binPath -lm -lpthread -ldl -lrt 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host " [TSAN COMPILE FAIL]" -ForegroundColor Red
        Write-Host $clangOut
        $flatClang = ($clangOut -join ' ') -replace '[\r\n]+', ' '
        Write-Host "::error title=$tName TSan Link Failed::$flatClang"
        $failed++
        $results += "| $tName | ❌ TSan Link Failed | - |"
        continue
    }

    # Step C: Execute under ThreadSanitizer
    $env:TSAN_OPTIONS = "abort_on_error=1:report_bugs=1"
    Push-Location $tempDir
    try {
        $runOut = & $binPath 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -eq $expected) {
        Write-Host " [PASSED]" -ForegroundColor Green
        Write-Host "::notice title=$tName::PASSED (ExitCode 0 under ThreadSanitizer)"
        $passed++
        $results += "| $tName | ✅ Passed | 0 |"
    } else {
        Write-Host " [RUN FAIL: ExitCode $exitCode, expected $expected]" -ForegroundColor Red
        Write-Host "Output: $runOut"
        $flatRun = ($runOut -join ' ') -replace '[\r\n]+', ' '
        Write-Host "::error title=$tName Execution Failed (Exit $exitCode)::$flatRun"
        $failed++
        $results += "| $tName | ❌ Run Failed (Exit $exitCode) | - |"
    }
}

Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "=== TSan Verification Summary ===" -ForegroundColor Cyan
Write-Host "Passed: $passed / $($testList.Count)"
Write-Host "Failed: $failed"

if ($env:GITHUB_STEP_SUMMARY) {
    "### ThreadSanitizer (TSan) Verification Summary`n" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append
    "- **Total Tests**: $($testList.Count)`n- **Passed**: $passed`n- **Failed**: $failed`n" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append
    "| Test | Status | Exit Code |`n|---|---|---|`n" + ($results -join "`n") | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append
}

if ($failed -gt 0) {
    exit 1
}

exit 0
