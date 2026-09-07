@echo off
setlocal
REM Update S3Drive from source: pull the latest code, rebuild, and republish the
REM agent and the TUI side by side into dist\ (the tray's Open action needs both
REM executables in the same folder). Restarts the agent if it was running.

set "SCRIPT_DIR=%~dp0"
for %%i in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fi"
cd /d "%REPO_ROOT%"

REM Remember whether the agent was running so we can restart it afterwards.
set "AGENT_WAS_RUNNING=0"
tasklist /FI "IMAGENAME eq S3Drive.Agent.exe" | find /I "S3Drive.Agent.exe" >nul && set "AGENT_WAS_RUNNING=1"

REM Stop both S3Drive processes: a running agent keeps S3Drive.Core.dll open, and
REM publishing over it would silently ship stale binaries. Exiting the agent also
REM unmounts its drives and drops its network shares.
taskkill /IM S3Drive.Tui.exe /F >nul 2>&1
taskkill /IM S3Drive.Agent.exe /F >nul 2>&1

git pull --ff-only
if errorlevel 1 echo git pull failed; rebuilding the checkout as-is.

dotnet publish src\S3Drive.Agent -c Release -f net8.0 -o dist || exit /b 1
dotnet publish src\S3Drive.Tui   -c Release -f net8.0 -o dist || exit /b 1

echo Published S3Drive.Agent.exe and S3Drive.Tui.exe to "%REPO_ROOT%\dist".

if "%AGENT_WAS_RUNNING%"=="1" (
    start "" "%REPO_ROOT%\dist\S3Drive.Agent.exe"
    echo Restarted the agent.
)
endlocal
