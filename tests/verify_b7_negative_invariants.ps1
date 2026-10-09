# tests/verify_b7_negative_invariants.ps1
# Level 2 Invariant Test: Verifies progressive elimination of unrolled loops (B7.2 - B7.5)
# Single Source of Truth for B7 legacy patterns.

param (
    [string]$IrDir = "tests/golden_ir",
    [switch]$CheckFresh
)

$ErrorActionPreference = "Stop"

# Canonical pattern definitions across B7 series
$LegacyPatterns = [ordered]@{
    "B7.2_SetMigrationCopy" = @(
        "mask_w_\w+(?<!_sw)_set"
        "cur_mask_\w+(?<!_sw)_set"
        "copy_(?!rem_|add_|sw_)\w+:\s+;\s+preds"
        "skip_(?!rem_|add_|sw_|col_|swap_)\w+:\s+;\s+preds"
    )
    "B7.3_AddMigrationCopy" = @(
        "mask_w_\w+_add"
        "cur_mask_\w+_add"
        "copy_add_\w+:"
        "src_col_\w+_add"
        "dst_col_\w+_add"
    )
    "B7.3_RemoveMigrationCopy" = @(
        "mask_w_\w+_rem"
        "cur_mask_\w+_rem"
        "copy_rem_\w+:"
        "src_col_\w+_rem"
        "dst_col_\w+_rem"
    )
    "B7.4_SwapRemoveCopy" = @(
        "mask_w_\w+_sw_"
        "cur_mask_\w+_sw_"
        "sw_col_\w+"
        "sw_raw_\w+"
        "sw_src_\w+"
        "sw_dst_\w+"
        "swap_\w+:"
        "skip_sw_\w+:"
    )
    "B7.4_GrowArchetypeCopy" = @(
        "grow_col_\w+"
        "grow_raw_\w+"
        "grow_src_\w+"
        "grow_dst_\w+"
        "skip_col_\w+:"
    )
}

function Get-B7LegacyPatterns {
    return $LegacyPatterns
}

# If executed directly as test script:
if ($MyInvocation.InvocationName -ne '.') {
    Write-Host "=== B7 Progressive Negative Invariant Status Map ===" -ForegroundColor Cyan

    $targetDir = $IrDir
    if ($CheckFresh) {
        $targetDir = Join-Path ([System.IO.Path]::GetTempPath()) "ecslang_fresh_b7_ir"
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        $cli = "src/ECSLang.CLI/bin/Release/net9.0/ECSLang.CLI.exe"
        $tempSrc = Join-Path $targetDir "08_ecs_basics.ecs"
        Copy-Item -Path "examples/08_ecs_basics.ecs" -Destination $tempSrc -Force
        & $cli build $tempSrc --emit-ir --emit-obj | Out-Null
    }

    $files = Get-ChildItem -Path $targetDir -Filter "*.ll"
    if ($files.Count -eq 0) {
        throw "No .ll files found in $targetDir"
    }

    $allResults = @{}
    foreach ($group in $LegacyPatterns.Keys) {
        $patterns = $LegacyPatterns[$group]
        $matchCount = 0
        foreach ($file in $files) {
            $content = Get-Content $file.FullName -Raw
            foreach ($pat in $patterns) {
                $matches = [regex]::Matches($content, $pat)
                $matchCount += $matches.Count
            }
        }
        $status = if ($matchCount -eq 0) { "ELIMINATED (0 instances)" } else { "PRESENT ($matchCount instances)" }
        $color = if ($matchCount -eq 0) { "Green" } else { "Yellow" }
        Write-Host ("  [{0,-26}] : {1}" -f $group, $status) -ForegroundColor $color
        $allResults[$group] = $matchCount
    }

    Write-Host "===================================================" -ForegroundColor Cyan
}
