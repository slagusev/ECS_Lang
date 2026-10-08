# Run all examples build and check
$files = Get-ChildItem -Path examples -Filter *.ecs | Sort-Object Name
$total = $files.Count
$passed = 0

Write-Host "=== Building and Verifying All $total Examples ===" -ForegroundColor Cyan

foreach ($f in $files) {
    Write-Host "  -> Compiling $($f.Name)..." -NoNewline
    $objPath = [System.IO.Path]::ChangeExtension($f.FullName, ".obj")
    & src/ECSLang.CLI/bin/Debug/net9.0/ECSLang.CLI.exe build $f.FullName --emit-obj 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host " [FAIL]" -ForegroundColor Red
        throw "Failed to build $($f.Name)"
    }
    if (Test-Path $objPath) {
        Remove-Item $objPath -Force
    }
    Write-Host " [OK]" -ForegroundColor Green
    $passed++
}

Write-Host "=== ALL $passed / $total EXAMPLES BUILT SUCCESSFULLY! ===" -ForegroundColor Green
