#!/usr/bin/env bash
# ============================================================================
#  go.sh - build S3Drive and launch the TUI.
#
#  1) Builds the solution.
#  2) Runs the TUI.
#  3) On startup the TUI checks whether the tray agent is running and starts
#     it if it is not.
#
#  The tray agent owns all mounts and network shares and keeps running
#  independently of the TUI. Closing the TUI does not unmount drives or stop
#  sharing; only choosing Exit from the tray does.
# ============================================================================
set -u

cd "$(dirname "$0")" || exit 1

echo "Building S3Drive..."
if ! dotnet build "S3Drive.sln" -c Debug -nologo; then
    echo
    echo "Build failed."
    exit 1
fi

echo
echo "Starting S3Drive TUI..."
dotnet run --project "src/S3Drive.Tui/S3Drive.Tui.csproj" -c Debug --no-build
exit $?
