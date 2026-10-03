# Changelog

All notable changes to S3Drive are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.1] - 2026-10-03 (Alpha)

### Changed
- Dependency updates: AWSSDK.S3 4.0.102.1 -> 4.0.104.1, Blobject.AmazonS3 5.0.21 -> 6.1.0,
  Padlock 1.0.4 -> 1.2.0, SyslogLogging 2.2.2 -> 2.3.1, Avalonia / Avalonia.Desktop /
  Avalonia.Themes.Fluent 11.3.20 -> 12.1.3, TUIKit 1.1.1 -> 1.2.0.
- Test dependency updates: Touchstone (Core, Cli, XunitAdapter, NunitAdapter) 0.1.12 -> 0.2.0,
  Microsoft.NET.Test.Sdk 17.14.1 -> 18.10.1, coverlet.collector 6.0.4 -> 10.1.0,
  xunit.runner.visualstudio 3.1.4 -> 4.0.0, NUnit 4.3.2 -> 5.0.0, NUnit.Analyzers 4.7.0 -> 4.15.0,
  NUnit3TestAdapter 5.0.0 -> 6.3.0. No source changes were required.

### Added
- Regression tests for the upgraded dependencies: `BlobS3Store` surfaces a failure (not an
  empty result) when the endpoint refuses connections, and `ObjectLocks` leaves a key
  acquirable after a canceled wait and honors a pre-canceled token.

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
  bucket per drive); and a file-based TUI-to-agent command and status channel. Configuring a
  drive auto-mounts it, and drives re-mount when the agent starts. A mounted drive is a normal
  Windows volume and can be shared on the network from Windows Explorer; S3Drive does not manage
  sharing itself.
- `S3Drive.Agent`: an always-on Avalonia system-tray agent (About, per-drive mount/unmount,
  Exit) that owns all mounts and runs independently of the TUI. The tray menu is rebuilt in
  place on status changes; replacing the menu instance crashed the agent on macOS.
- `S3Drive.Tui`: a TUIKit console for configuring connections, sending mount/unmount commands,
  and live-monitoring status and logs; it starts the agent if it is not already running.
- `go.bat` developer script: builds the solution and launches the TUI; `go.sh` is the bash
  equivalent.
- TUI mouse support (TUIKit 1.1.1): click a pane to focus it, click or use the arrow keys to
  highlight a drive (Enter or double-click edits it), click any shortcut hint, click fields,
  checkboxes, and Save/Cancel in the drive form, and click dialog buttons. Drag to select text
  and press Ctrl+C to copy; F12 hands the mouse back to the terminal. Drive actions now apply to
  the highlighted drive instead of prompting for one.
- `Test.Automated`: 73 tests covering key mapping, caching, configuration, cryptography,
  locking, sharing, the IPC channel, and the Dokan filesystem, plus storage integration tests
  that run against any S3 or S3-compatible endpoint (CLI arguments or `S3DRIVE_TEST_*`
  variables). `test/run-integration.{sh,bat}` exercises them against an ephemeral Less3
  container. A `--mount-test` mode performs a real Dokan mount and drives operating-system-level
  file operations against the mounted drive; `--make-config` and `--send-command` drive a full
  agent end-to-end test (auto-mount from config, then unmount via the command channel). These
  require Windows and the Dokany driver. Verified end-to-end against Less3: the agent
  auto-mounted a bucket to a drive letter, round-tripped files through Windows, and unmounted.
- Test infrastructure migrated to [Touchstone](https://github.com/jchristn/touchstone): every
  test now lives once in `Test.Shared` as a Touchstone descriptor and runs through
  `Test.Automated` (console, JSON results via `--results`), `Test.Xunit`, and `Test.Nunit`. The
  interactive `--mount-test`, `--make-config`, and `--send-command` harness modes are unchanged.
  Coverage grew from 70 to 225 cases, positive and negative, adding the mount manager, logging,
  offline `BlobS3Store` validation, every file-open disposition, offset and append writes,
  metadata-cache coherency after writes, deletes, and renames, store-failure and unmount
  (cancellation) mapping to NTSTATUS codes, and a filesystem end-to-end case against the live
  endpoint. Integration cases now use a unique key prefix per case and clean up after
  themselves.
- Observability (see `TELEMETRY.md`). `S3Drive.Core` emits metrics and traces through the .NET
  base class library on the `S3Drive` meter and activity source (no telemetry SDK dependency):
  per-operation counters and latency histograms with bounded outcome and `error.type` labels for
  every Dokan filesystem operation; per-stage histograms and spans for download, upload, copy,
  delete, list_all, and mkdir; an `S3 <Operation>` client span plus request, latency, byte, and
  error-code metrics for every outbound S3 call (`InstrumentedS3Store`); metadata cache
  hit/miss/entries/invalidations; per-object lock wait, held, and contention; mount and unmount
  operations with stages and per-drive state gauges; the agent command pipeline as a traced job
  (queued, parse, and execute stages, job outcomes, last-success and heartbeat gauges);
  status/config I/O; log-line and crash counters; and build-info and configuration gauges. HEAD
  failures that were previously reported to Windows as "file not found" are now counted as
  `s3drive.s3.suppressed_errors`, and a failed upload on close is now logged and counted instead of
  silently dropped.
- W3C trace context crosses the TUI-to-agent command channel: `AgentCommand` carries the sender's
  `TraceParent` and `CreatedUtc`, and the agent's command span joins the sender's trace.
- `S3Drive.Agent` hosts a single [Radiant](https://www.nuget.org/packages/Radiant) 0.1.2 telemetry
  host configured from a new `Telemetry` settings section (OTLP, in-process Prometheus endpoint,
  log export to the collector or Loki, sampling ratio, opt-in object keys on spans), each key with
  an `S3DRIVE_*` environment override. All exporters are off by default; the defaults use
  `127.0.0.1`. Also subscribes to the `System.Net.Http` meter and .NET runtime metrics.
- `docker/compose.yaml` observability stack (OpenTelemetry Collector, Prometheus, Tempo, Loki,
  Grafana; pinned images, healthchecks, `127.0.0.1`-bound overridable ports) with
  `docker/update.{bat,sh}`, and five provisioned Grafana dashboards in `assets/grafana/` (Overview,
  Filesystem, S3 & Integrations, Cache & Locks, Agent).
- Telemetry test suite (in-memory `MeterListener`/`ActivityListener`) covering every instrumented
  area, failure and cancellation paths, trace nesting and propagation, label cardinality, and the
  no-listener path, plus a live-endpoint classification case.
