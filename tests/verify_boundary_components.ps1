# ========================================================================
# Level 2 Invariant Test: Word Boundary Components (63, 64, 65, 127, 128, 129)
# Verifies:
# - Correct multi-word mask layout for WORDS = 1, 2, 3
# - Spawn, migration across word boundaries (set & remove)
# - Query filtering with 'query' and 'without'
# - Despawn swap-copy across word boundaries
# - Exact runtime value assertions (fails if any invariant broken)
# ========================================================================

param (
    [string]$CliPath = "src/ECSLang.CLI/bin/Debug/net9.0/ECSLang.CLI.exe"
)

$ErrorActionPreference = "Stop"
$boundaries = @(63, 64, 65, 127, 128, 129)
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ecslang_boundary_" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

Write-Host "=== Running Boundary Components Tests (63, 64, 65, 127, 128, 129) ===" -ForegroundColor Cyan

try {
    foreach ($count in $boundaries) {
        $userCompCount = $count - 1
        $lastComp = "C$userCompCount"
        $words = [Math]::Max(1, [int][Math]::Floor(($count + 63) / 64))

        Write-Host "-> Testing boundary N = $count (WORDS = $words, highest component: $lastComp)..." -ForegroundColor Yellow

        $srcFile = Join-Path $tempDir "boundary_$count.ecs"
        $exeFile = Join-Path $tempDir "boundary_$count.exe"
        $sb = [System.Text.StringBuilder]::new()

        # Declare components C1 .. C{userCompCount}
        for ($i = 1; $i -le $userCompCount; $i++) {
            [void]$sb.AppendLine("component C$i { v: i32 }")
        }

        # Resource for stats & validation
        [void]$sb.AppendLine(@"
resource BoundaryStats {
    matches: i32,
    err: i32,
}

system ValidateSystem {
    query(e: Entity, mut c1: C1, mut cLast: $lastComp, mut res: BoundaryStats, without: C2) {
        if (c1.v == 111 && cLast.v == 999) || (c1.v == 222 && cLast.v == 888) {
            res.matches += 1;
        } else {
            res.err = 1;
        }
        cLast.v += 1;
    }
}

pipeline BoundaryPipeline {
    stage Test {
        ValidateSystem;
    }
}

fn main(): i32 {
    let mut world = ecs::create_world();
    world.set_BoundaryStats(0, 0);

    // 1. Spawn entity and set C1 (word 0)
    let e1 = world.spawn();
    world.set_C1(e1, 111);
    if !world.has_C1(e1) { return 101; }
    if world.has_$lastComp(e1) { return 102; }

    // 2. Migrate across word boundary by adding highest component
    world.set_$lastComp(e1, 999);
    if !world.has_C1(e1) || !world.has_$lastComp(e1) { return 103; }

    // 3. Bulk spawn with C1 and highest component
    let e2 = world.spawn_with(C1(222), ${lastComp}(888));
    if !world.has_C1(e2) || !world.has_$lastComp(e2) { return 104; }

    // 4. Excluded entity with C2
    let e_ex = world.spawn_with(C1(333), ${lastComp}(777), C2(1));

    // 5. Run query system
    BoundaryPipeline(world);

    let stats = world.get_BoundaryStats();
    if stats.matches != 2 {
        return 105;
    }
    if stats.err != 0 {
        return 106;
    }

    // 6. Migrate remove C1 from e1 (leaving only highest component)
    world.remove_C1(e1);
    if world.has_C1(e1) { return 107; }
    if !world.has_$lastComp(e1) { return 108; }

    // 7. Despawn e2 (test swap-copy on multi-word columns)
    world.despawn(e2);
    if world.has_C1(e2) || world.has_$lastComp(e2) { return 109; }

    return 0;
}
"@)

        [System.IO.File]::WriteAllText($srcFile, $sb.ToString())

        # Compile with explicit --no-wait and -O0
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $buildProc = Start-Process -FilePath $CliPath -ArgumentList "build `"$srcFile`" -O0 --no-wait -o `"$exeFile`"" -NoNewWindow -Wait -PassThru -RedirectStandardOutput (Join-Path $tempDir "build_out_$count.txt") -RedirectStandardError (Join-Path $tempDir "build_err_$count.txt")
        if ($buildProc.ExitCode -ne 0) {
            $bOut = Get-Content (Join-Path $tempDir "build_out_$count.txt") -Raw -ErrorAction SilentlyContinue
            $bErr = Get-Content (Join-Path $tempDir "build_err_$count.txt") -Raw -ErrorAction SilentlyContinue
            throw "FAIL: Build boundary N = $count failed! Output:`n$bOut`n$bErr"
        }

        # Run compiled binary directly
        $runProc = Start-Process -FilePath $exeFile -NoNewWindow -Wait -PassThru -RedirectStandardOutput (Join-Path $tempDir "run_out_$count.txt") -RedirectStandardError (Join-Path $tempDir "run_err_$count.txt")
        $sw.Stop()

        if ($runProc.ExitCode -ne 0) {
            $rOut = Get-Content (Join-Path $tempDir "run_out_$count.txt") -Raw -ErrorAction SilentlyContinue
            $rErr = Get-Content (Join-Path $tempDir "run_err_$count.txt") -Raw -ErrorAction SilentlyContinue
            throw "FAIL: Boundary test N = $count failed with ExitCode $($runProc.ExitCode)! Output:`n$rOut`n$rErr"
        }

        Write-Host "   [OK] Boundary N = $count (WORDS = $words) PASSED in $($sw.ElapsedMilliseconds) ms." -ForegroundColor Green
    }

    Write-Host "`nLEVEL 2 PROOF PASSED: All 6 boundary tests (63, 64, 65, 127, 128, 129) PASSED with exact value validation." -ForegroundColor Green
}
finally {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}
