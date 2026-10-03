namespace S3Drive.Core.Telemetry
{
    using System.Collections.Generic;

    /// <summary>
    /// The complete catalog of metric instruments S3Drive emits on the <see cref="TelemetryNames.MeterName"/>
    /// meter, with their units, allowed label keys, and histogram buckets. Every label set is
    /// bounded: drive letters (at most 26), enumerated operations, stages, outcomes, and exception
    /// type names. Identifiers, object keys, and free-form text never appear as labels.
    /// </summary>
    public static class S3DriveMetricCatalog
    {
        private static readonly double[] _NetworkBuckets = new double[] { 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120 };
        private static readonly double[] _LockBuckets = new double[] { 0.0001, 0.0005, 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10, 30 };
        private static readonly double[] _PipelineBuckets = new double[] { 0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60 };
        private static readonly double[] _IoBuckets = new double[] { 0.0005, 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1 };

        private static readonly IReadOnlyList<MetricDescriptor> _All = new List<MetricDescriptor>
        {
            Histogram(TelemetryNames.FsOperationDuration, "Duration of filesystem (Dokan) operations handled for the operating system.", _NetworkBuckets,
                TelemetryNames.AttrDrive, TelemetryNames.AttrFsOperation, TelemetryNames.AttrOutcome, TelemetryNames.AttrErrorType),
            Counter(TelemetryNames.FsOperations, "{operation}", "Filesystem (Dokan) operations handled for the operating system.",
                TelemetryNames.AttrDrive, TelemetryNames.AttrFsOperation, TelemetryNames.AttrOutcome, TelemetryNames.AttrErrorType),
            Counter(TelemetryNames.FsBytes, "By", "Bytes served to (read) or accepted from (write) the operating system.",
                TelemetryNames.AttrDrive, TelemetryNames.AttrDirection),
            UpDown(TelemetryNames.FsOpenHandles, "{handle}", "Open file and directory handles currently held by the operating system.",
                TelemetryNames.AttrDrive),
            Histogram(TelemetryNames.FsStageDuration, "Duration of filesystem workflow stages (download, upload, copy, delete, list_all, mkdir).", _NetworkBuckets,
                TelemetryNames.AttrDrive, TelemetryNames.AttrStage, TelemetryNames.AttrOutcome),

            Histogram(TelemetryNames.S3RequestDuration, "Duration of outbound S3 requests.", _NetworkBuckets,
                TelemetryNames.AttrDrive, TelemetryNames.AttrProvider, TelemetryNames.AttrS3Operation, TelemetryNames.AttrOutcome, TelemetryNames.AttrErrorType),
            Counter(TelemetryNames.S3Requests, "{request}", "Outbound S3 requests.",
                TelemetryNames.AttrDrive, TelemetryNames.AttrProvider, TelemetryNames.AttrS3Operation, TelemetryNames.AttrOutcome, TelemetryNames.AttrErrorType),
            Counter(TelemetryNames.S3Bytes, "By", "Bytes transferred to (upload) or from (download) the S3 endpoint.",
                TelemetryNames.AttrDrive, TelemetryNames.AttrDirection),
            Counter(TelemetryNames.S3SuppressedErrors, "{error}", "S3 failures absorbed by the storage layer (for example a HEAD failure reported as an absent object).",
                TelemetryNames.AttrDrive, TelemetryNames.AttrS3Operation, TelemetryNames.AttrErrorType),
            Counter(TelemetryNames.S3BatchDeleteFallbacks, "{batch}", "Multi-object delete batches that fell back to per-key deletes.",
                TelemetryNames.AttrDrive, TelemetryNames.AttrReason),

            Counter(TelemetryNames.CacheLookups, "{lookup}", "Metadata cache lookups by kind and result (hit, miss, bypass when caching is disabled).",
                TelemetryNames.AttrDrive, TelemetryNames.AttrCacheKind, TelemetryNames.AttrCacheResult),
            UpDown(TelemetryNames.CacheEntries, "{entry}", "Metadata cache entries currently held.",
                TelemetryNames.AttrDrive, TelemetryNames.AttrCacheKind),
            Counter(TelemetryNames.CacheInvalidations, "{invalidation}", "Metadata cache invalidations by scope.",
                TelemetryNames.AttrDrive, TelemetryNames.AttrCacheScope),

            Histogram(TelemetryNames.LockWaitDuration, "Time spent waiting for a per-object lock.", _LockBuckets,
                TelemetryNames.AttrDrive, TelemetryNames.AttrOutcome),
            UpDown(TelemetryNames.LockHeld, "{lock}", "Per-object locks currently held.",
                TelemetryNames.AttrDrive),
            Counter(TelemetryNames.LockContentions, "{acquisition}", "Lock acquisitions that found the object already locked.",
                TelemetryNames.AttrDrive),

            Counter(TelemetryNames.MountOperations, "{operation}", "Mount and unmount operations.",
                TelemetryNames.AttrMountOperation, TelemetryNames.AttrOutcome, TelemetryNames.AttrErrorType),
            Histogram(TelemetryNames.MountDuration, "Duration of mount and unmount operations.", _PipelineBuckets,
                TelemetryNames.AttrMountOperation, TelemetryNames.AttrOutcome, TelemetryNames.AttrErrorType),
            Histogram(TelemetryNames.MountStageDuration, "Duration of mount and unmount stages.", _PipelineBuckets,
                TelemetryNames.AttrStage, TelemetryNames.AttrOutcome),
            Gauge(TelemetryNames.DrivesMounted, "{drive}", "Drives currently mounted."),
            Gauge(TelemetryNames.DriveState, "{drive}", "Per-drive mount state; 1 for the drive's current state.",
                TelemetryNames.AttrDrive, TelemetryNames.AttrState),

            Counter(TelemetryNames.CommandJobs, "{command}", "Agent commands processed, by type and outcome.",
                TelemetryNames.AttrCommandType, TelemetryNames.AttrOutcome),
            Histogram(TelemetryNames.CommandStageDuration, "Duration of agent command pipeline stages (queued, parse, execute).", _PipelineBuckets,
                TelemetryNames.AttrCommandType, TelemetryNames.AttrStage, TelemetryNames.AttrOutcome),
            Counter(TelemetryNames.CommandStageEvents, "{event}", "Agent command pipeline stage completions, by stage and outcome.",
                TelemetryNames.AttrCommandType, TelemetryNames.AttrStage, TelemetryNames.AttrOutcome),
            Gauge(TelemetryNames.CommandLastSuccess, "s", "Unix time of the last successfully executed command; 0 before the first."),
            Gauge(TelemetryNames.CommandQueueDepth, "{command}", "Command files pending at the most recent poll."),
            Gauge(TelemetryNames.AgentLastPoll, "s", "Unix time of the agent's most recent command poll (liveness heartbeat); 0 before the first."),

            Histogram(TelemetryNames.IpcDuration, "Duration of file-based IPC and configuration I/O.", _IoBuckets,
                TelemetryNames.AttrIpcOperation, TelemetryNames.AttrOutcome, TelemetryNames.AttrErrorType),
            Counter(TelemetryNames.IpcOperations, "{operation}", "File-based IPC and configuration I/O operations.",
                TelemetryNames.AttrIpcOperation, TelemetryNames.AttrOutcome, TelemetryNames.AttrErrorType),

            Gauge(TelemetryNames.BuildInfo, "{info}", "Build information; always 1.",
                TelemetryNames.AttrVersion, TelemetryNames.AttrRuntime, TelemetryNames.AttrOsType),
            Gauge(TelemetryNames.ConfigMetadataCacheSeconds, "s", "Configured metadata cache lifetime."),
            Gauge(TelemetryNames.ConfigDrives, "{drive}", "Configured drives, by whether they auto-mount.",
                TelemetryNames.AttrAutoMount),
            Counter(TelemetryNames.Crashes, "{crash}", "Crash reports written.",
                TelemetryNames.AttrErrorType),
            Counter(TelemetryNames.LogMessages, "{message}", "Log messages written, by severity.",
                TelemetryNames.AttrSeverity)
        };

        /// <summary>
        /// Every metric instrument S3Drive emits. Never null.
        /// </summary>
        public static IReadOnlyList<MetricDescriptor> All
        {
            get { return _All; }
        }

        /// <summary>
        /// Finds a descriptor by instrument name.
        /// </summary>
        /// <param name="name">The instrument name. Null returns null.</param>
        /// <returns>The descriptor, or null when the name is not in the catalog.</returns>
        public static MetricDescriptor? Find(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (MetricDescriptor descriptor in _All)
            {
                if (descriptor.Name == name) return descriptor;
            }

            return null;
        }

        private static MetricDescriptor Counter(string name, string unit, string description, params string[] labels)
        {
            return new MetricDescriptor(name, MetricKindEnum.Counter, unit, description, labels, null);
        }

        private static MetricDescriptor UpDown(string name, string unit, string description, params string[] labels)
        {
            return new MetricDescriptor(name, MetricKindEnum.UpDownCounter, unit, description, labels, null);
        }

        private static MetricDescriptor Histogram(string name, string description, double[] buckets, params string[] labels)
        {
            return new MetricDescriptor(name, MetricKindEnum.Histogram, "s", description, labels, buckets);
        }

        private static MetricDescriptor Gauge(string name, string unit, string description, params string[] labels)
        {
            return new MetricDescriptor(name, MetricKindEnum.Gauge, unit, description, labels, null);
        }
    }
}
