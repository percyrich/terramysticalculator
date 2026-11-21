@echo off
rem Converts every .json file in a folder (see BgaImport\README.md).
rem   run-bga-import-all.bat BgaConvert\samples
rem Saves, reports and traces go to BgaImport\out, with summary.txt (one line a game); the console output also goes to bga-import-results.txt.
cd /d "%~dp0"
if "%~1"=="" (
  echo Usage: run-bga-import-all.bat folder
  pause
  exit /b 1
)
rem A trailing backslash would escape the closing quote ("folder\" ...), so it is dropped.
set "DIR=%~1"
if "%DIR:~-1%"=="\" set "DIR=%DIR:~0,-1%"
echo Converting every game in %DIR% ...
dotnet run --project BgaImport\BgaImport.csproj -- "%DIR%" "" BgaImport\out > bga-import-results.txt 2>&1
echo Exit code: %ERRORLEVEL% (0 = every game converted, 3 = some stopped part-way: see BgaImport\out\summary.txt) >> bga-import-results.txt
type bga-import-results.txt
pause
