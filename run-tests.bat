@echo off
rem Runs every Core test, then checks that the Godot project still compiles against Core.
rem The outcome goes to test-results.txt (next to this file), so it can be read back.
cd /d "%~dp0"
echo Running tests...
dotnet test Core.Tests\Core.Tests.csproj --nologo > test-results.txt 2>&1
echo Tests exit code: %ERRORLEVEL% >> test-results.txt
echo Building the Godot project...
dotnet build tm-calc\TMCalc.csproj --nologo -v q >> test-results.txt 2>&1
echo UI build exit code: %ERRORLEVEL% >> test-results.txt
type test-results.txt
pause
