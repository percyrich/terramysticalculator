@echo off
rem Converts a Board Game Arena Terra Mystica game into a .tmcalc save (see BgaImport\README.md).
rem   run-bga-import.bat                          the sample game, BgaConvert\samples\EP1-333098289.json
rem   run-bga-import.bat log.json gamedatas.json  another game (or one file holding both: run-bga-import.bat game.json)
rem The save, a report and a trace go to BgaImport\out; the console output also goes to bga-import-results.txt.
cd /d "%~dp0"
if "%~1"=="" (
  set LOG=BgaConvert\samples\EP1-333098289.json
  set DATA=
) else (
  set LOG=%~1
  set DATA=%~2
)
echo Converting %LOG% ...
dotnet run --project BgaImport\BgaImport.csproj -- "%LOG%" "%DATA%" BgaImport\out > bga-import-results.txt 2>&1
echo Exit code: %ERRORLEVEL% (0 = whole game converted, 3 = stopped part-way: see the report) >> bga-import-results.txt
type bga-import-results.txt
pause
