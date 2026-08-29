@echo off
REM ============================================================================
REM  go.bat - build S3Drive and launch the TUI.
REM
REM  1) Builds the solution.
REM  2) Runs the TUI.
REM  3) On startup the TUI checks whether the tray agent is running and starts
REM     it if it is not.
REM
REM  The tray agent owns all mounts and network shares and keeps running
REM  independently of the TUI. Closing the TUI does not unmount drives or stop
REM  sharing; only choosing Exit from the tray does.
REM ============================================================================
setlocal
pushd "%~dp0"

echo Building S3Drive...
dotnet build "S3Drive.sln" -c Debug -nologo
if errorlevel 1 (
    echo.
    echo Build failed.
    popd
    endlocal
    exit /b 1
)

echo.
echo Starting S3Drive TUI...
dotnet run --project "src\S3Drive.Tui\S3Drive.Tui.csproj" -c Debug --no-build
set "EXITCODE=%ERRORLEVEL%"

popd
endlocal & exit /b %EXITCODE%
