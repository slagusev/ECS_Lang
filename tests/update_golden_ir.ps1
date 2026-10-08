# Update golden LLVM IR baselines for the 6 test programs
$files = @(
    @{ Source = "examples/08_ecs_basics.ecs"; Dest = "tests/golden_ir/08_ecs_basics.ll" },
    @{ Source = "examples/09_ecs_command_buffer.ecs"; Dest = "tests/golden_ir/09_ecs_command_buffer.ll" },
    @{ Source = "examples/11_ecs_archetypes_and_filters.ecs"; Dest = "tests/golden_ir/11_ecs_archetypes_and_filters.ll" },
    @{ Source = "examples/cast_test.ecs"; Dest = "tests/golden_ir/cast_test.ll" },
    @{ Source = "tests/hierarchy_childof.ecs"; Dest = "tests/golden_ir/hierarchy_childof.ll" },
    @{ Source = "tests/bulk_spawn_test.ecs"; Dest = "tests/golden_ir/bulk_spawn_test.ll" }
)

Write-Host "=== Updating Golden IR Baselines ===" -ForegroundColor Cyan
foreach ($f in $files) {
    Write-Host "  -> Compiling $($f.Source) to $($f.Dest)..."
    & src/ECSLang.CLI/bin/Debug/net9.0/ECSLang.CLI.exe build $f.Source --emit-ir --emit-obj 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile $($f.Source)"
    }
    $genIr = [System.IO.Path]::ChangeExtension($f.Source, ".ll")
    $genObj = [System.IO.Path]::ChangeExtension($f.Source, ".obj")
    Move-Item -Path $genIr -Destination $f.Dest -Force
    if (Test-Path $genObj) {
        Remove-Item -Path $genObj -Force
    }
}
Write-Host "=== All 6 Golden IR Baselines Updated Successfully! ===" -ForegroundColor Green
