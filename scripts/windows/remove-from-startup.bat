@echo off
setlocal
REM Deregister the S3Drive agent from starting at login (deletes the S3DriveAgent
REM value from the current user's Run registry key). A currently running agent is
REM left alone, and it keeps its drives mounted and shares up; choose Exit from
REM the tray menu if you want it gone now.

reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v S3DriveAgent >nul 2>&1
if errorlevel 1 (
    echo The agent is not registered to run at login; nothing to do.
    exit /b 0
)

reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v S3DriveAgent /f >nul
if errorlevel 1 (
    echo Failed to delete the Run registry value.
    exit /b 1
)
echo Removed the agent from startup. A running agent keeps running (drives stay
echo mounted, shares stay up) until you exit it from the tray menu.
endlocal
