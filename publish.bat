@echo off
setlocal

cd /d "%~dp0"

set "CONFIGURATION=%~1"
if not defined CONFIGURATION set "CONFIGURATION=Release"
set "OUTPUT=%~dp0artifacts\win-x64"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Error: The .NET SDK was not found on PATH.
    exit /b 1
)

echo Restoring win-x64 dependencies...
dotnet restore "League_Account_Manager\League_Account_Manager.csproj" --runtime win-x64
if errorlevel 1 exit /b %errorlevel%

echo Publishing single-file win-x64 application to "%OUTPUT%"...
dotnet publish "League_Account_Manager\League_Account_Manager.csproj" ^
    --configuration "%CONFIGURATION%" ^
    --runtime win-x64 ^
    --output "%OUTPUT%" ^
    --no-restore ^
    --self-contained false ^
    --property:PublishSingleFile=true ^
    --property:PublishReadyToRun=false
exit /b %errorlevel%