# S3Drive — Implementation Plan

S3Drive exposes an S3 bucket (AWS or S3-compatible) as a local Windows drive using
[Dokan.NET](https://github.com/dokan-dev/dokan-dotnet). Logic runs in an always-on tray
agent; a TUIKit terminal app configures and monitors it. Structure and conventions mirror
`c:\code\armor` (three-project split, filesystem-coordinated agent, `SyslogLogging`,
`System.Text.Json` config, code-only Avalonia tray) and borrow the catalog-driven TUI
patterns from `c:\code\mux`.

Version at first release: **v0.1.0 Alpha** — © 2026 Joel Christner —
<https://github.com/jchristn/S3Drive>.

---

## 1. Scope and decisions

Confirmed with the requestor before planning:

| Decision | Choice |
|---|---|
| Requirements to honor | `CODE_STYLE.md` + repository housekeeping only. The backend/web requirements (Watson HTTP, multi-tenancy, RBAC/AAA, four SQL providers, React dashboard, telemetry, i18n, SDKs) do **not** apply — S3Drive has none of those surfaces. |
| Drive → S3 mapping | **One bucket = one drive.** The drive root is a single configured bucket; keys are files, `/`-delimited prefixes are folders. |
| Concurrent mounts | **Multiple simultaneous.** Each connection profile mounts one bucket to its own drive letter; the tray lists each with its own mount/unmount. |
| S3 client library | **Blobject** (`Blobject.AmazonS3`), the abstraction `armor` uses. |
| Network re-sharing | **Explicit, first-class.** Each mounted drive can optionally be re-exposed as a **CIFS/SMB share** so other machines on the network can connect. Off by default; see §6.7. |
| Target framework | **net8.0** (single TFM), per the Dokan.NET target. `armor` multi-targets net8.0/net10.0; S3Drive stays on net8.0 to match the Dokan requirement and reduce surface. |
| Platform | **Windows only.** Dokan requires the Dokany kernel driver installed on the host; documented as a prerequisite. |

The requirements path the requestor referenced (`c:\code\assets\requirements`) does not exist;
the live set is `C:\code\Agents\requirements\`. `CODE_STYLE.md` (§13) drives the conventions
checklist below.

---

## 2. Prerequisites

- Windows 10/11 x64.
- **Dokany driver** installed (the `Dokan.NET` NuGet is a managed wrapper over the native
  driver). The README documents the download link and that mounting fails cleanly with a
  clear message if the driver is absent.
- .NET 8 SDK to build; .NET 8 Desktop Runtime to run the Avalonia tray agent.

---

## 3. Solution and project layout

```
C:\Code\Less3\S3Drive\
  S3Drive.sln
  Directory.Build.props           (TFM, version, copyright, nullable, warnings-as-errors)
  README.md  CHANGELOG.md  LICENSE.md  .gitignore
  assets\                         (logo.png, logo.ico  — already present; logo.pptx source)
  src\
    S3Drive.Core\                 classlib  net8.0   — all logic
    S3Drive.Agent\                WinExe    net8.0   — Avalonia tray, owns the mounts
    S3Drive.Tui\                  Exe       net8.0   — TUIKit config + monitoring console
  test\
    Test.Automated\               console harness for Core (path mapping, config, cache, locks)
```

Three shipping projects, matching `armor`. `Test.Automated` is a lightweight console harness
for the deterministic Core logic (key⇄path mapping, config round-trip, cache invalidation,
lock behavior) — not the full four-runner Touchstone matrix, which belongs to the
backend-test requirements that were scoped out.

### 3.1 Package references

| Project | Packages |
|---|---|
| `S3Drive.Core` | `DokanNet` (Dokan.NET wrapper), `Blobject.AmazonS3` (+ transitive `Blobject.Core`), `Padlock` (named locks), `SyslogLogging`, `PrettyId` (drive/profile IDs), `System.Text.Json` (in-box) |
| `S3Drive.Agent` | `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` — plus `ProjectReference` → `S3Drive.Core`. `EnableAvaloniaXamlCompilation=false`; tray built entirely in code (no XAML), as in `armor`. |
| `S3Drive.Tui` | `TUIKit` (0.8.4, exact) — plus `ProjectReference` → `S3Drive.Core`. |
| `Test.Automated` | `ProjectReference` → `S3Drive.Core`. |

Pin package versions in each `.csproj`; keep shared build properties central.

### 3.2 `Directory.Build.props` (at `src\`)

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>   <!-- usings are explicit, inside the namespace -->
    <LangVersion>latest</LangVersion>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Authors>Joel Christner</Authors>
    <Company>Joel Christner</Company>
    <Product>S3Drive</Product>
    <Copyright>(c) 2026 Joel Christner</Copyright>
    <Version>0.1.0</Version>
  </PropertyGroup>
</Project>
```

`ImplicitUsings` is disabled deliberately: `CODE_STYLE.md` requires explicit `using`
directives placed **inside** the `namespace` block.

---

## 4. On-disk layout (`~/.s3drive`)

Root defaults to `%USERPROFILE%\.s3drive`, overridable via an `S3DRIVE_HOME` env var (tests,
isolation). Created by `S3DrivePaths.EnsureDirectories()`.

```
~/.s3drive/
  s3drive.json                 config (connection profiles + global settings)
  logs/                        SyslogLogging dated files (s3drive.log)
  crash-logs/                  crash-<yyyyMMdd-HHmmss-fff>.log
  state/
    agent.lock                 single-instance guard (FileShare.None)
    dp.key                     machine-local AES key for secret-at-rest encryption
    status.json                agent-published live status (atomic writes)
    commands/                  TUI → agent command drop (JSON files, watched)
  cache/
    <driveId>/                 per-mount staging: write buffers, optional read cache
```

---

## 5. Configuration (`s3drive.json`)

Named types only — no `JsonNode`/`JsonElement` for this fixed contract (`CODE_STYLE.md`).
Serialized with one shared `JsonSerializerOptions`: `WriteIndented`,
`PropertyNameCaseInsensitive`, `DefaultIgnoreCondition = WhenWritingNull`,
`JsonStringEnumConverter`. Writes are atomic (temp sibling + `File.Move(overwrite:true)`);
reads open shared with retry (the `mux` discipline) so the TUI and agent can both touch it.

Secret keys are **encrypted at rest** (AES-256-GCM via a machine-local key in `state/dp.key`,
owner-only permissions), mirroring `armor`'s `CredentialProtector`. Plaintext secrets are
never written to the config file or logs; the UI masks them (set / not-set, last-4).

### 5.1 Model

- `S3DriveSettings`
  - `CreatedUtc` (DateTime)
  - `Logging` : `LoggingSettings { ConsoleLogging, FileLogging }`
  - `MetadataCacheSeconds` (int, clamped 0–3600, default 5)
  - `MultipartThresholdBytes` (long, clamped, default 16 MiB)
  - `Drives` : `List<DriveProfile>`
- `DriveProfile`
  - `Id` (string, `drv_` PrettyId), `Name` (string)
  - `Provider` : `S3ProviderEnum { AwsS3, S3Compatible }`
  - `ServiceUrl` (string?, required for `S3Compatible`), `UseSsl` (bool)
  - `Region` (string?), `Bucket` (string)
  - `AccessKey` (string), `SecretKeyEncrypted` (string) — never plaintext on disk
  - `UsePathStyle` (bool — path vs virtual-hosted addressing)
  - `DriveLetter` (string, e.g. `"S:"`), `AutoMount` (bool)
  - `Share` : `SmbShareSettings` (see below) — network re-sharing for this drive
- `SmbShareSettings`
  - `Enabled` (bool, default **false**)
  - `ShareName` (string, e.g. `"S3Drive-Prod"` — the network share name)
  - `Access` : `ShareAccessEnum { ReadOnly, ReadWrite }` (default `ReadOnly`)
  - `AllowedPrincipals` (`List<string>` — Windows accounts/groups granted access; empty ⇒
    a conservative default such as `Authenticated Users`, never `Everyone` implicitly)
  - `Description` (string?)

Scalar setters self-clamp/validate; guard clauses reject nulls. Enums serialize as strings.

### 5.2 Example

```json
{
  "CreatedUtc": "2026-08-29T00:00:00Z",
  "Logging": { "ConsoleLogging": false, "FileLogging": true },
  "MetadataCacheSeconds": 5,
  "MultipartThresholdBytes": 16777216,
  "Drives": [
    {
      "Id": "drv_2b0f...",
      "Name": "Prod backups",
      "Provider": "AwsS3",
      "Region": "us-east-1",
      "Bucket": "acme-backups",
      "AccessKey": "AKIA...",
      "SecretKeyEncrypted": "b64(aes-gcm(...))",
      "UsePathStyle": false,
      "UseSsl": true,
      "DriveLetter": "S:",
      "AutoMount": true,
      "Share": {
        "Enabled": true,
        "ShareName": "S3Drive-Prod",
        "Access": "ReadWrite",
        "AllowedPrincipals": ["DOMAIN\\Backups"],
        "Description": "Prod backups bucket over SMB"
      }
    },
    {
      "Id": "drv_9c41...",
      "Name": "MinIO lab",
      "Provider": "S3Compatible",
      "ServiceUrl": "http://127.0.0.1:9000",
      "UseSsl": false,
      "Bucket": "scratch",
      "AccessKey": "minioadmin",
      "SecretKeyEncrypted": "b64(aes-gcm(...))",
      "UsePathStyle": true,
      "DriveLetter": "T:",
      "AutoMount": false,
      "Share": { "Enabled": false }
    }
  ]
}
```

---

## 6. `S3Drive.Core` — the engine

One type per file. Namespaces block-scoped, usings inside, `_PascalCase` private fields,
XML docs on the public surface, `ConfigureAwait(false)`, `CancellationToken` on async.

### 6.1 Foundation

- `Configuration/S3DrivePaths.cs` — resolves root, derived paths, `EnsureDirectories()`.
- `Configuration/SettingsManager.cs` — `LoadAsync`/`SaveAsync`; seeds defaults on first run;
  atomic write, shared read with retry.
- `Configuration/S3DriveSettings.cs`, `DriveProfile.cs`, `LoggingSettings.cs`,
  `S3ProviderEnum.cs`.
- `Serialization/S3DriveJson.cs` — the shared `JsonSerializerOptions`.
- `Diagnostics/S3DriveLog.cs` — static null-safe facade over `SyslogLogging.LoggingModule`
  (`Initialize`, `Debug/Info/Warn/Error/Exception`, `WriteCrash`, `Flush`, `Dispose`,
  `MessageLogged` event for the TUI pane). Console logging **off** for the TUI so log lines
  don't corrupt the screen; the TUI mirrors `MessageLogged` into an on-screen Activity pane
  (the `armor` pattern).
- `Security/CredentialProtector.cs` + `Security/AesGcmCipher.cs` — encrypt/decrypt secret
  keys with the machine-local `dp.key`.
- `Constants.cs` — product name, tagline, GitHub URL, ID prefixes (`drv_`), default letters.

### 6.2 Storage layer (Blobject)

- `Storage/IS3Store.cs` / `Storage/BlobS3Store.cs` — wraps a `Blobject.AmazonS3`
  `AwsS3BlobClient` built from a `DriveProfile`. Surface used by the filesystem layer:
  - `ListAsync(prefix, delimiter, ct)` → entries + common prefixes (directory listing)
  - `HeadAsync(key, ct)` → exists/size/etag/last-modified (or null)
  - `GetRangeAsync(key, offset, count, ct)` → bytes (ranged read)
  - `PutAsync(key, stream/bytes, ct)` → whole-object write (multipart above threshold)
  - `DeleteAsync(key, ct)`, `CopyAsync(src, dst, ct)` (rename = copy + delete)
  - `PutFolderMarkerAsync(prefix, ct)` for empty folders
  - **Verification item:** confirm Blobject's `AwsSettings` exposes custom `ServiceUrl`,
    `UseSsl`, region, and **path-style vs virtual-hosted** addressing. If path-style isn't a
    first-class Blobject setting, construct the underlying `AmazonS3Client`
    (`AmazonS3Config { ServiceURL, ForcePathStyle, UseHttp }`) and hand it to Blobject, or
    hold the `AmazonS3Client` directly for the operations that need it. Resolve during
    Phase 1 spike before committing the abstraction.

### 6.3 Filesystem ⇄ object semantics

Key mapping helper `Storage/KeyMapper.cs`:
- Path `\a\b\file.txt` ⇄ key `a/b/file.txt`. Drive root ⇄ empty prefix.
- Folders are prefixes ending in `/`. Empty folders persist via a zero-byte `prefix/` marker
  object; listing hides markers and synthesizes folder entries from common prefixes.
- Deterministic, unit-tested both directions (round-trip, edge cases: root, trailing slash,
  reserved chars).

Metadata cache `Storage/MetadataCache.cs`:
- In-memory cache of directory listings and object attributes, TTL = `MetadataCacheSeconds`.
- Invalidated immediately on local mutations (Put/Delete/Copy update or evict affected
  entries) so the drive reflects its own writes without waiting for TTL.
- Persistent (SQLite) caching is intentionally out of scope for Alpha.

### 6.4 Dokan operations

- `FileSystem/S3DriveFileSystem.cs` implements Dokan's `IDokanOperations` for one mounted
  bucket. Read/write model driven by the 1-file-=-1-object, immutable-object constraints:
  - **Read.** `ReadFile(offset, length)` → `GetRangeAsync`. Attributes/listing served from
    the metadata cache. `FindFiles`/`FindFilesWithPattern` → `ListAsync` with `/` delimiter.
  - **Write.** S3 objects are immutable and support no range writes, so an opened-for-write
    handle **stages to a local temp file** under `cache/<driveId>/`. `WriteFile(offset,…)`
    writes to the stage; `SetEndOfFile`/`SetAllocationSize` size the stage. On
    `Cleanup`/`CloseFile` (and `FlushFileBuffers`), the whole stage is `PutAsync`'d as the
    object (multipart above threshold), the cache is updated, and the stage is deleted.
  - **Range locks disallowed.** `LockFile`/`UnlockFile` are no-ops returning success (byte
    ranges are meaningless against a whole-object backing store); coherency is enforced by
    the coarse per-object lock below, not by range locks.
  - **Directory ops.** `CreateDirectory` writes a folder marker; `DeleteDirectory` verifies
    emptiness (or removes marker); `MoveFile` = copy-then-delete (recursive for prefixes).
  - **Metadata.** `GetFileInformation` from cache/HEAD. Timestamps map to `LastModified`;
    size from object length; attributes synthesized (folders = directory).
  - Unsupported/irrelevant ops fail cleanly with the correct `NtStatus` rather than throwing.
  - **Verification item:** confirm the current `Dokan.NET` mount API surface
    (`DokanInstanceBuilder` / `Dokan.CreateFileSystem`, interface version
    `IDokanOperations` vs newer) against the pinned package before implementing.

### 6.5 Coarse locking (Padlock)

- `Concurrency/ObjectLocks.cs` wraps `Padlock` named locks keyed by `driveId + "|" + key`.
- Any handle that may write to an object acquires the named lock for the whole
  open→stage→PUT lifecycle; the filesystem serializes conflicting operations on the same
  object. Directory-level mutations (rename/delete of a prefix) lock the prefix scope.
- Because the agent is **single-instance** (§7.1) and owns every Dokan mount in one process,
  the only concurrency axis is cross-thread within the agent; Padlock covers it. The
  single-instance guard covers the cross-process axis. Together they deliver the requested
  guarantee — consistency and coherency first, even at the cost of concurrent access.
- **Verification item:** confirm Padlock's exact API (sync/async acquire, disposable handle,
  timeout). Model locks as exclusive per key; if Padlock offers reader/writer semantics,
  reads may take shared locks, otherwise reads take the exclusive lock too (acceptable given
  the stated priority).

