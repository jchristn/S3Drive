@echo off
REM ============================================================================
REM  Runs Test.Automated (including storage integration) against an ephemeral
REM  Less3 instance started in Docker, then tears it down.
REM
REM  Less3 auto-seeds a default tenant, credential (access key "default", secret
REM  key "default"), and bucket ("default") on first start, so no provisioning
REM  is needed here.
REM
REM  To test against a different endpoint (real AWS S3, MinIO, Ceph, or an
REM  already-running server), skip this script and pass the endpoint directly:
REM
REM    dotnet run --project test\Test.Automated -- ^
REM      --endpoint http://127.0.0.1:9000 --access-key KEY --secret-key SECRET ^
REM      --bucket my-bucket --provider s3compatible --path-style true --ssl false
REM ============================================================================
setlocal
set CONTAINER=s3drive-itest-less3
set IMAGE=jchristn77/less3:v4.0.0
set ENDPOINT=http://127.0.0.1:8000

pushd "%~dp0.."

echo Starting ephemeral Less3 (%IMAGE%)...
docker rm -f %CONTAINER% >nul 2>&1
docker run -d --name %CONTAINER% -p 8000:8000 %IMAGE% >nul

echo Waiting for Less3 health...
for /l %%i in (1,1,90) do (
  curl -sf %ENDPOINT%/healthz >nul 2>&1 && goto ready
  timeout /t 1 >nul
)
:ready

echo Running tests...
dotnet run --project "test\Test.Automated\Test.Automated.csproj" -c Debug -- --endpoint %ENDPOINT% --access-key default --secret-key default --bucket default --provider s3compatible --path-style true --ssl false
set EXITCODE=%ERRORLEVEL%

echo Tearing down...
docker rm -f %CONTAINER% >nul 2>&1

popd
endlocal & exit /b %EXITCODE%
