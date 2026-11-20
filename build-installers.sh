#!/usr/bin/env bash
# =============================================================================
#  build-installers.sh - build the S3Drive installer formats this macOS or
#  Linux machine can produce, by running S3Drive.Publisher channel by channel
#  (the same commands CI runs).
#
#  S3Drive is Windows-only, and the Inno Setup .exe installer can only be built
#  on Windows (use build-installers.bat or the release workflow). Because .NET
#  cross-compiles, this script still builds and stages the Windows binaries and
#  the portable .zip archives, which catches most packaging problems quickly.
#
#  Usage:   ./build-installers.sh [version]
#           version defaults to <Version> in Directory.Build.props.
#  Needs:   the .NET 10 SDK (builds net8.0).
#  Output:  installers/<version>/s3drive-<version>-win-{x64,arm64}.zip, SHA256SUMS
# =============================================================================
set -euo pipefail
cd "$(dirname "$0")"

publisher="src/S3Drive.Publisher/S3Drive.Publisher.csproj"
version_args=()
if [ $# -gt 0 ] && [ -n "$1" ]; then
  version_args=(--version "$1")
fi

run_publisher() {
  dotnet run --project "$publisher" -c Release --no-build -- "$@" ${version_args[@]+"${version_args[@]}"}
}

echo "Building S3Drive.Publisher..."
dotnet build "$publisher" -c Release -nologo -v q

for channel in publish zip checksums; do
  echo
  run_publisher --channel "$channel"
done

version="$(run_publisher --print-version)"
echo
echo "Built into $(pwd)/installers/$version."
echo "The setup .exe (channel 'inno') needs Windows: run build-installers.bat there, or the release workflow."
