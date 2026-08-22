@echo off
setlocal

cd /d "%~dp0"

set "CONFIGURATION=%~1"
if not defined CONFIGURATION set "CONFIGURATION=Release"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Error: The .NET SDK was not found on PATH.
    exit /b 1
)

echo Restoring tests...
dotnet restore "League_Account_Manager.Tests\League_Account_Manager.Tests.csproj"
if errorlevel 1 exit /b %errorlevel%

echo Running tests in %CONFIGURATION% configuration...
dotnet test "League_Account_Manager.Tests\League_Account_Manager.Tests.csproj" --configuration "%CONFIGURATION%" --no-restore
exit /b %errorlevel%