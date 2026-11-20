@echo off
REM ============================================================================
REM  build-installers.bat - build every S3Drive installer this Windows machine
REM  can produce, by running S3Drive.Publisher channel by channel (the same
REM  commands CI runs).
REM
REM  Usage:   build-installers.bat [version]
REM           version defaults to <Version> in Directory.Build.props.
REM  Needs:   the .NET 10 SDK (builds net8.0) and Inno Setup 6.3 or later:
REM             choco install innosetup --no-progress -y
REM  Output:  installers\<version>\
REM             s3drive-<version>-win-x64-setup.exe     s3drive-<version>-win-x64.zip
REM             s3drive-<version>-win-arm64-setup.exe   s3drive-<version>-win-arm64.zip
REM             SHA256SUMS
REM ============================================================================
setlocal
pushd "%~dp0"

set "PUBLISHER=src\S3Drive.Publisher\S3Drive.Publisher.csproj"
set "VERSIONARG="
if not "%~1"=="" set "VERSIONARG=--version %~1"

echo Building S3Drive.Publisher...
dotnet build "%PUBLISHER%" -c Release -nologo -v q
if errorlevel 1 goto :fail

for %%c in (publish zip inno checksums) do (
    echo.
    dotnet run --project "%PUBLISHER%" -c Release --no-build -- --channel %%c %VERSIONARG%
    if errorlevel 1 goto :fail
)

for /f "usebackq delims=" %%v in (`dotnet run --project "%PUBLISHER%" -c Release --no-build -- --print-version %VERSIONARG%`) do set "VERSION=%%v"
echo.
echo Installers are in "%CD%\installers\%VERSION%".
popd
endlocal
exit /b 0

:fail
echo.
echo Installer build failed.
popd
endlocal
exit /b 1
