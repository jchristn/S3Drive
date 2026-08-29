#!/usr/bin/env bash
#
# Runs the full Test.Automated suite (including the storage integration tests) against an
# ephemeral Less3 instance started in Docker.
#
# Less3 is an S3-compatible server. On first start with no config it auto-generates a
# container configuration and seeds a default tenant, credential (access key "default",
# secret key "default"), and bucket ("default") — so no provisioning is needed here.
#
# To test against a different endpoint instead (real AWS S3, MinIO, Ceph, or an
# already-running server), skip this script and pass the endpoint on the command line:
#
#   dotnet run --project test/Test.Automated -- \
#     --endpoint http://127.0.0.1:9000 --access-key KEY --secret-key SECRET \
#     --bucket my-bucket --provider s3compatible --path-style true --ssl false
#
set -euo pipefail

CONTAINER="s3drive-itest-less3"
IMAGE="jchristn77/less3:v4.0.0"
ENDPOINT="http://127.0.0.1:8000"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cleanup() {
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "Starting ephemeral Less3 ($IMAGE)..."
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$CONTAINER" -p 8000:8000 "$IMAGE" >/dev/null

echo "Waiting for Less3 health..."
for _ in $(seq 1 90); do
  if curl -sf "$ENDPOINT/healthz" >/dev/null 2>&1; then break; fi
  sleep 1
done

echo "Running tests..."
dotnet run --project "$ROOT/test/Test.Automated/Test.Automated.csproj" -c Debug -- \
  --endpoint "$ENDPOINT" --access-key default --secret-key default \
  --bucket default --provider s3compatible --path-style true --ssl false
