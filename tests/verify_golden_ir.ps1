# Verification script for D1 Golden LLVM IR baselines
# Modes:
#   -strict   : Exact line-by-line equality against all 7 golden files (including 70+ components)
#   -fast     : Rapid inner-loop check against 6 small golden files (< 5 seconds)
#   -refactor : Validates that diffs only contain legal architectural categories

param (
    [string]$Mode = "strict",
    [string]$CliPath = "src/ECSLang.CLI/bin/Release/net9.0/ECSLang.CLI.exe",
    [switch]$UpdateBaselines
)

$ErrorActionPreference = "Stop"

# 0. Freshness guard: fail hard if compiler binary is older than any source file
$cliDll = [System.IO.Path]::ChangeExtension($CliPath, ".dll")
if (-not (Test-Path $cliDll)) {
    throw "STALE COMPILER — binary '$cliDll' not found. Run 'dotnet build -c Release' first."
}
$binMtime = (Get-Item $cliDll).LastWriteTime
$newestSrc = Get-ChildItem -Path "src" -Recurse -Include *.cs |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($newestSrc -and $newestSrc.LastWriteTime -gt $binMtime) {
    throw "STALE COMPILER DETECTED! Source '$($newestSrc.FullName)' ($($newestSrc.LastWriteTime)) is newer than binary '$cliDll' ($binMtime). Run 'dotnet build -c Release' before verification."
}

# Dot-source B7 legacy patterns for safe deletion classification
. "$PSScriptRoot/verify_b7_negative_invariants.ps1"
$legacyPatternsDict = Get-B7LegacyPatterns
$legacyPatternsList = @()
foreach ($val in $legacyPatternsDict.Values) {
    $legacyPatternsList += $val
}

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

        if ($UpdateBaselines) {
            Copy-Item -Path $genIr -Destination $goldenFile -Force
            Write-Host "  [UPDATED] $name golden baseline updated" -ForegroundColor Cyan
            $summary += "$name : BASELINE UPDATED"
            continue
        }

        if ($Mode -eq "strict" -or $Mode -eq "fast") {
            # Fast exact match via SHA256 hash comparison
            $goldenHash = (Get-FileHash -Path $goldenFile -Algorithm SHA256).Hash
            $genHash = (Get-FileHash -Path $genIr -Algorithm SHA256).Hash

            if ($goldenHash -eq $genHash) {
                Write-Host "  [OK] $name matches golden baseline exactly" -ForegroundColor Green
                $summary += "$name : EXACT MATCH"
            } else {
                # Mismatch detected: perform line diff for detailed diagnostics
                $goldenContent = Get-Content $goldenFile
                $genContent = Get-Content $genIr
                $diff = Compare-Object $goldenContent $genContent -CaseSensitive
                Write-Host "  [FAIL] $name differs from golden baseline ($($diff.Count) diff lines, Hash Mismatch)" -ForegroundColor Red
                $allPassed = $false
                $summary += "$name : MISMATCH ($($diff.Count) lines)"
            }
        }
        elseif ($Mode -eq "refactor") {
            # Fast path: exact match check via hash first
            $goldenHash = (Get-FileHash -Path $goldenFile -Algorithm SHA256).Hash
            $genHash = (Get-FileHash -Path $genIr -Algorithm SHA256).Hash

            if ($goldenHash -eq $genHash) {
                Write-Host "  [OK] $name matches golden baseline exactly" -ForegroundColor Green
                $summary += "$name : EXACT MATCH"
                continue
            }

            # Hashes differ: classify diff lines into allowed categories
            # Use git diff --no-index for fast C-level line diff (prevents OOM / 3-minute hangs on 540K-line canary)
            $diffLines = git diff --no-index --unified=0 $goldenFile $genIr 2>$null
            $illegalDiffs = @()
            foreach ($raw in $diffLines) {
                if ($raw.StartsWith("@@") -or $raw.StartsWith("---") -or $raw.StartsWith("+++") -or $raw.StartsWith("diff ") -or $raw.StartsWith("index ") -or $raw.StartsWith("\ No newline")) { continue }
                $side = if ($raw.StartsWith("-")) { "<=" } elseif ($raw.StartsWith("+")) { "=>" } else { continue }
                $line = $raw.Substring(1).Trim()
                if ([string]::IsNullOrWhiteSpace($line)) { continue }

                # Comments / ModuleID line
                if ($line -match "^; ModuleID") { continue }

                # 1. Deletions from Golden Baseline (SideIndicator == '<=')
                if ($side -eq "<=") {
                    # Must match one of the canonical legacy patterns from verify_b7_negative_invariants.ps1
                    $matchedLegacy = $false
                    foreach ($pat in $legacyPatternsList) {
                        if ($line -match $pat) { $matchedLegacy = $true; break }
                    }
                    if ($matchedLegacy) { continue }

                    # Control flow and SSA lines tied to deleted unrolled migration blocks in world_set_*
                    if ($line -match "(is_has_|br i1 %is_has_|br label %skip_|call ptr @memcpy\(ptr %dst_elem_|do_swap_remove:\s+;\s+preds|after_swap_remove:\s+;\s+preds)") { continue }
                    
                    # Shifted swap-remove memcpy in old baseline
                    if ($line -match "call ptr @memcpy\(ptr %sw_dst_.*%sw_src_") { continue }
                }

                # 2. Additions in Fresh IR (SideIndicator == '=>')
                if ($side -eq "=>") {
                    # Legal Category 7: Component descriptor table and struct (Step B7.1)
                    if ($line -match "%struct\.ComponentDesc = type \{ i64, i64, i32, i32 \}" -or $line -match "@_ecs_component_descriptors = internal constant \[\d+ x %struct\.ComponentDesc\]") { continue }

                    # Legal Category 8: Generic component migration core, cttz intrinsic, Function Attrs (Step B7.2)
                    if ($line -match "(world_migrate_entity|llvm\.cttz\.i64|Function Attrs: nocallback|attributes #\d+ = \{ nocallback|m_tables|m_old|m_new|m_common|m_word|m_has_bits|desc_slot|desc_elem|size_slot|comp_size|old_col|new_col|cur_mask|ctz|tz32|comp_id|src_off|dst_off|src_elem|dst_elem|more_bits|next_mask|mask_sub1|m_w\d+_bit_loop)" -or
                        ($line -match "call.*@memcpy\(ptr %dst_elem, ptr %src_elem") -or
                        ($line -match "^define void @world_migrate_entity") -or
                        ($line -match "^declare i64 @llvm\.cttz\.i64") -or
                        ($line -match "call void @world_migrate_entity") -or
                        ($line -eq "entry:") -or ($line -eq "ret void") -or ($line -eq "}")) { continue }

                    # Shifted labels and SSA lines in world_set_* resulting from migration loop removal
                    if ($line -match "do_swap_remove:\s+;\s+preds = %after_grow_new_arch" -or
                        $line -match "after_swap_remove:\s+;\s+preds = %skip_sw_.*%after_grow_new_arch" -or
                        $line -match "call ptr @memcpy\(ptr %sw_dst_.*%sw_src_") { continue }
                }

                # General categories (both directions)
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
                # Legal Category 6: Profiler multi-word HUD bitmask formatting (Step A7.6)
                if ($line -match "(arch_fmt|str_buf|sprintf\(ptr %str_buf|p_a_mask_w|DrawRectangle\(i32 10, i32 10, i32 (500|620|700|780)|DrawRectangleLines\(i32 10, i32 10, i32 (500|620|700|780))") { continue }

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
