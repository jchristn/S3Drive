# CLAUDE.md — S3Drive

Guidance for working in this repository.

## What this is

S3Drive exposes an S3 bucket as a local Windows drive via Dokan.NET, driven by an always-on
tray agent, and configured/monitored by a TUIKit terminal app. The full design is in
`archive/S3DRIVE_PLAN.md` — read it before making structural changes.

## Fixed product decisions

- **Amazon S3 and S3-compatible endpoints** are both first-class: Less3, Ceph, MinIO, and
  others. Every connection carries endpoint URL, SSL toggle, access/secret keys, region, and
  **path-style vs virtual-hosted** addressing.
- **One drive = one bucket.** The drive root maps to a single bucket; keys are files,
  `/`-prefixes are folders.
- **Multiple simultaneous mounts.** Each connection mounts its bucket to its own drive letter.
- **Network re-sharing (CIFS/SMB)** of any mounted drive is a first-class feature (off by
  default, read-only default, never `Everyone`, requires admin elevation).
- **Run model.** The tray agent is the always-on component and owns all mounts and shares. The
  system keeps operating whether or not the TUI is running. The TUI is a client that
  configures, monitors, and sends commands; on startup it launches the agent if it is not
  already running.
- **Storage client:** Blobject (`Blobject.AmazonS3`). **Locks:** Padlock, coarse per-object.
  **Logging:** SyslogLogging. **Target framework:** net8.0. **Windows only** (Dokan).
- **Coordination:** TUI and agent coordinate through the filesystem under `~/.s3drive/state/`
  (single-instance lock, `status.json`, and a command drop directory) — no HTTP/socket control
  surface.

## Requirements scope

Only `CODE_STYLE.md` and repository housekeeping from `C:\code\Agents\requirements\` apply.
The web/backend requirements (Watson HTTP, multi-tenancy, RBAC/AAA, SQL providers, React
dashboard, telemetry, i18n, SDKs) do **not** apply to this desktop app.

## Coding conventions (from CODE_STYLE.md)

- Block-scoped namespaces; `using` directives **inside** the namespace, system usings first
  (alphabetical), then others (alphabetical).
- No `var`; no tuples; no partial classes; **one class or one enum per file**.
- Private fields `_PascalCase`; public members `LikeThis`; constants `PascalCase`.
- `Nullable` enabled; guard clauses with specific exception types and contextual messages;
  do not swallow exceptions without adding context.
- XML docs on **all** public members/constructors/methods/enums (with `<exception>` and
  documented defaults/min/max); **no** docs on private members.
- `ConfigureAwait(false)` on every library `await`; `CancellationToken` on async methods
  (unless the class holds a `CancellationTokenSource`); async variant for `IEnumerable`
  returns.
- Fixed contracts (config, status, commands) are **named types**, never `JsonNode`/
  `JsonElement`.
- No `Console.WriteLine` in library code (`S3Drive.Core`); use the logger / TUI pane.
- Classic `using (...) { }` blocks; full Dispose pattern where resources are held.
- Secrets never logged or returned in plaintext; masked in the UI; encrypted at rest.
- Use `127.0.0.1`, not `localhost`.

## Build and run

- `go.bat` — builds the solution and launches the TUI (which starts the agent if needed).
- Build must be warnings-clean (`TreatWarningsAsErrors=true`).
- Shared build settings live in the root `Directory.Build.props` (net8.0, version 0.1.0).
