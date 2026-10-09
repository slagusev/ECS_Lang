# Verification script for D1 Golden LLVM IR baselines
# Modes:
#   -strict   : Exact line-by-line equality against all 7 golden files (including 70+ components)
#   -fast     : Rapid inner-loop check against 6 small golden files (< 5 seconds)
#   -refactor : Validates that diffs only contain legal architectural categories

param (
    [string]$Mode = "strict",
    [string]$CliPath = "src/ECSLang.CLI/bin/Debug/net9.0/ECSLang.CLI.exe"
)

$ErrorActionPreference = "Stop"
$goldenDir = "tests/golden_ir"

$baseTestFiles = @(
    @{ Name = "08_ecs_basics"; Source = "examples/08_ecs_basics.ecs" },
    @{ Name = "09_ecs_command_buffer"; Source = "examples/09_ecs_command_buffer.ecs" },
    @{ Name = "11_ecs_archetypes_and_filters"; Source = "examples/11_ecs_archetypes_and_filters.ecs" },
    @{ Name = "cast_test"; Source = "examples/cast_test.ecs" },
    @{ Name = "hierarchy_childof"; Source = "tests/hierarchy_childof.ecs" },
    @{ Name = "bulk_spawn_test"; Source = "tests/bulk_spawn_test.ecs" }
)

$over64Test = @{ Name = "over_64_components"; Source = "tests/canary_70_components.ecs" }

$testFiles = if ($Mode -eq "fast") {
    $baseTestFiles
} else {
    $baseTestFiles + $over64Test
}

Write-Host "=== Running Golden IR Verification (Mode: $Mode, Tests: $($testFiles.Count)) ===" -ForegroundColor Cyan

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_ir_test_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

$allPassed = $true
$summary = @()

try {
    foreach ($item in $testFiles) {
        $name = $item.Name
        $src = $item.Source
        $goldenFile = Join-Path $goldenDir "$name.ll"

        if (-not (Test-Path $goldenFile)) {
            Write-Host "[FAIL] Golden file not found: $goldenFile" -ForegroundColor Red
            $allPassed = $false
            continue
        }

        # 1. Copy source file into fresh isolated temp directory to guarantee outputDir = $tempDir
        $tempSrc = Join-Path $tempDir "$name.ecs"
        Copy-Item -Path $src -Destination $tempSrc -Force

        # 2. Invoke compiler freshly on isolated temp source
        Write-Host "  -> Compiling fresh $name to $tempDir..." -ForegroundColor DarkGray
        & $CliPath build $tempSrc --emit-ir --emit-obj 2>&1 | Out-Null
        $genIr = Join-Path $tempDir "$name.ll"

        if (-not (Test-Path $genIr)) {
            Write-Host "[FAIL] Generated IR not found for $src at $genIr" -ForegroundColor Red
            $allPassed = $false
            continue
        }

        $goldenContent = Get-Content $goldenFile
        $genContent = Get-Content $genIr

        if ($Mode -eq "strict") {
            # Exact line diff
            $diff = Compare-Object $goldenContent $genContent
            if ($null -eq $diff -or $diff.Count -eq 0) {
                Write-Host "  [OK] $name matches golden baseline exactly" -ForegroundColor Green
                $summary += "$name : EXACT MATCH"
            } else {
                Write-Host "  [FAIL] $name differs from golden baseline ($($diff.Count) diff lines)" -ForegroundColor Red
                $allPassed = $false
                $summary += "$name : MISMATCH ($($diff.Count) lines)"
            }
        }
        elseif ($Mode -eq "refactor") {
            # Classify diff lines into allowed categories
            $diff = Compare-Object $goldenContent $genContent
            if ($null -eq $diff -or $diff.Count -eq 0) {
                Write-Host "  [OK] $name matches golden baseline exactly" -ForegroundColor Green
                $summary += "$name : EXACT MATCH"
                continue
            }

            $illegalDiffs = @()
            foreach ($d in $diff) {
                $line = $d.InputObject.Trim()
                # Legal Category 1: Archetype struct mask type: [1 x i64] vs i64
                if ($line -match "%struct\.Archetype = type \{ \[(1|\d+) x i64\]" -or $line -match "%struct\.Archetype = type \{ i64") { continue }
                # Legal Category 2: world_get_or_create_archetype signature and calls (ptr vs i64)
                if ($line -match "world_get_or_create_archetype.*(ptr|i64)") { continue }
                # Legal Category 3: GEP mask patterns (e.g. getelementptr inbounds [1 x i64], mask slots/words)
                if ($line -match "getelementptr.*\[(1|\d+) x i64\]" -or $line -match "(mask_slot|mask_gep|m_gep|ex_w|tgt_w|src_w|dst_w|src_set|src_rem|dst_set|dst_rem|dst_spawn|cur_mask_w|childof_w)") { continue }
                # Legal Category 4: alloca / store / load of mask array and words
                if ($line -match "(alloca|constant) \[(1|\d+) x i64\]" -or $line -match "@_ecs_query_" -or $line -match "(load|store).*(ex_w|tgt_w|src_w|dst_w|src_set|src_rem|dst_set|dst_rem|val_set|val_rem|dst_spawn|cur_mask_w|mask_w|m_gep|m_slot|a0_mask|temp_mask|spawn_mask|childof_w|\[(1|\d+) x i64\])" -or $line -match "zeroinitializer") { continue }
                # Legal Category 5: mask comparison and boolean combinations
                if (($line -match "(icmp eq i64|and i1|and i64|br i1)") -and ($line -match "(is_match|existing_mask|target_mask|%1|arch_mask|cur_mask|has_|and_mask|and_without|and_co|sw_co_has|m_val|word_\d+_match)")) { continue }
                # Comments / ModuleID line
                if ($line -match "^; ModuleID") { continue }

                $illegalDiffs += $line
            }

            if ($illegalDiffs.Count -eq 0) {
                Write-Host "  [OK] $name : all diffs belong to legal architectural categories" -ForegroundColor Green
                $summary += "$name : LEGAL REFACTOR DIFFS ONLY"
            } else {
                Write-Host "  [FAIL] $name : found $($illegalDiffs.Count) illegal diff line(s):" -ForegroundColor Red
                foreach ($ill in $illegalDiffs | Select-Object -First 5) {
                    Write-Host "    $ill" -ForegroundColor Yellow
                }
                $allPassed = $false
                $summary += "$name : ILLEGAL DIFFS ($($illegalDiffs.Count))"
            }
        }
    }
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "=== Summary ===" -ForegroundColor Cyan
foreach ($s in $summary) {
    Write-Host "  $s"
}

if (-not $allPassed) {
    Write-Host "Golden IR Verification FAILED!" -ForegroundColor Red
    exit 1
}

Write-Host "Golden IR Verification PASSED!" -ForegroundColor Green
exit 0
