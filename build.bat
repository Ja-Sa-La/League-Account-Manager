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

echo Restoring solution...
dotnet restore "League_Account_Manager.sln"
if errorlevel 1 exit /b %errorlevel%

echo Building application in %CONFIGURATION% configuration...
dotnet build "League_Account_Manager\League_Account_Manager.csproj" --configuration "%CONFIGURATION%" --no-restore
exit /b %errorlevel%