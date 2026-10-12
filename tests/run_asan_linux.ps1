# Run ASan Verification for Linux-target IR on Ubuntu Runner
param (
    [string]$CliDll = "src/ECSLang.CLI/bin/Release/net9.0/ECSLang.CLI.dll",
    [string]$ClangCmd = "clang"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Starting ECSLang AddressSanitizer (ASan) Verification Suite ===" -ForegroundColor Cyan
Write-Host "Compiler DLL: $CliDll"
Write-Host "Clang: $(& $ClangCmd --version | Select-Object -First 1)"

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_asan_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

$testList = @(
    # 1. Primary stress test: 72 components, 2 words, all migration/SoA dynamic addressing paths
    @{ Name = "canary_70_components"; Path = "tests/canary_70_components.ecs"; ExpectedExit = 0 },
    # 2. Structural & boundary tests
    @{ Name = "hierarchy_childof"; Path = "tests/hierarchy_childof.ecs"; ExpectedExit = 0 },
    @{ Name = "bulk_spawn_test"; Path = "tests/bulk_spawn_test.ecs"; ExpectedExit = 0 },
    # 3. Headless non-parallel console examples
    @{ Name = "01_hello_world"; Path = "examples/01_hello_world.ecs"; ExpectedExit = 0 },
    @{ Name = "02_variables_and_math"; Path = "examples/02_variables_and_math.ecs"; ExpectedExit = 0 },
    @{ Name = "03_functions_and_flow"; Path = "examples/03_functions_and_flow.ecs"; ExpectedExit = 0 },
    @{ Name = "04_structs_and_methods"; Path = "examples/04_structs_and_methods.ecs"; ExpectedExit = 0 },
    @{ Name = "05_enums_and_matching"; Path = "examples/05_enums_and_matching.ecs"; ExpectedExit = 0 },
    @{ Name = "06_dynamic_collections"; Path = "examples/06_dynamic_collections.ecs"; ExpectedExit = 0 },
    @{ Name = "07_closures_and_generics"; Path = "examples/07_closures_and_generics.ecs"; ExpectedExit = 0 },
    @{ Name = "08_ecs_basics"; Path = "examples/08_ecs_basics.ecs"; ExpectedExit = 0 },
    @{ Name = "09_ecs_command_buffer"; Path = "examples/09_ecs_command_buffer.ecs"; ExpectedExit = 0 },
    @{ Name = "10_ecs_events_and_observers"; Path = "examples/10_ecs_events_and_observers.ecs"; ExpectedExit = 0 },
    @{ Name = "11_ecs_archetypes_and_filters"; Path = "examples/11_ecs_archetypes_and_filters.ecs"; ExpectedExit = 0 },
    @{ Name = "12_ecs_multi_world"; Path = "examples/12_ecs_multi_world.ecs"; ExpectedExit = 0 },
    @{ Name = "break_continue_test"; Path = "examples/break_continue_test.ecs"; ExpectedExit = 0 },
    @{ Name = "cast_test"; Path = "examples/cast_test.ecs"; ExpectedExit = 0 },
    @{ Name = "const_test"; Path = "examples/const_test.ecs"; ExpectedExit = 0 },
    @{ Name = "error_propagation_test"; Path = "examples/error_propagation_test.ecs"; ExpectedExit = 0 },
    @{ Name = "file_io_test"; Path = "examples/file_io_test.ecs"; ExpectedExit = 0 },
    @{ Name = "resource_main_test"; Path = "examples/resource_main_test.ecs"; ExpectedExit = 0 },
    @{ Name = "string_ops_test"; Path = "examples/string_ops_test.ecs"; ExpectedExit = 0 }
)

$passed = 0
$failed = 0

foreach ($t in $testList) {
    $tName = $t.Name
    $tSrc = $t.Path
    $expected = $t.ExpectedExit

    Write-Host "[TEST] $tName ($tSrc)..." -NoNewline

    $llPath = Join-Path $tempDir "$tName.ll"
    $binPath = Join-Path $tempDir "$tName.asan"

    # Step A: Compile with ECSLang to Linux-target LLVM IR
    $buildOut = & dotnet $CliDll build $tSrc --target x86_64-unknown-linux-gnu --emit-ir -o $binPath 2>&1
    if ($LASTEXITCODE -ne 0 -or (-not (Test-Path $llPath))) {
        Write-Host " [BUILD FAIL]" -ForegroundColor Red
        Write-Host $buildOut
        $failed++
        continue
    }

    # Step B: Compile with Clang AddressSanitizer
    $clangOut = & $ClangCmd -fsanitize=address $llPath -o $binPath -lm -lpthread -ldl -lrt 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host " [ASAN COMPILE FAIL]" -ForegroundColor Red
        Write-Host $clangOut
        $failed++
        continue
    }

    # Step C: Execute under AddressSanitizer
    $env:ASAN_OPTIONS = "detect_leaks=1:abort_on_error=1"
    $runOut = & $binPath 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq $expected) {
        Write-Host " [PASSED]" -ForegroundColor Green
        $passed++
    } else {
        Write-Host " [RUN FAIL: ExitCode $exitCode, expected $expected]" -ForegroundColor Red
        Write-Host $runOut
        $failed++
    }
}

Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "=== ASan Verification Summary ===" -ForegroundColor Cyan
Write-Host "Passed: $passed / $($testList.Count)"
Write-Host "Failed: $failed"

if ($failed -gt 0) {
    exit 1
}

exit 0
