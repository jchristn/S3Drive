#!/usr/bin/env bash
#
# Runs the full Test.Automated suite (including the storage integration tests) against an
# ephemeral S3-compatible endpoint started in Docker.
#
# By default it spins up MinIO (an S3-compatible endpoint, path-style, no TLS), creates a test
# bucket, runs the tests, and tears everything down.
#
# To test against a different endpoint instead (real AWS S3, Less3, Ceph, or an already-running
# MinIO), skip this script and pass the endpoint on the command line:
#
#   dotnet run --project test/Test.Automated -- \
#     --endpoint http://127.0.0.1:8000 --access-key KEY --secret-key SECRET \
#     --bucket my-bucket --provider s3compatible --path-style true --ssl false
#
set -euo pipefail

NETWORK="s3drive-itest-net"
CONTAINER="s3drive-itest-minio"
BUCKET="s3drive-test"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cleanup() {
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  docker network rm "$NETWORK" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "Starting ephemeral MinIO..."
docker network create "$NETWORK" >/dev/null 2>&1 || true
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$CONTAINER" --network "$NETWORK" -p 9000:9000 \
  -e MINIO_ROOT_USER=minioadmin -e MINIO_ROOT_PASSWORD=minioadmin \
  minio/minio server /data >/dev/null

echo "Waiting for MinIO to become healthy..."
for _ in $(seq 1 40); do
  if curl -sf http://127.0.0.1:9000/minio/health/live >/dev/null 2>&1; then break; fi
  sleep 1
done

echo "Creating bucket '$BUCKET'..."
docker run --rm --network "$NETWORK" \
  -e MC_HOST_local="http://minioadmin:minioadmin@$CONTAINER:9000" \
  minio/mc mb --ignore-existing "local/$BUCKET" >/dev/null

echo "Running tests..."
dotnet run --project "$ROOT/test/Test.Automated/Test.Automated.csproj" -c Debug -- \
  --endpoint http://127.0.0.1:9000 --access-key minioadmin --secret-key minioadmin \
  --bucket "$BUCKET" --provider s3compatible --path-style true --ssl false