### 6.6 Mount manager

- `Mounting/MountManager.cs` — owns the set of active mounts, one background thread per
  drive (`DokanInstanceBuilder…Build()` / `Dokan.CreateFileSystem`). `MountAsync(profile)`,
  `UnmountAsync(driveId)`, `UnmountAllAsync()`, `MountedIds`, and a `StatusChanged` event
  feeding the tray and the published `status.json`. Handles driver-missing and
  letter-in-use failures with logged, user-readable messages.
- After a successful mount, if `profile.Share.Enabled`, the manager creates the SMB share
  (§6.7); on unmount it removes the share **first**, then unmounts the volume. Share state is
  part of the per-drive status.

### 6.7 CIFS/SMB network re-sharing

The mounted Dokan volume is a normal Windows drive letter, so it can be re-exported over SMB
for other machines on the network. This is an explicit deliverable.

- `Sharing/ISmbShareManager.cs` / `Sharing/WindowsSmbShareManager.cs`:
  - `CreateShareAsync(profile, mountPath, ct)` — publishes an SMB share pointing at the
    mounted drive/path, with the configured name, access level, and principal ACLs.
  - `RemoveShareAsync(shareName, ct)` — removes the share.
  - `ShareExistsAsync(shareName, ct)`, `ListManagedSharesAsync(ct)` — reconciliation on
    startup and after crashes so stale shares are cleaned up.
  - Implementation via the Win32 Server service API (`NetShareAdd` / `NetShareDel` /
    `NetShareEnum` P/Invoke) with explicit share-level `SECURITY_DESCRIPTOR` ACLs built from
    `AllowedPrincipals` and `Access`. A PowerShell (`New-SmbShare` / `Grant-SmbShareAccess` /
    `Remove-SmbShare`) fallback path is acceptable if the P/Invoke ACL plumbing proves
    costly for Alpha — decided in the Phase 1 spike (§13).
