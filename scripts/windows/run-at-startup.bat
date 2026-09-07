@echo off
setlocal
REM Register the S3Drive agent (the system-tray process that owns all mounts and
REM network shares) to start at login for the current user, via the HKCU Run
REM registry key. No administrator rights needed. Expects the published layout:
REM the agent and the TUI side by side in dist\ at the repository root (run
REM update.bat first).

set "SCRIPT_DIR=%~dp0"
for %%i in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fi"
set "AGENT_EXE=%REPO_ROOT%\dist\S3Drive.Agent.exe"

if not exist "%AGENT_EXE%" (
    echo S3Drive.Agent.exe not found at "%AGENT_EXE%".
    echo Run scripts\windows\update.bat first to build and publish it.
    exit /b 1
)

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v S3DriveAgent /t REG_SZ /d "\"%AGENT_EXE%\"" /f >nul
if errorlevel 1 (
    echo Failed to write the Run registry key.
    exit /b 1
)
echo Registered "%AGENT_EXE%" to run at login.

tasklist /FI "IMAGENAME eq S3Drive.Agent.exe" | find /I "S3Drive.Agent.exe" >nul
if errorlevel 1 (
    start "" "%AGENT_EXE%"
    echo Started the agent now; look for the S3Drive icon in the system tray.
) else (
    echo The agent is already running.
)
endlocal
