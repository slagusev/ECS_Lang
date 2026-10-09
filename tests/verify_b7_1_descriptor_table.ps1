# Level 2 Invariant Test for Step B7.1: Component Descriptor Table
# Validates structural ABI properties and values of @_ecs_component_descriptors

$ErrorActionPreference = "Stop"
$cli = $(if (Test-Path "src/ECSLang.CLI/bin/Release/net9.0/ECSLang.CLI.exe") { "src/ECSLang.CLI/bin/Release/net9.0/ECSLang.CLI.exe" } else { "src/ECSLang.CLI/bin/Debug/net9.0/ECSLang.CLI.exe" })
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_b7_1_test_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

Write-Host "=== Running Level 2 Invariant Test: Step B7.1 Descriptor Table ===" -ForegroundColor Cyan

try {
    # 1. Test 08_ecs_basics.ecs (4 components: ChildOf, Health, Position, Velocity)
    $src1 = Join-Path $tempDir "08_ecs_basics.ecs"
    Copy-Item "examples/08_ecs_basics.ecs" $src1 -Force
    & $cli build $src1 --emit-ir --emit-obj 2>&1 | Out-Null
    $ir1Path = Join-Path $tempDir "08_ecs_basics.ll"

    if (-not (Test-Path $ir1Path)) {
        throw "Failed to emit IR for 08_ecs_basics.ecs"
    }

    $ir1 = Get-Content $ir1Path -Raw

    # Invariant 1: %struct.ComponentDesc declaration
    if ($ir1 -notmatch "%struct\.ComponentDesc = type \{ i64, i64, i32, i32 \}") {
        throw "INVARIANT VIOLATION: %struct.ComponentDesc layout is not { i64, i64, i32, i32 } in 08_ecs_basics.ll"
    }
    Write-Host "  [OK] Invariant 1: %struct.ComponentDesc type layout verified" -ForegroundColor Green

    # Invariant 2: @_ecs_component_descriptors array of 4 components
    if ($ir1 -notmatch "@_ecs_component_descriptors = internal constant \[4 x %struct\.ComponentDesc\]") {
        throw "INVARIANT VIOLATION: @_ecs_component_descriptors with [4 x %struct.ComponentDesc] not found in 08_ecs_basics.ll"
    }
    Write-Host "  [OK] Invariant 2: @_ecs_component_descriptors [4 x ...] table verified" -ForegroundColor Green

    # 2. Test canary_70_components.ecs (72 components, WORDS=2)
    $src2 = Join-Path $tempDir "canary.ecs"
    Copy-Item "tests/canary_70_components.ecs" $src2 -Force
    & $cli build $src2 --emit-ir --emit-obj 2>&1 | Out-Null
    $ir2Path = Join-Path $tempDir "canary.ll"

    if (-not (Test-Path $ir2Path)) {
        throw "Failed to emit IR for canary_70_components.ecs"
    }

    $ir2 = Get-Content $ir2Path -Raw

    # Invariant 3: Canary has 72 descriptors
    if ($ir2 -notmatch "@_ecs_component_descriptors = internal constant \[72 x %struct\.ComponentDesc\]") {
        throw "INVARIANT VIOLATION: Canary does not have [72 x %struct.ComponentDesc] in descriptor table"
    }
    Write-Host "  [OK] Invariant 3: Multi-word canary has [72 x %struct.ComponentDesc] in table" -ForegroundColor Green

    Write-Host "All Step B7.1 Invariants PASSED!" -ForegroundColor Green
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
    }
}