- **Visibility to the SMB service (critical gotcha).** The Windows SMB server
  (`LanmanServer`) runs as `SYSTEM`, while a Dokan drive is, by default, mounted in the
  launching user's session/security context and is **not visible** to `SYSTEM` — so a naive
  `net share` against it fails or serves nothing. The mount must therefore be made
  system-visible. Options, resolved during the spike (§13): mount through the **Dokan Mount
  Manager** (`DokanOptions.MountManager`) and/or mount as a **network/removable** volume with
  the appropriate flags, and confirm the volume is reachable by the `LanmanServer` service.
  If a session-scoped mount cannot be shared reliably, the agent (or a small elevated helper)
  performs the mount in a system-visible context.
- **Elevation.** Creating/removing SMB shares and altering share ACLs requires administrative
  privileges. The agent detects when it lacks elevation and surfaces a clear, actionable
  message (in the tray and TUI) rather than failing silently; the README documents the
  requirement. Design keeps share operations isolated behind `ISmbShareManager` so an
  elevated-helper or manifest-based elevation strategy can be slotted in without touching the
  filesystem/mount code.
- **Security posture.** Re-sharing exposes cloud-backed data on the LAN. Sharing is **off by
  default**, never grants `Everyone` implicitly, defaults to `ReadOnly`, and the README calls
  out the exposure explicitly. Share ACLs are additive to (not a replacement for) the S3
  credentials the agent holds.
