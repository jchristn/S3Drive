# Changelog

All notable changes to S3Drive are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - Unreleased (Alpha)

Initial alpha. Configuration formats, behavior, and interfaces may change between `0.1.x`
releases.

### Added
- Implementation plan (`S3DRIVE_PLAN.md`) and repository housekeeping: README, LICENSE (MIT),
  `.gitignore`, `CLAUDE.md`, and a documented `s3drive.sample.json`.
- `S3Drive.Core` engine: configuration (`s3drive.json`) with atomic writes and environment
  overrides; `SyslogLogging` facade with crash reports; AES-256-GCM credential encryption at
  rest; Blobject-backed storage supporting AWS S3 and S3-compatible endpoints (custom endpoint
  URL, SSL toggle, path-style vs virtual-hosted); one-file-equals-one-object filesystem
  semantics over Dokan with local staging for reads and writes; coarse per-object locking via
  Padlock; an in-memory metadata cache; a mount manager (multiple simultaneous mounts, one
  bucket per drive); CIFS/SMB re-sharing via the Windows SMB cmdlets; and a file-based
  TUI-to-agent command and status channel.
- `S3Drive.Agent`: an always-on Avalonia system-tray agent (About, per-drive mount/unmount and
  share/unshare, Exit) that owns all mounts and shares and runs independently of the TUI.
- `S3Drive.Tui`: a TUIKit console for configuring connections, sending mount/unmount/share
  commands, and monitoring status and logs; it starts the agent if it is not already running.
- `go.bat` developer script: builds the solution and launches the TUI.
- `Test.Automated`: 73 tests covering key mapping, caching, configuration, cryptography,
  locking, sharing, the IPC channel, and the Dokan filesystem, plus storage integration tests
  that run against any S3 or S3-compatible endpoint (CLI arguments or `S3DRIVE_TEST_*`
  variables). `test/run-integration.{sh,bat}` exercises them against an ephemeral Less3
  container. A `--mount-test` mode performs a real Dokan mount and drives operating-system-level
  file operations against the mounted drive (requires Windows and the Dokany driver).
