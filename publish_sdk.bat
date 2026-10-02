@echo off
setlocal enabledelayedexpansion

echo ================================================================================
echo           ECSLang Official SDK Release Packager (Native AOT)
echo ================================================================================

set "ROOT_DIR=%~dp0"
set "DIST_DIR=%ROOT_DIR%dist\ecslang-sdk"
set "BIN_DIR=%DIST_DIR%\bin"
set "STD_DIR=%DIST_DIR%\std"
set "EXAMPLES_DIR=%DIST_DIR%\examples"

echo.
echo [1/5] Preparing clean distribution directory...
if exist "%DIST_DIR%" (
    rmdir /s /q "%DIST_DIR%"
)
mkdir "%BIN_DIR%"
mkdir "%STD_DIR%"
mkdir "%EXAMPLES_DIR%"

echo.
echo [2/5] Publishing ECSLang.CLI with Native AOT (Release win-x64)...
dotnet publish "%ROOT_DIR%src\ECSLang.CLI\ECSLang.CLI.csproj" -c Release -r win-x64 --self-contained /p:PublishAot=true -o "%BIN_DIR%"
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Native AOT compilation failed!
    exit /b %ERRORLEVEL%
)

:: Create convenience aliases (ecslang.exe and ecs.exe)
if exist "%BIN_DIR%\ECSLang.CLI.exe" (
    copy /y "%BIN_DIR%\ECSLang.CLI.exe" "%BIN_DIR%\ecslang.exe" >nul
    copy /y "%BIN_DIR%\ECSLang.CLI.exe" "%BIN_DIR%\ecs.exe" >nul
)

:: Clean debug symbols from release package
del /q "%BIN_DIR%\*.pdb" >nul 2>&1

echo.
echo [3/5] Packaging Standard Library modules (.ecs only)...
powershell -NoProfile -Command "Get-ChildItem -Path '%ROOT_DIR%std' -Recurse -Filter '*.ecs' | ForEach-Object { $rel = (Resolve-Path $_.FullName -Relative).Substring(5); $dest = Join-Path '%STD_DIR%' $rel; $destDir = Split-Path $dest; if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }; Copy-Item $_.FullName -Destination $dest -Force; Write-Host ('  + std/' + $rel) }"

echo.
echo [4/5] Packaging Golden Examples and Benchmarks...
copy /y "%ROOT_DIR%examples\io_benchmark_precise.ecs" "%EXAMPLES_DIR%\" >nul
copy /y "%ROOT_DIR%examples\olap_bigdata_analyzer.ecs" "%EXAMPLES_DIR%\" >nul
copy /y "%ROOT_DIR%examples\01_hello_world.ecs" "%EXAMPLES_DIR%\" >nul
copy /y "%ROOT_DIR%examples\14_pure_dod_network.ecs" "%EXAMPLES_DIR%\" >nul
copy /y "%ROOT_DIR%examples\18_arcade_void_defender.ecs" "%EXAMPLES_DIR%\" >nul
if exist "%ROOT_DIR%README.md" (
    copy /y "%ROOT_DIR%README.md" "%DIST_DIR%\" >nul
)

echo.
echo [5/5] Verifying Native AOT SDK Executable...
"%BIN_DIR%\ecslang.exe" --help
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Verification of ecslang.exe failed!
    exit /b %ERRORLEVEL%
)

echo.
echo ================================================================================
echo [SUCCESS] ECSLang SDK v0.1.0 alpha packaged successfully!
echo Distribution folder: %DIST_DIR%
echo ================================================================================
