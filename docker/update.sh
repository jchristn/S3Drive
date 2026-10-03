#!/usr/bin/env bash
# ============================================================================
#  update.sh - pull the pinned observability images and recreate the S3Drive
#  stack (collector, Prometheus, Tempo, Loki, Grafana). Non-destructive: named
#  volumes (metrics, traces, logs, Grafana state) are preserved.
# ============================================================================
set -euo pipefail
cd "$(dirname "$0")"

docker compose -f compose.yaml pull
docker compose -f compose.yaml down
docker compose -f compose.yaml up -d
docker ps -a