- **Lifecycle.** Shares are created only after the backing mount is live and removed before
  the mount goes away; on agent shutdown all managed shares are removed. On startup the agent
  reconciles `status.json` + `ListManagedSharesAsync` to drop orphaned shares from a prior
  crash.
- **Cross-platform note.** The manager is behind an interface; the Alpha ships the Windows
  (`LanmanServer`) implementation only, consistent with the Windows-only Dokan requirement.

---

## 7. `S3Drive.Agent` — tray, owner of the mounts

Avalonia `WinExe`, no main window, code-only (no XAML), `EnableAvaloniaXamlCompilation=false`
— exactly `armor`'s agent shape. Runs until the tray Exit action
(`StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown)`).

### 7.1 Startup and single-instance

`Program.cs`: paths → `S3DriveLog.Initialize` → global exception handlers
(`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` → `WriteCrash`) →
**single-instance guard** (`AgentInstanceLock` = exclusive `state/agent.lock`, `FileShare.None`;
a second agent exits cleanly) → build tray + host.

### 7.2 Tray

- Avalonia `TrayIcon` + `NativeMenu`/`NativeMenuItem`; icon loaded from embedded
  `logo.ico`. Menu:
  - **About** → `AboutWindow`.
  - **Open** → launches the TUI (`Process.Start`, `UseShellExecute`, beside-the-exe /
    sibling dev path discovery, as in `armor`'s `TerminalLauncher`).
  - **Mounts** submenu — one entry per configured drive with live **Mount/Unmount**, a
    **Share/Unshare** toggle, and a status glyph (mounted + shared); updated via
    `MountManager.StatusChanged` + `Dispatcher.UIThread.Post`.
  - A disabled **Status: …** line.
  - **Exit** → unmount all, dispose, `desktop.Shutdown()`.
- `AboutWindow.cs` — small non-resizable code-built window: `logo.png`, "S3Drive",
  "v0.1.0 Alpha", "© 2026 Joel Christner", and the clickable repo URL
  <https://github.com/jchristn/S3Drive>, plus Close.

### 7.3 Host and command channel

- `AgentHost.cs` — loads settings, auto-mounts profiles with `AutoMount = true` (creating
  their SMB shares where enabled), reconciles orphaned shares from a prior crash, publishes
  `state/status.json` (atomic — including per-drive share name/state) on every change, and
  watches `state/commands/` via `FileSystemWatcher`. Share create/remove is driven through
  the `MountManager` so it always tracks the mount lifecycle.
- **Command channel (file-based, no network).** The TUI drops a small JSON command file
  (`{ "type": "mount|unmount|mount-all|unmount-all|reload", "driveId": "drv_…" }`) into
  `state/commands/`; the agent executes it, deletes the file, and republishes status. This
  keeps *all logic and the actual Dokan mounts in the agent* and *the drive exposed via the
  agent*, per the requirement, while staying within the `armor` filesystem-coordination
  ethos (no HTTP/socket control surface — consistent with the "code style + repo only"
  scope). Typed command/status DTOs, not `JsonElement`.

---

## 8. `S3Drive.Tui` — configuration and monitoring

TUIKit console (`armor`'s shell shape + `mux`'s command catalog). `Program.cs`: paths →
`EnsureDirectories` → `S3DriveLog.Initialize` (console off) → global handlers →
`AgentLauncher.EnsureRunning` (start the agent if `agent.lock` is free) → build controller →
`TuiApp.RunAsync`.

### 8.1 Shell and catalog

- Application-shell layout with named regions (header banner, nav, content, activity log,
  status, key hints), built with TUIKit `Layout.Create()`.
- A single `CommandDescriptor` catalog (the `mux` pattern) feeds keybindings, the menu bar,
  and a command palette so they can't drift: Add drive (`c`), Edit (`e`), Delete (`d`),
  Mount (`m`), Unmount (`u`), Refresh (`F5`), Help (`F1`), Quit (`Ctrl+Q`).
- Left-nav sections: **Drives** (table: name, endpoint, bucket, letter, mount state, share
  name/state), **Activity** (log pane bound to `S3DriveLog.MessageLogged`), **About/Status**.
  The command catalog also exposes share enable/disable per drive.

### 8.2 Modals

- `DriveFormModal` (modeled on `mux`'s `EndpointFormModal`) — a TUIKit `Form` with
  `TextField`/`RadioGroup`/`Checkbox` for name, provider (AwsS3 / S3Compatible), service URL,
  SSL, region, bucket, access key, secret key (masked), path-style, drive letter, automount,
  and a **Network sharing** group (enable, share name, ReadOnly/ReadWrite, allowed
  principals, description); per-field validators; Esc cancels, Enter validates + saves.
- Confirm modal for delete/unmount. A splash modal with the S3Drive wordmark on launch
  (optional, `armor`'s pattern).

### 8.3 Talking to the agent

- Mount/unmount/share actions write a command file to `state/commands/`; the TUI reflects
  results by reading `state/status.json` (poll + refresh). Config edits save `s3drive.json` and drop
  a `reload` command so the running agent re-reads profiles.

### 8.4 Run model and `go.bat`

- **The tray agent is the always-on component.** It owns every Dokan mount and SMB share and
  runs independently of the TUI. Mounts and shares stay live **whether or not the TUI is
  open** — closing the TUI unmounts nothing; only the tray **Exit** tears mounts and shares
  down. The TUI is purely a client for configuration, monitoring, and issuing commands.
- **TUI-ensures-agent.** On startup the TUI probes `state/agent.lock`
  (`AgentInstanceLock.IsRunning`) and, if the agent isn't running,
  `AgentLauncher.EnsureRunning` starts it detached (`Process.Start`, `UseShellExecute`,
  beside-the-exe / sibling dev-path discovery). The agent's own single-instance guard makes a
  duplicate launch a no-op.
- **`go.bat`** (repo root) — the developer entry point:
  1. Builds the solution (`dotnet build S3Drive.sln -c Debug`), aborting on failure.
  2. Runs the TUI (`dotnet run --project src\S3Drive.Tui`).
  3. The TUI then ensures the agent is running per the point above.
  It builds and runs `S3Drive.sln`; the classic `.sln` format is used (not `.slnx`) for
  compatibility with .NET 8 SDK / CI machines.

---

## 9. Versioning, About, branding

- Version centralized in `Directory.Build.props` (`0.1.0`); read at runtime via
  `Assembly.GetName().Version`. Displayed as **v0.1.0 Alpha** in the About window and the
  TUI header/splash.
- Copyright **© 2026 Joel Christner**; `Product` = "S3Drive"; `Authors`/`Company` =
  "Joel Christner".
- GitHub link **<https://github.com/jchristn/S3Drive>** in About, the TUI header, and README.
- Tray tooltip and header tagline, e.g. "S3Drive — your S3 bucket as a local drive."
- Logos from `assets\logo.png` (About) and `assets\logo.ico` (tray), embedded as resources.

---

## 10. Repository housekeeping (deliverables)

Per `REPOSITORY_REQUIREMENTS.md` (the applicable subset):

- `README.md` — what it is, Windows + Dokany prerequisite, install/build/run, configuring a
  drive, mount/unmount, the `~/.s3drive` layout, the S3/S3-compatible options (endpoint URL,
  SSL, path vs virtual-hosted, keys, region), and a **Network sharing (CIFS/SMB)** section
  covering enabling a share, the administrator-elevation requirement, share ACLs, and the
  security implications of exposing cloud-backed data on the LAN. Keep accurate as code lands.
- `CHANGELOG.md` — start at `0.1.0`.
- `LICENSE.md` — MIT.
- `.gitignore` — .NET/VS/build artifacts, `~/.s3drive` never in-repo.
- `assets\` — existing `logo.png`, `logo.ico` (and `logo.pptx` source); referenced by
  explicit repo-relative paths in docs.
- A default/sample settings file (`s3drive.sample.json`) documenting every field.
- `go.bat` (repo root) — builds the solution and launches the TUI (§8.4).
- Source under `src\`, tests under `test\`. No Docker/REST/SDK/Postman deliverables (N/A).
- A repo `CLAUDE.md` capturing the conventions (CODE_STYLE.md asks for this).

---

## 11. Coding-conventions checklist (`CODE_STYLE.md`)

Enforced throughout:

- Block-scoped namespaces; `using`s **inside** the namespace, system usings first
  alphabetically, then others alphabetically.
- No `var`; no tuples; no partial classes; **one class or one enum per file**.
- `Nullable` enabled; guard clauses (`ArgumentNullException.ThrowIfNull`, specific exception
  types with contextual messages); no swallowed exceptions without added context.
- Private fields `_PascalCase`; public `LikeThis`; constants `PascalCase`.
- Public members with validation use explicit getters/setters over backing fields;
  clamp numeric ranges; configurable values are properties with sensible defaults, not magic
  constants.
- XML docs on **all** public members/ctors/methods/enums, including `<exception>` and
  documented defaults/min/max; **no** docs on private members.
- `ConfigureAwait(false)` on every library `await`; `CancellationToken` on async methods
  (unless the class holds a `CancellationTokenSource`); check cancellation at sensible points;
  async variant for any `IEnumerable`-returning method.
- Named types for the config/status/command contracts — no `JsonNode`/`JsonElement` DOM.
- No `Console.WriteLine` in library code (`S3Drive.Core`); UI/log output goes through the
  logger and the TUI pane.
- Classic `using (…) { }` blocks, full Dispose pattern where resources are held.
- Secrets never logged or returned in plaintext; masked in the UI; encrypted at rest.
- Loopback references use `127.0.0.1`, not `localhost`.

---

## 12. Build and verification

- Build the solution warnings-clean (`TreatWarningsAsErrors=true`).
- `Test.Automated` covers Core determinism: key⇄path mapping, config load/save round-trip,
  metadata-cache invalidation, and lock serialization.
- Manual acceptance: install Dokany; configure an AWS bucket and a MinIO (S3-compatible,
  path-style, non-SSL) profile; mount both to separate letters simultaneously; verify
  create/read/write/rename/delete of files and folders, large-file multipart PUT on close,
  ranged reads, empty-folder persistence, and clean unmount; confirm the tray About window,
  mount/unmount from both tray and TUI, agent auto-start from the TUI, single-instance
  behavior, and crash-log capture. Enable network sharing on a drive, confirm the share is
  reachable and read/write-correct **from a second machine on the network**, that the share
  is removed on unmount and on agent shutdown, and that a stale share left by a killed agent
  is reconciled away on next startup.

---

## 13. Open verification items (resolve in Phase 1 spike)

1. **Blobject** `AwsSettings` coverage of custom `ServiceUrl` / `UseSsl` / region and
   **path-style vs virtual-hosted**. Fallback: build/hold the underlying `AmazonS3Client`
   with `AmazonS3Config { ServiceURL, ForcePathStyle, UseHttp }`.
2. **Dokan.NET** current mount API and interface version for the pinned package.
3. **Padlock** exact API (async acquire, disposable handle, timeout, reader/writer or
   exclusive-only).
4. **TUIKit 0.8.4** API parity with the `armor`/`mux` usage referenced here.
5. **SMB re-sharing** — the two blockers in §6.7: (a) making the Dokan mount **visible to the
   `LanmanServer` (SYSTEM) service** so an SMB share against it actually serves data
   (Mount-Manager vs network/removable mount flags, or a system-context mount); and (b) the
   **elevation** path for `NetShareAdd`/ACLs (in-process elevated agent, elevated helper, or
   the PowerShell `New-SmbShare` fallback). Prove an end-to-end share from a second machine
   before committing the approach.

---

## 14. Phased implementation

1. **Spike** — resolve §13. Prove: Blobject read/write against AWS + MinIO with path-style;
   a minimal Dokan mount serving a read-only listing; Padlock acquire/release; and a Dokan
   mount that is **visible to `LanmanServer` and shareable over SMB** from a second machine
   (settles the mount-flags + elevation approach for §6.7).
2. **Core foundation** — paths, settings (+ encryption), logging facade, constants,
   `Test.Automated` scaffolding.
3. **Storage + mapping + cache** — `BlobS3Store`, `KeyMapper`, `MetadataCache`; unit tests.
4. **Filesystem + locking + mount manager** — `S3DriveFileSystem`, `ObjectLocks`,
   `MountManager`; end-to-end mount of one bucket.
5. **SMB re-sharing** — `ISmbShareManager` + Windows implementation, share lifecycle tied to
   mount/unmount, ACLs from `AllowedPrincipals`/`Access`, elevation handling, orphan
   reconciliation.
6. **Agent** — single-instance, tray (Mount/Unmount + Share/Unshare), About, host,
   `status.json`, command watcher, auto-mount, multi-drive.
7. **TUI** — shell, catalog, Drives table (mount + share state), `DriveFormModal` (incl.
   Network sharing group), activity log, agent launch, command/status wiring.
8. **Housekeeping + acceptance** — README/CHANGELOG/LICENSE/.gitignore/sample config,
   CLAUDE.md, warnings-clean build, manual acceptance pass.
