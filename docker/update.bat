@echo off
REM ============================================================================
REM  update.bat - pull the pinned observability images and recreate the S3Drive
REM  stack (collector, Prometheus, Tempo, Loki, Grafana). Non-destructive: named
REM  volumes (metrics, traces, logs, Grafana state) are preserved.
REM ============================================================================
setlocal
cd /d "%~dp0"

docker compose -f compose.yaml pull || exit /b 1
docker compose -f compose.yaml down || exit /b 1
docker compose -f compose.yaml up -d || exit /b 1
docker ps -a
endlocal
