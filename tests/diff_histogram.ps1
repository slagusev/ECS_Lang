# Calculate diff histogram for Step 1 across all 6 golden files
$diff = git diff HEAD~1 HEAD -- tests/golden_ir/
$currentFile = ""
$stats = [ordered]@{}

foreach ($line in ($diff -split "`r?`n")) {
    if ($line -match '^\+\+\+ b/tests/golden_ir/(.*)') {
        $currentFile = $matches[1]
        $stats[$currentFile] = [ordered]@{
            MASK_TYPE = 0
            GET_OR_CREATE_SIG = 0
            GEP_MASK_PATTERN = 0
            ALLOCA_MASK_PATTERN = 0
            OTHER = 0
            TotalLines = 0
        }
    }
    elseif ($currentFile -ne "" -and $line -match '^[+-][^+-]') {
        $trimmed = $line.Substring(1).Trim()
        $stats[$currentFile].TotalLines++
        if ($trimmed -match '%struct\.Archetype = type') {
            $stats[$currentFile].MASK_TYPE++
        }
        elseif ($trimmed -match 'world_get_or_create_archetype') {
            $stats[$currentFile].GET_OR_CREATE_SIG++
        }
        elseif ($trimmed -match 'getelementptr.*\[(1|\d+) x i64\]' -or $trimmed -match '(mask_slot|mask_gep|m_gep|ex_w|tgt_w|src_w|dst_w|src_set|src_rem|dst_set|dst_rem|dst_spawn|cur_mask_w)') {
            $stats[$currentFile].GEP_MASK_PATTERN++
        }
        elseif ($trimmed -match 'alloca.*\[(1|\d+) x i64\]' -or $trimmed -match '(load|store).*(ex_w|tgt_w|src_w|dst_w|src_set|src_rem|dst_set|dst_rem|val_set|val_rem|dst_spawn|cur_mask_w|m_gep|a0_mask|temp_mask|spawn_mask|\[(1|\d+) x i64\])' -or $trimmed -match 'zeroinitializer' -or $trimmed -match '(icmp eq i64|and i1).*(is_match|existing_mask|target_mask|%1)') {
            $stats[$currentFile].ALLOCA_MASK_PATTERN++
        }
        else {
            $stats[$currentFile].OTHER++
        }
    }
}

Write-Host "=== Step 1 Golden IR Diff Histogram ===" -ForegroundColor Cyan
foreach ($f in $stats.Keys) {
    Write-Host "File: $f (Total diff lines: $($stats[$f].TotalLines))" -ForegroundColor Yellow
    Write-Host "  - MASK_TYPE           : $($stats[$f].MASK_TYPE)"
    Write-Host "  - GET_OR_CREATE_SIG   : $($stats[$f].GET_OR_CREATE_SIG)"
    Write-Host "  - GEP_MASK_PATTERN    : $($stats[$f].GEP_MASK_PATTERN)"
    Write-Host "  - ALLOCA_MASK_PATTERN : $($stats[$f].ALLOCA_MASK_PATTERN)"
    Write-Host "  - OTHER (uncat)       : $($stats[$f].OTHER)"
}
