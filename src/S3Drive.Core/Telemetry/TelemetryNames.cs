namespace S3Drive.Core.Telemetry
{
    /// <summary>
    /// Every telemetry name S3Drive emits: the meter and activity-source names, metric instrument
    /// names, attribute (label) keys, span names, and the bounded label values. These strings are
    /// a public contract consumed by Grafana dashboards and alerts; treat a rename as a breaking
    /// change. Instrument names are dotted OpenTelemetry names; a Prometheus exporter rewrites them
    /// to snake case with unit suffixes (for example <c>s3drive.s3.request.duration</c> becomes
    /// <c>s3drive_s3_request_duration_seconds</c>).
    /// </summary>
    public static class TelemetryNames
    {
        /// <summary>
        /// The <see cref="System.Diagnostics.Metrics.Meter"/> name every S3Drive metric is emitted on.
        /// </summary>
        public const string MeterName = "S3Drive";

        /// <summary>
        /// The <see cref="System.Diagnostics.ActivitySource"/> name every S3Drive span is emitted on.
        /// </summary>
        public const string ActivitySourceName = "S3Drive";

        /// <summary>
        /// The .NET HTTP client meter (raw HTTP calls the AWS SDK makes to the S3 endpoint).
        /// </summary>
        public const string HttpClientMeterName = "System.Net.Http";

        /// <summary>
        /// Filesystem (Dokan) operation duration histogram, in seconds.
        /// </summary>
        public const string FsOperationDuration = "s3drive.fs.operation.duration";

        /// <summary>
        /// Filesystem (Dokan) operation counter.
        /// </summary>
        public const string FsOperations = "s3drive.fs.operations";

        /// <summary>
        /// Bytes served to (read) or accepted from (write) the operating system, in bytes.
        /// </summary>
        public const string FsBytes = "s3drive.fs.bytes";

        /// <summary>
        /// Open file and directory handles currently held by the operating system.
        /// </summary>
        public const string FsOpenHandles = "s3drive.fs.open_handles";

        /// <summary>
        /// Filesystem workflow sub-stage duration histogram (download, upload, copy, delete, list_all), in seconds.
        /// </summary>
        public const string FsStageDuration = "s3drive.fs.stage.duration";

        /// <summary>
        /// Outbound S3 request duration histogram, in seconds.
        /// </summary>
        public const string S3RequestDuration = "s3drive.s3.request.duration";

        /// <summary>
        /// Outbound S3 request counter.
        /// </summary>
        public const string S3Requests = "s3drive.s3.requests";

        /// <summary>
        /// Bytes transferred to (upload) or from (download) the S3 endpoint, in bytes.
        /// </summary>
        public const string S3Bytes = "s3drive.s3.bytes";

        /// <summary>
        /// S3 failures that the storage layer absorbed (for example a HEAD failure reported to the
        /// filesystem as "object absent"), by operation and error type.
        /// </summary>
        public const string S3SuppressedErrors = "s3drive.s3.suppressed_errors";

        /// <summary>
        /// Multi-object delete batches that fell back to per-key deletes.
        /// </summary>
        public const string S3BatchDeleteFallbacks = "s3drive.s3.batch_delete.fallbacks";

        /// <summary>
        /// Metadata cache lookups by kind and result.
        /// </summary>
        public const string CacheLookups = "s3drive.cache.lookups";

        /// <summary>
        /// Metadata cache entries currently held, by kind.
        /// </summary>
        public const string CacheEntries = "s3drive.cache.entries";

        /// <summary>
        /// Metadata cache invalidations by scope.
        /// </summary>
        public const string CacheInvalidations = "s3drive.cache.invalidations";

        /// <summary>
        /// Time spent waiting for a per-object lock, in seconds.
        /// </summary>
        public const string LockWaitDuration = "s3drive.lock.wait.duration";

        /// <summary>
        /// Per-object locks currently held.
        /// </summary>
        public const string LockHeld = "s3drive.lock.held";

        /// <summary>
        /// Lock acquisitions that found the object already locked by another operation.
        /// </summary>
        public const string LockContentions = "s3drive.lock.contentions";

        /// <summary>
        /// Mount and unmount operation counter.
        /// </summary>
        public const string MountOperations = "s3drive.mount.operations";

        /// <summary>
        /// Mount and unmount operation duration histogram, in seconds.
        /// </summary>
        public const string MountDuration = "s3drive.mount.duration";

        /// <summary>
        /// Mount sub-stage duration histogram, in seconds.
        /// </summary>
        public const string MountStageDuration = "s3drive.mount.stage.duration";

        /// <summary>
        /// Number of drives currently mounted.
        /// </summary>
        public const string DrivesMounted = "s3drive.drives.mounted";

        /// <summary>
        /// Per-drive mount state (1 for the drive's current state).
        /// </summary>
        public const string DriveState = "s3drive.drive.state";

        /// <summary>
        /// Agent command jobs processed, by command type and outcome.
        /// </summary>
        public const string CommandJobs = "s3drive.command.jobs";

        /// <summary>
        /// Agent command pipeline stage duration histogram (queued, parse, execute), in seconds.
        /// </summary>
        public const string CommandStageDuration = "s3drive.command.stage.duration";

        /// <summary>
        /// Agent command pipeline stage events, by stage and outcome.
        /// </summary>
        public const string CommandStageEvents = "s3drive.command.stage.events";

        /// <summary>
        /// Unix time, in seconds, of the last successfully executed command.
        /// </summary>
        public const string CommandLastSuccess = "s3drive.command.last_success";

        /// <summary>
        /// Command files found pending at the most recent poll.
        /// </summary>
        public const string CommandQueueDepth = "s3drive.command.queue.depth";

        /// <summary>
        /// Unix time, in seconds, of the agent's most recent command poll (a liveness heartbeat).
        /// </summary>
        public const string AgentLastPoll = "s3drive.agent.last_poll";

        /// <summary>
        /// File-based IPC and configuration I/O duration histogram, in seconds.
        /// </summary>
        public const string IpcDuration = "s3drive.ipc.duration";

        /// <summary>
        /// File-based IPC and configuration I/O operation counter.
        /// </summary>
        public const string IpcOperations = "s3drive.ipc.operations";

        /// <summary>
        /// Build information gauge (always 1), labeled with version, runtime, and operating system.
        /// </summary>
        public const string BuildInfo = "s3drive.build.info";

        /// <summary>
        /// Configured metadata cache lifetime, in seconds.
        /// </summary>
        public const string ConfigMetadataCacheSeconds = "s3drive.config.metadata_cache_seconds";

        /// <summary>
        /// Configured drive count, by whether the drive auto-mounts.
        /// </summary>
        public const string ConfigDrives = "s3drive.config.drives";

        /// <summary>
        /// Crash reports written, by error type.
        /// </summary>
        public const string Crashes = "s3drive.crashes";

        /// <summary>
        /// Log messages written, by severity.
        /// </summary>
        public const string LogMessages = "s3drive.log.messages";

        /// <summary>
        /// Attribute: the drive letter (for example <c>S</c>). Bounded to 26 values.
        /// </summary>
        public const string AttrDrive = "s3drive.drive";

        /// <summary>
        /// Attribute: the drive identifier. Span-only (never a metric label).
        /// </summary>
        public const string AttrDriveId = "s3drive.drive.id";

        /// <summary>
        /// Attribute: the S3 provider kind (<c>aws_s3</c> or <c>s3_compatible</c>).
        /// </summary>
        public const string AttrProvider = "s3drive.provider";

        /// <summary>
        /// Attribute: the bounded outcome of an operation (see the <c>Outcome*</c> constants).
        /// </summary>
        public const string AttrOutcome = "s3drive.outcome";

        /// <summary>
        /// Attribute: the OpenTelemetry error type (the exception type name, or an NTSTATUS name).
        /// </summary>
        public const string AttrErrorType = "error.type";

        /// <summary>
        /// Attribute: the filesystem operation (see the <c>Fs*</c> operation constants).
        /// </summary>
        public const string AttrFsOperation = "s3drive.fs.operation";

        /// <summary>
        /// Attribute: the NTSTATUS returned to the operating system. Span-only.
        /// </summary>
        public const string AttrNtStatus = "s3drive.fs.ntstatus";

        /// <summary>
        /// Attribute: the S3 operation (for example <c>HeadObject</c>).
        /// </summary>
        public const string AttrS3Operation = "s3drive.s3.operation";

        /// <summary>
        /// Attribute: a workflow or pipeline stage name.
        /// </summary>
        public const string AttrStage = "s3drive.stage";

        /// <summary>
        /// Attribute: a transfer direction (<c>read</c>/<c>write</c> for the filesystem, <c>download</c>/<c>upload</c> for S3).
        /// </summary>
        public const string AttrDirection = "s3drive.direction";

        /// <summary>
        /// Attribute: the metadata cache kind (<c>head</c> or <c>listing</c>).
        /// </summary>
        public const string AttrCacheKind = "s3drive.cache.kind";

        /// <summary>
        /// Attribute: the metadata cache lookup result (<c>hit</c>, <c>miss</c>, or <c>bypass</c>).
        /// </summary>
        public const string AttrCacheResult = "s3drive.cache.result";

        /// <summary>
        /// Attribute: the metadata cache invalidation scope (<c>key</c>, <c>prefix</c>, or <c>clear</c>).
        /// </summary>
        public const string AttrCacheScope = "s3drive.cache.scope";

        /// <summary>
        /// Attribute: the mount operation (<c>mount</c> or <c>unmount</c>).
        /// </summary>
        public const string AttrMountOperation = "s3drive.mount.operation";

        /// <summary>
        /// Attribute: a drive mount state name.
        /// </summary>
        public const string AttrState = "s3drive.state";

        /// <summary>
        /// Attribute: the agent command type.
        /// </summary>
        public const string AttrCommandType = "s3drive.command.type";

        /// <summary>
        /// Attribute: the IPC / configuration I/O operation.
        /// </summary>
        public const string AttrIpcOperation = "s3drive.ipc.operation";

        /// <summary>
        /// Attribute: the reason a multi-object delete fell back (<c>partial</c> or <c>unsupported</c>).
        /// </summary>
        public const string AttrReason = "s3drive.reason";

        /// <summary>
        /// Attribute: whether a configured drive auto-mounts (<c>true</c>/<c>false</c>).
        /// </summary>
        public const string AttrAutoMount = "s3drive.automount";

        /// <summary>
        /// Attribute: a log severity name.
        /// </summary>
        public const string AttrSeverity = "s3drive.severity";

        /// <summary>
        /// Attribute: the S3Drive version.
        /// </summary>
        public const string AttrVersion = "s3drive.version";

        /// <summary>
        /// Attribute: the .NET runtime version.
        /// </summary>
        public const string AttrRuntime = "s3drive.runtime";

        /// <summary>
        /// Attribute: the operating system type (OpenTelemetry <c>os.type</c>).
        /// </summary>
        public const string AttrOsType = "os.type";

        /// <summary>
        /// Span attribute: the object key or prefix. Emitted only when object keys are opted in.
        /// </summary>
        public const string AttrObjectKey = "s3drive.object.key";

        /// <summary>
        /// Span attribute: the destination object key of a copy or move. Emitted only when object keys are opted in.
        /// </summary>
        public const string AttrObjectDestination = "s3drive.object.destination";

        /// <summary>
        /// Span attribute: a byte count.
        /// </summary>
        public const string AttrBytes = "s3drive.bytes";

        /// <summary>
        /// Span attribute: an entry or key count.
        /// </summary>
        public const string AttrCount = "s3drive.count";

        /// <summary>
        /// Span attribute: the S3 bucket (OpenTelemetry <c>aws.s3.bucket</c>).
        /// </summary>
        public const string AttrBucket = "aws.s3.bucket";

        /// <summary>
        /// Span attribute: the RPC system (OpenTelemetry <c>rpc.system</c>).
        /// </summary>
        public const string AttrRpcSystem = "rpc.system";

        /// <summary>
        /// Span attribute: the RPC service (OpenTelemetry <c>rpc.service</c>).
        /// </summary>
        public const string AttrRpcService = "rpc.service";

        /// <summary>
        /// Span attribute: the RPC method (OpenTelemetry <c>rpc.method</c>).
        /// </summary>
        public const string AttrRpcMethod = "rpc.method";

        /// <summary>
        /// Span attribute: the file mode (disposition) requested by the operating system.
        /// </summary>
        public const string AttrFileMode = "s3drive.fs.mode";

        /// <summary>
        /// Span attribute: whether the target is a directory.
        /// </summary>
        public const string AttrIsDirectory = "s3drive.fs.is_directory";

        /// <summary>
        /// Span attribute: whether a metadata lookup was served from the cache.
        /// </summary>
        public const string AttrCacheHit = "s3drive.cache.hit";

        /// <summary>
        /// Outcome: the operation succeeded.
        /// </summary>
        public const string OutcomeSuccess = "success";

        /// <summary>
        /// Outcome: the target did not exist.
        /// </summary>
        public const string OutcomeNotFound = "not_found";

        /// <summary>
        /// Outcome: the target already existed (name collision).
        /// </summary>
        public const string OutcomeConflict = "conflict";

        /// <summary>
        /// Outcome: the request was refused as invalid, denied, unsupported, or not empty.
        /// </summary>
        public const string OutcomeRejected = "rejected";

        /// <summary>
        /// Outcome: the operation was cancelled (for example by an unmount).
        /// </summary>
        public const string OutcomeCancelled = "cancelled";

        /// <summary>
        /// Outcome: the operation failed.
        /// </summary>
        public const string OutcomeError = "error";

        /// <summary>
        /// Outcome: the operation was skipped (for example a command whose target is unknown).
        /// </summary>
        public const string OutcomeSkipped = "skipped";

        /// <summary>
        /// Filesystem operation: open or create a file or directory.
        /// </summary>
        public const string FsCreateFile = "create_file";

        /// <summary>
        /// Filesystem operation: last handle closed (commits writes and pending deletes).
        /// </summary>
        public const string FsCleanup = "cleanup";

        /// <summary>
        /// Filesystem operation: read from a file.
        /// </summary>
        public const string FsReadFile = "read_file";

        /// <summary>
        /// Filesystem operation: write to a file.
        /// </summary>
        public const string FsWriteFile = "write_file";

        /// <summary>
        /// Filesystem operation: read file or directory attributes.
        /// </summary>
        public const string FsGetFileInformation = "get_file_information";

        /// <summary>
        /// Filesystem operation: enumerate a directory.
        /// </summary>
        public const string FsFindFiles = "find_files";

        /// <summary>
        /// Filesystem operation: mark a file for deletion.
        /// </summary>
        public const string FsDeleteFile = "delete_file";

        /// <summary>
        /// Filesystem operation: mark a directory for deletion.
        /// </summary>
        public const string FsDeleteDirectory = "delete_directory";

        /// <summary>
        /// Filesystem operation: rename or move a file or directory.
        /// </summary>
        public const string FsMoveFile = "move_file";

        /// <summary>
        /// Filesystem operation: set the end of file or allocation size (truncate or extend).
        /// </summary>
        public const string FsSetLength = "set_length";

        /// <summary>
        /// Stage: waiting for a per-object lock.
        /// </summary>
        public const string StageLockWait = "lock_wait";

        /// <summary>
        /// Stage: downloading an object into local staging.
        /// </summary>
        public const string StageDownload = "download";

        /// <summary>
        /// Stage: uploading a staged file to its object.
        /// </summary>
        public const string StageUpload = "upload";

        /// <summary>
        /// Stage: copying objects for a rename or move.
        /// </summary>
        public const string StageCopy = "copy";

        /// <summary>
        /// Stage: deleting objects.
        /// </summary>
        public const string StageDelete = "delete";

        /// <summary>
        /// Stage: listing every key under a prefix (directory rename).
        /// </summary>
        public const string StageListAll = "list_all";

        /// <summary>
        /// Stage: creating a directory marker object.
        /// </summary>
        public const string StageMkdir = "mkdir";

        /// <summary>
        /// Mount stage: decrypting the secret key.
        /// </summary>
        public const string StageDecryptCredentials = "decrypt_credentials";

        /// <summary>
        /// Mount stage: resetting the local staging directory.
        /// </summary>
        public const string StagePrepareStaging = "prepare_staging";

        /// <summary>
        /// Mount stage: building the storage client.
        /// </summary>
        public const string StageBuildStore = "build_store";

        /// <summary>
        /// Mount stage: mounting the Dokan volume.
        /// </summary>
        public const string StageDokanMount = "dokan_mount";

        /// <summary>
        /// Unmount stage: tearing down the Dokan volume.
        /// </summary>
        public const string StageDokanUnmount = "dokan_unmount";

        /// <summary>
        /// Command stage: time between the command being written and the agent picking it up.
        /// </summary>
        public const string StageQueued = "queued";

        /// <summary>
        /// Command stage: reading and parsing the command file.
        /// </summary>
        public const string StageParse = "parse";

        /// <summary>
        /// Command stage: executing the command (including the status publish that follows).
        /// </summary>
        public const string StageExecute = "execute";

        /// <summary>
        /// Mount operation: mount.
        /// </summary>
        public const string MountOperationMount = "mount";

        /// <summary>
        /// Mount operation: unmount.
        /// </summary>
        public const string MountOperationUnmount = "unmount";

        /// <summary>
        /// IPC operation: the TUI writes a command file.
        /// </summary>
        public const string IpcCommandSend = "command_send";

        /// <summary>
        /// IPC operation: the agent writes status.json.
        /// </summary>
        public const string IpcStatusWrite = "status_write";

        /// <summary>
        /// IPC operation: a client reads status.json.
        /// </summary>
        public const string IpcStatusRead = "status_read";

        /// <summary>
        /// IPC operation: configuration load.
        /// </summary>
        public const string IpcConfigLoad = "config_load";

        /// <summary>
        /// IPC operation: configuration save.
        /// </summary>
        public const string IpcConfigSave = "config_save";

        /// <summary>
        /// S3 operation: object existence probe.
        /// </summary>
        public const string S3ObjectExists = "ObjectExists";

        /// <summary>
        /// S3 operation: read object metadata.
        /// </summary>
        public const string S3HeadObject = "HeadObject";

        /// <summary>
        /// S3 operation: list the immediate children of a prefix.
        /// </summary>
        public const string S3ListObjects = "ListObjects";

        /// <summary>
        /// S3 operation: list every key under a prefix.
        /// </summary>
        public const string S3ListAllKeys = "ListAllKeys";

        /// <summary>
        /// S3 operation: download an object.
        /// </summary>
        public const string S3GetObject = "GetObject";

        /// <summary>
        /// S3 operation: upload an object.
        /// </summary>
        public const string S3PutObject = "PutObject";

        /// <summary>
        /// S3 operation: delete one object.
        /// </summary>
        public const string S3DeleteObject = "DeleteObject";

        /// <summary>
        /// S3 operation: delete many objects (multi-object delete).
        /// </summary>
        public const string S3DeleteObjects = "DeleteObjects";

        /// <summary>
        /// S3 operation: copy an object (read and rewrite).
        /// </summary>
        public const string S3CopyObject = "CopyObject";

        /// <summary>
        /// S3 operation: validate connectivity to the endpoint and bucket.
        /// </summary>
        public const string S3ValidateConnectivity = "ValidateConnectivity";

        /// <summary>
        /// Command type label used when a command file could not be parsed.
        /// </summary>
        public const string CommandTypeUnknown = "unknown";

        /// <summary>
        /// Span name prefix for filesystem operations (<c>fs &lt;operation&gt;</c>).
        /// </summary>
        public const string SpanFsPrefix = "fs ";

        /// <summary>
        /// Span name prefix for outbound S3 calls (<c>S3 &lt;Operation&gt;</c>).
        /// </summary>
        public const string SpanS3Prefix = "S3 ";

        /// <summary>
        /// Span name prefix for workflow and pipeline stages (<c>stage:&lt;name&gt;</c>).
        /// </summary>
        public const string SpanStagePrefix = "stage:";

        /// <summary>
        /// Span name prefix for agent commands (<c>command &lt;Type&gt;</c>).
        /// </summary>
        public const string SpanCommandPrefix = "command ";

        /// <summary>
        /// Span name prefix for drive lifecycle operations (<c>drive mount</c>, <c>drive unmount</c>).
        /// </summary>
        public const string SpanDrivePrefix = "drive ";

        /// <summary>
        /// Span name prefix for IPC and configuration I/O (<c>ipc &lt;operation&gt;</c>).
        /// </summary>
        public const string SpanIpcPrefix = "ipc ";
    }
}
