# S3Drive Telemetry

S3Drive emits **metrics**, **traces**, and (opt-in) **logs** so an operator can tell, from Grafana
alone, *where* the time went and *what* failed — a slow Explorer copy resolves to the stage and the
S3 call that caused it; a failed mount resolves to the exception that broke it.

- **Emit** happens in `S3Drive.Core` through the .NET base class library only
  (`System.Diagnostics.Metrics.Meter` and `System.Diagnostics.ActivitySource`). Core takes no
  telemetry SDK dependency, and with nothing subscribed every call is a near-free no-op.
- **Export** happens once, in the always-on agent (`S3Drive.Agent`), through a single
  [Radiant](https://www.nuget.org/packages/Radiant) host (`RadiantHost`, package `Radiant` 0.1.2)
  that subscribes to the S3Drive meter and activity source and ships OTLP to a collector, serves an
  optional in-process Prometheus endpoint, and forwards logs.
- Instrumentation is **best-effort**: a telemetry failure never changes a filesystem result, a mount,
  or a command. If the telemetry host cannot start, the agent logs a warning and runs without it.

Contents: [Quick start](#quick-start) · [Configuration](#configuration) ·
[Sources](#meter-and-activity-source) · [Metrics catalog](#metrics-catalog) ·
[Spans catalog](#spans-catalog) · [Logs](#logs) · [Context propagation](#context-propagation) ·
[Privacy and cardinality](#privacy-and-cardinality) · [Subscribing from another host](#subscribing-from-another-host) ·
[Observability stack](#observability-stack) · [Dashboards](#dashboards) · [Alerts](#recommended-alerts) ·
[Troubleshooting](#troubleshooting) · [Tests](#tests)

## Quick start

1. Start the observability stack (Prometheus, Tempo, Loki, Grafana, and an OpenTelemetry Collector):

   ```
   docker compose -f docker/compose.yaml up -d
   ```

2. Turn on OTLP export in `~/.s3drive/s3drive.json` (or `%USERPROFILE%\.s3drive\s3drive.json`):

   ```json
   "Telemetry": {
     "OtlpEnabled": true,
     "ExportLogs": true
   }
   ```

   or set `S3DRIVE_OTLP_ENABLED=true` (and `S3DRIVE_TELEMETRY_EXPORT_LOGS=true`) in the agent's
   environment.

3. Restart the agent (tray → Exit, then start it again or open the TUI).

4. Open Grafana at `http://localhost:3000` (`admin` / `admin` for local development) → folder
   **S3Drive** → **S3Drive / Overview**.

## Configuration

Telemetry settings live in the `Telemetry` section of `s3drive.json` and are read when the agent
starts (restart the agent to apply changes). Every exporter is **off by default**: a desktop install
usually has no collector, and the agent should not make background connections nobody asked for.
Metrics and spans are still emitted and can be picked up by any in-process listener.

| JSON key (`Telemetry.*`) | Environment override | Default | Range / values | Purpose |
|---|---|---|---|---|
| `Enabled` | `S3DRIVE_TELEMETRY_ENABLED` | `true` | bool | Master switch. `false` builds no pipeline and binds no ports. |
| `ServiceName` | `S3DRIVE_TELEMETRY_SERVICE_NAME` | `s3drive-agent` | non-empty | `service.name`; becomes the Prometheus `job` label via the collector. |
| `OtlpEnabled` | `S3DRIVE_OTLP_ENABLED` | `false` | bool | Push metrics, traces (and logs when `ExportLogs`) over OTLP. |
| `OtlpEndpoint` | `S3DRIVE_OTLP_ENDPOINT` | `http://127.0.0.1:4317` | absolute URI | Collector endpoint (gRPC 4317, or HTTP 4318 with `httpprotobuf`). |
| `OtlpProtocol` | `S3DRIVE_OTLP_PROTOCOL` | `grpc` | `grpc`, `httpprotobuf` | OTLP wire protocol; anything else is treated as `grpc`. |
| `PrometheusEnabled` | `S3DRIVE_PROMETHEUS_ENABLED` | `false` | bool | Serve `/metrics` in-process (no collector needed). |
| `PrometheusHostname` | `S3DRIVE_PROMETHEUS_HOSTNAME` | `127.0.0.1` | hostname | Bind address for `/metrics`. See [Troubleshooting](#troubleshooting) for Windows. |
| `PrometheusPort` | `S3DRIVE_PROMETHEUS_PORT` | `9464` | 1–65535 | Port for `/metrics`. |
| `ExportLogs` | `S3DRIVE_TELEMETRY_EXPORT_LOGS` | `false` | bool | Export the agent log stream (Information and above) with trace correlation. Opt-in because lines contain file paths. |
| `LokiEnabled` | `S3DRIVE_LOKI_ENABLED` | `false` | bool | Also push logs straight to Loki (requires `ExportLogs`). Not needed when the collector forwards logs. |
| `LokiEndpoint` | `S3DRIVE_LOKI_ENDPOINT` | `http://127.0.0.1:3100/otlp` | absolute URI | Loki 3.x OTLP base endpoint. |
| `SamplingRatio` | `S3DRIVE_TELEMETRY_SAMPLING_RATIO` | `1.0` | 0.0–1.0 | Head-based, parent-based trace sampling. Metrics are never sampled. |
| `MetricsExportIntervalMs` | — | `15000` | 1000–300000 | OTLP metric push interval. |
| `IncludeObjectKeys` | `S3DRIVE_TELEMETRY_INCLUDE_OBJECT_KEYS` | `false` | bool | Add object keys (file/folder names) to spans. Never affects metrics. |

Invalid environment values (for example `S3DRIVE_PROMETHEUS_PORT=lots`) are ignored. Out-of-range
values are clamped.

## Meter and activity source

| Kind | Name | Notes |
|---|---|---|
| Meter | `S3Drive` | Every `s3drive.*` instrument. Version = the S3Drive version. |
| ActivitySource | `S3Drive` | Every S3Drive span. |
| Meter (BCL) | `System.Net.Http` | Subscribed by the agent: raw HTTP calls the AWS SDK makes to each S3 endpoint (`http.client.request.duration`, open connections, queue time). |
| Runtime / process | Radiant | .NET runtime (GC, heap, JIT, thread pool, exceptions) and process (memory, threads, uptime) metrics, enabled by the agent. |

All names are constants in `src/S3Drive.Core/Telemetry/TelemetryNames.cs`; the full instrument list
with units, labels, and histogram buckets is `S3DriveMetricCatalog.All`. The agent registers that
catalog with Radiant (`settings.Metrics.DefineAll(...)`) so each histogram gets its declared buckets.

## Metrics catalog

Instrument names are dotted OpenTelemetry names with UCUM units. A Prometheus exporter (the collector,
or the in-process endpoint) rewrites them to snake case: dots become underscores, `_total` is added to
counters, and the unit is appended (`s` → `_seconds`, `By` → `_bytes`; annotation units such as
`{operation}` add nothing). Label keys are rewritten the same way (`s3drive.drive` → `s3drive_drive`,
`error.type` → `error_type`). Through the collector every series also carries `job` (= service name),
`instance` / `service_instance_id`, and `service_name`.

Quantiles (p50/p95/p99) are computed in Grafana from histogram buckets; nothing is precomputed
in-process.

| Instrument | Prometheus series | Type | Unit | Labels | Description |
|---|---|---|---|---|---|
| `s3drive.fs.operation.duration` | `s3drive_fs_operation_duration_seconds_bucket/_sum/_count` | histogram | `s` | `s3drive.drive`, `s3drive.fs.operation`, `s3drive.outcome`, `error.type` | Duration of filesystem (Dokan) operations handled for the operating system. |
| `s3drive.fs.operations` | `s3drive_fs_operations_total` | counter | `{operation}` | `s3drive.drive`, `s3drive.fs.operation`, `s3drive.outcome`, `error.type` | Filesystem (Dokan) operations handled for the operating system. |
| `s3drive.fs.bytes` | `s3drive_fs_bytes_total` | counter | `By` | `s3drive.drive`, `s3drive.direction` | Bytes served to (read) or accepted from (write) the operating system. |
| `s3drive.fs.open_handles` | `s3drive_fs_open_handles` | updowncounter | `{handle}` | `s3drive.drive` | Open file and directory handles currently held by the operating system. |
| `s3drive.fs.stage.duration` | `s3drive_fs_stage_duration_seconds_bucket/_sum/_count` | histogram | `s` | `s3drive.drive`, `s3drive.stage`, `s3drive.outcome` | Duration of filesystem workflow stages (download, upload, copy, delete, list_all, mkdir). |
| `s3drive.s3.request.duration` | `s3drive_s3_request_duration_seconds_bucket/_sum/_count` | histogram | `s` | `s3drive.drive`, `s3drive.provider`, `s3drive.s3.operation`, `s3drive.outcome`, `error.type` | Duration of outbound S3 requests. |
| `s3drive.s3.requests` | `s3drive_s3_requests_total` | counter | `{request}` | `s3drive.drive`, `s3drive.provider`, `s3drive.s3.operation`, `s3drive.outcome`, `error.type` | Outbound S3 requests. |
| `s3drive.s3.bytes` | `s3drive_s3_bytes_total` | counter | `By` | `s3drive.drive`, `s3drive.direction` | Bytes transferred to (upload) or from (download) the S3 endpoint. |
| `s3drive.s3.suppressed_errors` | `s3drive_s3_suppressed_errors_total` | counter | `{error}` | `s3drive.drive`, `s3drive.s3.operation`, `error.type` | S3 failures absorbed by the storage layer (for example a HEAD failure reported as an absent object). |
| `s3drive.s3.batch_delete.fallbacks` | `s3drive_s3_batch_delete_fallbacks_total` | counter | `{batch}` | `s3drive.drive`, `s3drive.reason` | Multi-object delete batches that fell back to per-key deletes. |
| `s3drive.cache.lookups` | `s3drive_cache_lookups_total` | counter | `{lookup}` | `s3drive.drive`, `s3drive.cache.kind`, `s3drive.cache.result` | Metadata cache lookups by kind and result (hit, miss, bypass when caching is disabled). |
| `s3drive.cache.entries` | `s3drive_cache_entries` | updowncounter | `{entry}` | `s3drive.drive`, `s3drive.cache.kind` | Metadata cache entries currently held. |
| `s3drive.cache.invalidations` | `s3drive_cache_invalidations_total` | counter | `{invalidation}` | `s3drive.drive`, `s3drive.cache.scope` | Metadata cache invalidations by scope. |
| `s3drive.lock.wait.duration` | `s3drive_lock_wait_duration_seconds_bucket/_sum/_count` | histogram | `s` | `s3drive.drive`, `s3drive.outcome` | Time spent waiting for a per-object lock. |
| `s3drive.lock.held` | `s3drive_lock_held` | updowncounter | `{lock}` | `s3drive.drive` | Per-object locks currently held. |
| `s3drive.lock.contentions` | `s3drive_lock_contentions_total` | counter | `{acquisition}` | `s3drive.drive` | Lock acquisitions that found the object already locked. |
| `s3drive.mount.operations` | `s3drive_mount_operations_total` | counter | `{operation}` | `s3drive.mount.operation`, `s3drive.outcome`, `error.type` | Mount and unmount operations. |
| `s3drive.mount.duration` | `s3drive_mount_duration_seconds_bucket/_sum/_count` | histogram | `s` | `s3drive.mount.operation`, `s3drive.outcome`, `error.type` | Duration of mount and unmount operations. |
| `s3drive.mount.stage.duration` | `s3drive_mount_stage_duration_seconds_bucket/_sum/_count` | histogram | `s` | `s3drive.stage`, `s3drive.outcome` | Duration of mount and unmount stages. |
| `s3drive.drives.mounted` | `s3drive_drives_mounted` | gauge | `{drive}` | — | Drives currently mounted. |
| `s3drive.drive.state` | `s3drive_drive_state` | gauge | `{drive}` | `s3drive.drive`, `s3drive.state` | Per-drive mount state; 1 for the drive's current state. |
| `s3drive.command.jobs` | `s3drive_command_jobs_total` | counter | `{command}` | `s3drive.command.type`, `s3drive.outcome` | Agent commands processed, by type and outcome. |
| `s3drive.command.stage.duration` | `s3drive_command_stage_duration_seconds_bucket/_sum/_count` | histogram | `s` | `s3drive.command.type`, `s3drive.stage`, `s3drive.outcome` | Duration of agent command pipeline stages (queued, parse, execute). |
| `s3drive.command.stage.events` | `s3drive_command_stage_events_total` | counter | `{event}` | `s3drive.command.type`, `s3drive.stage`, `s3drive.outcome` | Agent command pipeline stage completions, by stage and outcome. |
| `s3drive.command.last_success` | `s3drive_command_last_success_seconds` | gauge | `s` | — | Unix time of the last successfully executed command; 0 before the first. |
| `s3drive.command.queue.depth` | `s3drive_command_queue_depth` | gauge | `{command}` | — | Command files pending at the most recent poll. |
| `s3drive.agent.last_poll` | `s3drive_agent_last_poll_seconds` | gauge | `s` | — | Unix time of the agent's most recent command poll (liveness heartbeat); 0 before the first. |
| `s3drive.ipc.duration` | `s3drive_ipc_duration_seconds_bucket/_sum/_count` | histogram | `s` | `s3drive.ipc.operation`, `s3drive.outcome`, `error.type` | Duration of file-based IPC and configuration I/O. |
| `s3drive.ipc.operations` | `s3drive_ipc_operations_total` | counter | `{operation}` | `s3drive.ipc.operation`, `s3drive.outcome`, `error.type` | File-based IPC and configuration I/O operations. |
| `s3drive.build.info` | `s3drive_build_info` | gauge | `{info}` | `s3drive.version`, `s3drive.runtime`, `os.type` | Build information; always 1. |
| `s3drive.config.metadata_cache_seconds` | `s3drive_config_metadata_cache_seconds` | gauge | `s` | — | Configured metadata cache lifetime. |
| `s3drive.config.drives` | `s3drive_config_drives` | gauge | `{drive}` | `s3drive.automount` | Configured drives, by whether they auto-mount. |
| `s3drive.crashes` | `s3drive_crashes_total` | counter | `{crash}` | `error.type` | Crash reports written. |
| `s3drive.log.messages` | `s3drive_log_messages_total` | counter | `{message}` | `s3drive.severity` | Log messages written, by severity. |

### Label values

| Label | Values |
|---|---|
| `s3drive.drive` | Drive letter `A`–`Z`, or `unknown`. |
| `s3drive.outcome` | `success`, `not_found`, `conflict`, `rejected` (invalid/denied/not empty/unsupported), `cancelled` (aborted by unmount or token), `error`, `skipped` (mount of an already-mounted drive). |
| `error.type` | On failures only: the S3 error code for `AmazonS3Exception` (for example `AccessDenied`, `NoSuchBucket`), otherwise the exception type name (`IOException`, `S3DriveCryptoException`), the NTSTATUS name for filesystem results (`ObjectNameNotFound`, `DirectoryNotEmpty`), or `ConnectivityFailed`. Truncated to 64 characters. |
| `s3drive.fs.operation` | `create_file`, `cleanup`, `read_file`, `write_file`, `get_file_information`, `find_files`, `delete_file`, `delete_directory`, `move_file`, `set_length`. |
| `s3drive.stage` | Filesystem: `download`, `upload`, `copy`, `delete`, `list_all`, `mkdir`. Mount: `decrypt_credentials`, `prepare_staging`, `build_store`, `dokan_mount`, `dokan_unmount`. Commands: `queued`, `parse`, `execute`. Spans also use `lock_wait`. |
| `s3drive.s3.operation` | `ObjectExists`, `HeadObject`, `ListObjects`, `ListAllKeys`, `GetObject`, `PutObject`, `DeleteObject`, `DeleteObjects`, `CopyObject`, `ValidateConnectivity`. |
| `s3drive.provider` | `aws_s3`, `s3_compatible`. |
| `s3drive.direction` | Filesystem: `read`, `write`. S3: `download`, `upload`. |
| `s3drive.cache.kind` / `.result` / `.scope` | `head`, `listing` / `hit`, `miss`, `bypass` / `key`, `prefix`, `clear`. |
| `s3drive.mount.operation` | `mount`, `unmount`. |
| `s3drive.state` | `Unmounted`, `Mounting`, `Mounted`, `Unmounting`, `Failed`. |
| `s3drive.command.type` | `Reload`, `Mount`, `Unmount`, `MountAll`, `UnmountAll`, or `unknown` (unreadable command file). |
| `s3drive.ipc.operation` | `command_send`, `status_write`, `status_read`, `config_load`, `config_save`. |
| `s3drive.reason` | `partial`, `unsupported`. |
| `s3drive.severity` | `Debug`, `Info`, `Warn`, `Error`, `Alert`, `Critical`, `Emergency`. |

## Spans catalog

| Span name | Kind | Emitted by | When | Key attributes |
|---|---|---|---|---|
| `fs <operation>` (for example `fs create_file`, `fs cleanup`) | Server | `S3DriveFileSystem` | Each OS request that can reach S3: `create_file`, `cleanup` (when it commits a write or delete), `get_file_information` (non-root), `find_files`, `delete_file`, `delete_directory`, `move_file`, and the **first** `read_file` / `write_file` / `set_length` on a handle (the one that stages the object). Later chunk reads/writes are metrics only. | `s3drive.drive`, `s3drive.fs.operation`, `s3drive.fs.ntstatus`, `s3drive.outcome`, `error.type`, `s3drive.fs.mode`, `s3drive.fs.is_directory`, `s3drive.count`, `s3drive.object.key`* |
| `stage:download` / `stage:upload` / `stage:copy` / `stage:delete` / `stage:list_all` / `stage:mkdir` | Internal | `S3DriveFileSystem` | Each workflow stage inside an fs span. | `s3drive.drive`, `s3drive.stage`, `s3drive.outcome` |
| `stage:lock_wait` | Internal | `ObjectLocks` | Each per-object lock acquisition. | `s3drive.drive`, `s3drive.lock.contended` |
| `S3 <Operation>` (for example `S3 PutObject`) | Client | `InstrumentedS3Store` | Every outbound storage call. | `rpc.system=aws-api`, `rpc.service=S3`, `rpc.method`, `aws.s3.bucket`, `s3drive.drive`, `s3drive.provider`, `s3drive.bytes`, `s3drive.count`, `s3drive.outcome`, `error.type`, `s3drive.object.key`* |
| `drive mount` / `drive unmount` | Internal | `MountManager` | Each mount/unmount. | `s3drive.drive`, `s3drive.drive.id`, `s3drive.mount.operation`, `s3drive.outcome`, `error.type` |
| `stage:decrypt_credentials` / `stage:prepare_staging` / `stage:build_store` / `stage:dokan_mount` / `stage:dokan_unmount` | Internal | `MountManager` | Mount/unmount stages. | `s3drive.stage`, `s3drive.outcome` |
| `ipc command_send` | Producer | `CommandChannel.SendAsync` | The TUI (or any sender) writes a command. | `s3drive.ipc.operation`, `s3drive.command.type` |
| `command <Type>` (for example `command Mount`) | Consumer | `CommandDispatcher` | The agent processes one command file. Starts when the command was written, so it includes queue time. | `s3drive.command.type`, `s3drive.drive.id`, `s3drive.outcome` |
| `stage:queued` / `stage:parse` / `stage:execute` | Internal | `CommandDispatcher` | Children of `command <Type>`; `queued` and `parse` carry their real start/end times. | `s3drive.command.type`, `s3drive.stage`, `s3drive.outcome` |
| `ipc status_write` / `ipc status_read` / `ipc config_load` / `ipc config_save` | Internal | `StatusStore`, `SettingsManager` | Each status or configuration read/write. | `s3drive.ipc.operation`, `s3drive.count` |

\* Only when `Telemetry.IncludeObjectKeys` is `true`.

Every span sets its status explicitly: `Ok` for success and expected outcomes (`not_found`,
`conflict`, `rejected`), `Error` (with an `exception` event carrying type, message, and stack) for
failures, and leaves `Unset` for `cancelled`. A failure the storage layer absorbs (a HEAD that failed
for a reason other than not-found and was reported to Windows as "absent") marks the active S3 span
`Error` with description `suppressed: <error.type>` and increments `s3drive.s3.suppressed_errors`.

## Logs

With `ExportLogs` on, every line the agent writes through `S3DriveLog` at Information or above is
also exported over OTLP (and to Loki directly when `LokiEnabled`). Lines written inside a span carry
`trace_id` and `span_id`, so Grafana links a log line to its trace and a trace to its logs. The local
log files under `~/.s3drive/logs` are unchanged and still include Debug lines. Exported lines contain
file and folder paths, which is why export is opt-in.

`s3drive.log.messages` (by severity) and `s3drive.crashes` (by `error.type`) are always emitted as
metrics, whether or not log export is on.

## Context propagation

W3C trace context crosses every boundary S3Drive owns:

- **TUI → agent.** `CommandChannel.SendAsync` stamps the sender's `traceparent` (and the send time)
  into the command file; `CommandDispatcher` starts `command <Type>` as a child of that context, so
  the click and the agent's work are one trace. The TUI itself does not host an exporter; a sender
  that does (or a test) gets an end-to-end trace.
- **In-process hand-offs.** Spans flow through `async`/`await` and `Task.Run` via
  `Activity.Current`; S3 client spans nest under stage spans, which nest under the fs span.
- **Outbound HTTP.** The AWS SDK's HTTP calls are measured by the `System.Net.Http` meter.
- **Dokan callbacks** arrive from the kernel with no parent, so each OS request is a root span.

## Privacy and cardinality

- **Metric labels are bounded**: drive letters (≤ 26), enumerated operations, stages, outcomes,
  providers, and exception/error-code types. Drive ids, bucket names, object keys, endpoint URLs, and
  free-form text never appear on metrics. A test enforces that every emitted label is declared in the
  catalog and that object keys never leak into metrics or (by default) spans.
- **No secrets anywhere**: access keys, secret keys, and decrypted credentials are never recorded.
  Bucket names and drive ids appear on spans only.
- **Object keys** are user content; they reach spans only with `IncludeObjectKeys` and reach exported
  logs only with `ExportLogs`.

## Subscribing from another host

`S3Drive.Core` is a plain BCL emitter, so any host can listen by name without referencing Radiant:

```csharp
RadiantSettings settings = new RadiantSettings("my-host");
settings.Sources.AddMeter(TelemetryNames.MeterName);            // "S3Drive"
settings.Sources.AddActivitySource(TelemetryNames.ActivitySourceName);
using (RadiantHost host = RadiantHost.Start(settings)) { /* ... */ }
```

With the OpenTelemetry SDK directly: `.AddMeter("S3Drive")` on a `MeterProviderBuilder` and
`.AddSource("S3Drive")` on a `TracerProviderBuilder`. With no SDK at all, a `MeterListener` and an
`ActivityListener` filtered to `"S3Drive"` see everything (that is how the tests assert on telemetry).
Wrap any `IS3Store` in `InstrumentedS3Store` to get the `S3 <Operation>` spans and `s3drive.s3.*`
metrics for it.

## Observability stack

`docker/compose.yaml` brings up the operator side; the agent stays on the host because it owns the
Dokan mounts.

| Service | Image (pinned) | Host port (override) | Role |
|---|---|---|---|
| `otel-collector` | `otel/opentelemetry-collector-contrib:0.109.0` | `4317` gRPC (`S3DRIVE_OTLP_GRPC_PORT`), `4318` HTTP (`S3DRIVE_OTLP_HTTP_PORT`) | Receives the agent's OTLP; metrics → Prometheus exporter `:8889`, traces → Tempo, logs → Loki. |
| `prometheus` | `prom/prometheus:v3.5.4` | `9090` (`S3DRIVE_PROMETHEUS_PORT`) | Scrapes the collector. |
| `tempo` | `grafana/tempo:2.6.1` | `3200` (`S3DRIVE_TEMPO_PORT`) | Trace storage and query. |
| `loki` | `grafana/loki:3.2.1` | `3100` (`S3DRIVE_LOKI_PORT`) | Log storage (OTLP ingest). |
| `grafana` | `grafana/grafana-oss:13.0.2` | `3000` (`S3DRIVE_GRAFANA_PORT`) | Dashboards; datasources `prometheus`, `tempo`, `loki` (stable UIDs). |

- Every host port binds `127.0.0.1` only. Prometheus, Tempo, Loki, and the collector have no
  authentication; do not publish them on a public interface.
- Tempo, Loki, Prometheus, and Grafana have healthchecks (`interval: 5s`, `retries: 2`) and start in
  dependency order (`depends_on: condition: service_healthy`). The collector image is distroless (no
  shell, curl, or wget), so it cannot carry a Docker healthcheck; Prometheus waits for it with
  `service_started`, and its `health_check` extension answers on `:13133` inside the network.
- Grafana credentials default to `admin` / `admin` **for local development only**. Anywhere else set
  `GF_SECURITY_ADMIN_USER` and `GF_SECURITY_ADMIN_PASSWORD` out of band (environment or an
  uncommitted `.env`). Sign-up is disabled.
- `docker/update.bat` / `docker/update.sh` pull the pinned images and recreate the stack without
  deleting volumes.

| Tool | URL | Credentials |
|---|---|---|
| Grafana | `http://localhost:3000` | `admin` / `admin` (local only) |
| Prometheus | `http://localhost:9090` | none |
| Tempo API | `http://localhost:3200` | none |
| Loki API | `http://localhost:3100` | none |
| OTLP ingest | `http://localhost:4317` (gRPC), `http://localhost:4318` (HTTP) | none |

S3Drive has no product web dashboard (it is a tray agent plus a terminal UI), so there is no
External Services card; this table is the equivalent reference.

## Dashboards

Dashboard JSON lives in `assets/grafana/` and is provisioned into the Grafana folder **S3Drive**.
Every dashboard links to the others (top bar) and has an **Agent** (`job`) selector; per-drive
dashboards add a **Drive** selector.

| Dashboard (uid) | Answers | Key panels |
|---|---|---|
| **S3Drive / Overview** (`s3drive-overview`) | Is the agent alive and healthy? Start here. | Heartbeat age, drives mounted, fs ops/s, fs and S3 error ratios, S3 p95, crashes, errors logged, pending commands, version; fs and S3 traffic by outcome; fs p95 by operation; drive state; recent failed traces (Tempo); recent warnings/errors (Loki). |
| **S3Drive / Filesystem** (`s3drive-filesystem`) | Which OS operation is slow or failing, and in which stage? | Ops/s and failures by operation and `error_type`; p95 by operation; p50/p95/p99; **p95 by stage** (download/upload/copy/delete/list_all/mkdir); stage failures; bytes read/written; open handles; outcome mix. |
| **S3Drive / S3 & Integrations** (`s3drive-s3`) | Is the S3 endpoint the cause? | Requests by operation; errors by operation and S3 error code; p95 by operation; p99 by drive; throughput; suppressed errors; batch-delete fallbacks; HTTP client p95, status codes, connections, and queue time per endpoint. |
| **S3Drive / Cache & Locks** (`s3drive-cache-locks`) | Is caching working; are operations queuing on a file? | Hit ratio by kind; lookups by result; entries; invalidations; lock wait p50/p95/p99; locks held; contention; lock waits by outcome. |
| **S3Drive / Agent** (`s3drive-agent`) | Mounts, TUI commands, IPC, and the process. | Heartbeat, last command success, pending commands, configured drives, cache TTL; mount/unmount by outcome and `error_type`; mount p95 by stage; drive state; command jobs; command stage p95 (queued/parse/execute); IPC p95 and failures; log rate; crashes; memory, GC, exceptions, thread pool; agent logs (Loki). |

Investigation path: **Overview** (something regressed) → the domain dashboard (which operation /
stage / S3 call) → **Tempo** trace (the exact request) → **Loki** lines for that trace.

## Recommended alerts

```yaml
groups:
  - name: s3drive
    rules:
      - alert: S3DriveAgentStalled
        # No command poll for 2 minutes: the agent is hung, crashed, or stopped exporting.
        expr: time() - max by (job, instance) (s3drive_agent_last_poll_seconds) > 120
        for: 2m
        labels: { severity: critical }

      - alert: S3DriveFilesystemErrors
        expr: |
          sum by (job, s3drive_drive) (rate(s3drive_fs_operations_total{s3drive_outcome="error"}[5m]))
            / clamp_min(sum by (job, s3drive_drive) (rate(s3drive_fs_operations_total[5m])), 1e-9) > 0.05
        for: 10m
        labels: { severity: warning }

      - alert: S3DriveWritesNotCommitted
        # A file closed by an application failed to upload: data was NOT written to S3.
        expr: sum by (job, s3drive_drive) (increase(s3drive_fs_stage_duration_seconds_count{s3drive_stage="upload",s3drive_outcome!="success"}[10m])) > 0
        labels: { severity: critical }

      - alert: S3DriveS3Errors
        expr: |
          sum by (job, s3drive_drive, error_type) (rate(s3drive_s3_requests_total{s3drive_outcome="error"}[5m])) > 0.1
        for: 10m
        labels: { severity: warning }

      - alert: S3DriveS3SlowP95
        expr: |
          histogram_quantile(0.95, sum by (le, job, s3drive_drive) (rate(s3drive_s3_request_duration_seconds_bucket[5m]))) > 2
        for: 15m
        labels: { severity: warning }

      - alert: S3DriveSuppressedS3Errors
        # Files may appear missing in Explorer because HEAD requests are failing (auth, network).
        expr: sum by (job, s3drive_drive, error_type) (increase(s3drive_s3_suppressed_errors_total[10m])) > 0
        labels: { severity: warning }

      - alert: S3DriveMountFailures
        expr: sum by (job, error_type) (increase(s3drive_mount_operations_total{s3drive_mount_operation="mount",s3drive_outcome="error"}[15m])) > 0
        labels: { severity: warning }

      - alert: S3DriveCommandFailures
        expr: sum by (job, s3drive_command_type) (increase(s3drive_command_jobs_total{s3drive_outcome!="success"}[15m])) > 0
        labels: { severity: warning }

      - alert: S3DriveCommandBacklog
        expr: max by (job) (s3drive_command_queue_depth) > 5
        for: 5m
        labels: { severity: warning }

      - alert: S3DriveLockContention
        expr: histogram_quantile(0.99, sum by (le, job, s3drive_drive) (rate(s3drive_lock_wait_duration_seconds_bucket[5m]))) > 5
        for: 10m
        labels: { severity: warning }

      - alert: S3DriveCrashed
        expr: sum by (job, error_type) (increase(s3drive_crashes_total[30m])) > 0
        labels: { severity: critical }
```

## Troubleshooting

- **Nothing in Grafana.** Check the agent log (`~/.s3drive/logs`) for `Telemetry enabled (...)`. If it
  says `no exporter enabled`, set `OtlpEnabled`. If the collector port is moved
  (`S3DRIVE_OTLP_GRPC_PORT`), point `OtlpEndpoint` at it. Prometheus → *Status → Targets* should show
  `s3drive-otel-collector` up.
- **Scraping the agent directly (no collector).** Set `PrometheusEnabled` and uncomment the
  `s3drive-agent` job in `docker/prometheus.yaml`. On Windows the endpoint is served by HTTP.sys,
  which matches the request's `Host` header against the bound hostname. Binding `localhost` works
  for a standard user; other hostnames (including `127.0.0.1`, or `+` to accept requests addressed
  to `host.docker.internal`) typically need a URL reservation from an elevated prompt, for example
  `netsh http add urlacl url=http://+:9464/ user=%USERNAME%`. If binding fails, the agent logs a
  warning and continues with OTLP only. The collector path avoids all of this and is the
  recommended setup. (The endpoint is a read-only telemetry listener, not a control surface; the
  TUI and agent still coordinate only through files.)
- **Too many traces.** Lower `SamplingRatio` (for example `0.1`). Metrics are unaffected.
- **Debugging a specific file.** Temporarily set `IncludeObjectKeys` to `true` to see keys on spans,
  then turn it back off.

## Tests

`test/Test.Shared/Suites/TelemetrySuite.cs` (run by `Test.Automated`, `Test.Xunit`, and `Test.Nunit`)
uses an in-memory `MeterListener` / `ActivityListener` (`Helpers/TelemetryCapture.cs`) to prove:
the catalog matches the published instruments; every inventory category emits metrics and spans
(filesystem operations and stages, S3 integration, cache, locks, mount lifecycle, the command
pipeline, IPC and configuration I/O, logging, build and config gauges); failure, not-found, and
cancellation paths carry the right outcome and `error.type`; S3 spans nest under stage and fs spans;
a command joins its sender's trace across the command file; labels stay inside the catalog and object
keys never leak by default; every path runs without a listener; and the settings defaults, clamps,
environment overrides, and JSON round-trip. A live-endpoint case (enabled with `--endpoint` or
`S3DRIVE_TEST_*`, or `test/run-integration.sh`) confirms a real missing-key HEAD is classified as
`not_found` rather than a suppressed error.
