@echo off
REM ============================================================================
REM  Runs Test.Automated (including storage integration) against an ephemeral
REM  S3-compatible endpoint (MinIO) started in Docker, then tears it down.
REM
REM  To test against a different endpoint (real AWS S3, Less3, Ceph, or an
REM  already-running MinIO), skip this script and pass the endpoint directly:
REM
REM    dotnet run --project test\Test.Automated -- ^
REM      --endpoint http://127.0.0.1:8000 --access-key KEY --secret-key SECRET ^
REM      --bucket my-bucket --provider s3compatible --path-style true --ssl false
REM ============================================================================
setlocal
set NETWORK=s3drive-itest-net
set CONTAINER=s3drive-itest-minio
set BUCKET=s3drive-test

pushd "%~dp0.."

echo Starting ephemeral MinIO...
docker network create %NETWORK% >nul 2>&1
docker rm -f %CONTAINER% >nul 2>&1
docker run -d --name %CONTAINER% --network %NETWORK% -p 9000:9000 -e MINIO_ROOT_USER=minioadmin -e MINIO_ROOT_PASSWORD=minioadmin minio/minio server /data >nul

echo Waiting for MinIO to become healthy...
for /l %%i in (1,1,40) do (
  curl -sf http://127.0.0.1:9000/minio/health/live >nul 2>&1 && goto ready
  timeout /t 1 >nul
)
:ready

echo Creating bucket %BUCKET%...
docker run --rm --network %NETWORK% -e MC_HOST_local=http://minioadmin:minioadmin@%CONTAINER%:9000 minio/mc mb --ignore-existing local/%BUCKET% >nul

echo Running tests...
dotnet run --project "test\Test.Automated\Test.Automated.csproj" -c Debug -- --endpoint http://127.0.0.1:9000 --access-key minioadmin --secret-key minioadmin --bucket %BUCKET% --provider s3compatible --path-style true --ssl false
set EXITCODE=%ERRORLEVEL%

echo Tearing down...
docker rm -f %CONTAINER% >nul 2>&1
docker network rm %NETWORK% >nul 2>&1

popd
endlocal & exit /b %EXITCODE%
