namespace S3Drive.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Threading;
    using Amazon.S3;
    using DokanNet;
    using S3Drive.Core.Ipc;
    using S3Drive.Core.Mounting;

    /// <summary>
    /// The S3Drive telemetry emitter. Owns the <see cref="TelemetryNames.MeterName"/> meter and the
    /// <see cref="TelemetryNames.ActivitySourceName"/> activity source and records every instrument
    /// in <see cref="S3DriveMetricCatalog"/>. Emission rides the .NET base class library only: with
    /// no listener subscribed, every call is a near-free no-op. A host subscribes by name (for
    /// example Radiant's <c>settings.Sources.AddMeter("S3Drive")</c>). Instrumentation is best-effort;
    /// no member throws into the caller.
    /// </summary>
    public static class S3DriveTelemetry
    {
        private static readonly string _Version = ResolveVersion();
        private static readonly Meter _Meter = new Meter(TelemetryNames.MeterName, _Version);
        private static readonly ActivitySource _Source = new ActivitySource(TelemetryNames.ActivitySourceName, _Version);

        private static readonly object _MountSync = new object();
        private static readonly List<WeakReference<MountManager>> _MountManagers = new List<WeakReference<MountManager>>();

        private static long _CommandLastSuccessSeconds;
        private static long _AgentLastPollSeconds;
        private static int _CommandQueueDepth;
        private static int _ConfigMetadataCacheSeconds = -1;
        private static int _ConfigDrives = -1;
        private static int _ConfigAutoMountDrives = -1;
        private static bool _IncludeObjectKeys;

        internal static readonly Histogram<double> FsOperationDuration = CreateHistogram(TelemetryNames.FsOperationDuration);
        internal static readonly Counter<long> FsOperations = CreateCounter(TelemetryNames.FsOperations);
        internal static readonly Counter<long> FsBytes = CreateCounter(TelemetryNames.FsBytes);
        internal static readonly UpDownCounter<long> FsOpenHandles = CreateUpDown(TelemetryNames.FsOpenHandles);
        internal static readonly Histogram<double> FsStageDuration = CreateHistogram(TelemetryNames.FsStageDuration);

        internal static readonly Histogram<double> S3RequestDuration = CreateHistogram(TelemetryNames.S3RequestDuration);
        internal static readonly Counter<long> S3Requests = CreateCounter(TelemetryNames.S3Requests);
        internal static readonly Counter<long> S3Bytes = CreateCounter(TelemetryNames.S3Bytes);
        internal static readonly Counter<long> S3SuppressedErrors = CreateCounter(TelemetryNames.S3SuppressedErrors);
        internal static readonly Counter<long> S3BatchDeleteFallbacks = CreateCounter(TelemetryNames.S3BatchDeleteFallbacks);

        internal static readonly Counter<long> CacheLookups = CreateCounter(TelemetryNames.CacheLookups);
        internal static readonly UpDownCounter<long> CacheEntries = CreateUpDown(TelemetryNames.CacheEntries);
        internal static readonly Counter<long> CacheInvalidations = CreateCounter(TelemetryNames.CacheInvalidations);

        internal static readonly Histogram<double> LockWaitDuration = CreateHistogram(TelemetryNames.LockWaitDuration);
        internal static readonly UpDownCounter<long> LockHeld = CreateUpDown(TelemetryNames.LockHeld);
        internal static readonly Counter<long> LockContentions = CreateCounter(TelemetryNames.LockContentions);

        internal static readonly Counter<long> MountOperations = CreateCounter(TelemetryNames.MountOperations);
        internal static readonly Histogram<double> MountDuration = CreateHistogram(TelemetryNames.MountDuration);
        internal static readonly Histogram<double> MountStageDuration = CreateHistogram(TelemetryNames.MountStageDuration);

        internal static readonly Counter<long> CommandJobs = CreateCounter(TelemetryNames.CommandJobs);
        internal static readonly Histogram<double> CommandStageDuration = CreateHistogram(TelemetryNames.CommandStageDuration);
        internal static readonly Counter<long> CommandStageEvents = CreateCounter(TelemetryNames.CommandStageEvents);

        internal static readonly Histogram<double> IpcDuration = CreateHistogram(TelemetryNames.IpcDuration);
        internal static readonly Counter<long> IpcOperations = CreateCounter(TelemetryNames.IpcOperations);

        internal static readonly Counter<long> Crashes = CreateCounter(TelemetryNames.Crashes);
        internal static readonly Counter<long> LogMessages = CreateCounter(TelemetryNames.LogMessages);

        private static readonly ObservableGauge<int> _DrivesMounted = CreateGauge<int>(TelemetryNames.DrivesMounted, ObserveDrivesMounted);
        private static readonly ObservableGauge<int> _DriveState = CreateGauge<int>(TelemetryNames.DriveState, ObserveDriveStates);
        private static readonly ObservableGauge<long> _CommandLastSuccess = CreateGauge<long>(TelemetryNames.CommandLastSuccess, ObserveCommandLastSuccess);
        private static readonly ObservableGauge<int> _CommandQueue = CreateGauge<int>(TelemetryNames.CommandQueueDepth, ObserveCommandQueueDepth);
        private static readonly ObservableGauge<long> _AgentLastPoll = CreateGauge<long>(TelemetryNames.AgentLastPoll, ObserveAgentLastPoll);
        private static readonly ObservableGauge<int> _BuildInfo = CreateGauge<int>(TelemetryNames.BuildInfo, ObserveBuildInfo);
        private static readonly ObservableGauge<int> _ConfigCacheSeconds = CreateGauge<int>(TelemetryNames.ConfigMetadataCacheSeconds, ObserveConfigCacheSeconds);
        private static readonly ObservableGauge<int> _ConfigDrivesGauge = CreateGauge<int>(TelemetryNames.ConfigDrives, ObserveConfigDrives);

        /// <summary>
        /// The S3Drive version reported on the meter, the activity source, and <c>s3drive.build.info</c>.
        /// Never null.
        /// </summary>
        public static string Version
        {
            get { return _Version; }
        }

        /// <summary>
        /// The meter every S3Drive metric is emitted on. Never null.
        /// </summary>
        public static Meter Meter
        {
            get { return _Meter; }
        }

        /// <summary>
        /// The activity source every S3Drive span is emitted on. Never null.
        /// </summary>
        public static ActivitySource ActivitySource
        {
            get { return _Source; }
        }

        /// <summary>
        /// Whether spans carry object keys and prefixes (<c>s3drive.object.key</c>). Object keys are
        /// user content (file and folder names), so they are omitted by default. Metrics never carry
        /// object keys regardless of this setting. Defaults to false.
        /// </summary>
        public static bool IncludeObjectKeys
        {
            get { return Volatile.Read(ref _IncludeObjectKeys); }
            set { Volatile.Write(ref _IncludeObjectKeys, value); }
        }

        /// <summary>
        /// Publishes the safe configuration values reported by the <c>s3drive.config.*</c> gauges.
        /// Negative values are clamped to zero.
        /// </summary>
        /// <param name="metadataCacheSeconds">The configured metadata cache lifetime, in seconds.</param>
        /// <param name="configuredDrives">The number of configured drives.</param>
        /// <param name="autoMountDrives">The number of configured drives that auto-mount.</param>
        public static void SetConfigSnapshot(int metadataCacheSeconds, int configuredDrives, int autoMountDrives)
        {
            Volatile.Write(ref _ConfigMetadataCacheSeconds, Math.Max(0, metadataCacheSeconds));
            Volatile.Write(ref _ConfigDrives, Math.Max(0, configuredDrives));
            Volatile.Write(ref _ConfigAutoMountDrives, Math.Clamp(autoMountDrives, 0, Math.Max(0, configuredDrives)));
        }

        internal static string NormalizeDrive(string? driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return "unknown";
            char letter = char.ToUpperInvariant(driveLetter.Trim()[0]);
            if (letter < 'A' || letter > 'Z') return "unknown";
            return letter.ToString();
        }

        internal static Activity? StartActivity(string name, ActivityKind kind)
        {
            try
            {
                return _Source.StartActivity(name, kind);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static Activity? StartActivity(string name, ActivityKind kind, string? traceParent, DateTimeOffset startTime)
        {
            try
            {
                ActivityContext parent = default;
                if (!string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, null, out ActivityContext parsed))
                {
                    parent = parsed;
                }

                return _Source.StartActivity(name, kind, parent, null, null, startTime);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static string? CurrentTraceParent()
        {
            Activity? current = Activity.Current;
            if (current == null || current.IdFormat != ActivityIdFormat.W3C) return null;
            return current.Id;
        }

        internal static TelemetryScope StartFs(string drive, string operation, bool withSpan)
        {
            Activity? activity = withSpan ? StartActivity(TelemetryNames.SpanFsPrefix + operation, ActivityKind.Server) : null;
            if (activity != null)
            {
                activity.SetTag(TelemetryNames.AttrDrive, drive);
                activity.SetTag(TelemetryNames.AttrFsOperation, operation);
            }

            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrDrive, drive);
            tags.Add(TelemetryNames.AttrFsOperation, operation);
            return new TelemetryScope(activity, FsOperationDuration, FsOperations, tags, true);
        }

        internal static NtStatus CompleteFs(TelemetryScope scope, NtStatus status)
        {
            try
            {
                scope.SetTag(TelemetryNames.AttrNtStatus, status.ToString());
                string outcome = OutcomeFor(status);
                if (!string.Equals(outcome, TelemetryNames.OutcomeSuccess, StringComparison.Ordinal)) scope.SetErrorType(status.ToString());
                scope.Complete(outcome);
            }
            catch (Exception)
            {
            }

            return status;
        }

        internal static TelemetryScope StartFsStage(string drive, string stage)
        {
            Activity? activity = StartActivity(TelemetryNames.SpanStagePrefix + stage, ActivityKind.Internal);
            if (activity != null)
            {
                activity.SetTag(TelemetryNames.AttrDrive, drive);
                activity.SetTag(TelemetryNames.AttrStage, stage);
            }

            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrDrive, drive);
            tags.Add(TelemetryNames.AttrStage, stage);
            return new TelemetryScope(activity, FsStageDuration, null, tags, false);
        }

        internal static TelemetryScope StartS3(string drive, string provider, string operation)
        {
            Activity? activity = StartActivity(TelemetryNames.SpanS3Prefix + operation, ActivityKind.Client);
            if (activity != null)
            {
                activity.SetTag(TelemetryNames.AttrRpcSystem, "aws-api");
                activity.SetTag(TelemetryNames.AttrRpcService, "S3");
                activity.SetTag(TelemetryNames.AttrRpcMethod, operation);
                activity.SetTag(TelemetryNames.AttrDrive, drive);
                activity.SetTag(TelemetryNames.AttrProvider, provider);
            }

            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrDrive, drive);
            tags.Add(TelemetryNames.AttrProvider, provider);
            tags.Add(TelemetryNames.AttrS3Operation, operation);
            return new TelemetryScope(activity, S3RequestDuration, S3Requests, tags, true);
        }

        internal static TelemetryScope StartMount(string operation, string drive, string driveId)
        {
            Activity? activity = StartActivity(TelemetryNames.SpanDrivePrefix + operation, ActivityKind.Internal);
            if (activity != null)
            {
                activity.SetTag(TelemetryNames.AttrMountOperation, operation);
                activity.SetTag(TelemetryNames.AttrDrive, drive);
                activity.SetTag(TelemetryNames.AttrDriveId, driveId);
            }

            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrMountOperation, operation);
            return new TelemetryScope(activity, MountDuration, MountOperations, tags, true);
        }

        internal static TelemetryScope StartMountStage(string stage)
        {
            Activity? activity = StartActivity(TelemetryNames.SpanStagePrefix + stage, ActivityKind.Internal);
            activity?.SetTag(TelemetryNames.AttrStage, stage);

            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrStage, stage);
            return new TelemetryScope(activity, MountStageDuration, null, tags, false);
        }

        internal static TelemetryScope StartIpc(string operation)
        {
            Activity? activity = StartActivity(TelemetryNames.SpanIpcPrefix + operation,
                string.Equals(operation, TelemetryNames.IpcCommandSend, StringComparison.Ordinal) ? ActivityKind.Producer : ActivityKind.Internal);
            activity?.SetTag(TelemetryNames.AttrIpcOperation, operation);

            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrIpcOperation, operation);
            return new TelemetryScope(activity, IpcDuration, IpcOperations, tags, true);
        }

        internal static TelemetryScope StartCommandStage(string commandType, string stage)
        {
            Activity? activity = StartActivity(TelemetryNames.SpanStagePrefix + stage, ActivityKind.Internal);
            return WrapCommandStage(activity, commandType, stage);
        }

        internal static TelemetryScope WrapCommandStage(Activity? activity, string commandType, string stage)
        {
            if (activity != null)
            {
                activity.SetTag(TelemetryNames.AttrCommandType, commandType);
                activity.SetTag(TelemetryNames.AttrStage, stage);
            }

            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrCommandType, commandType);
            tags.Add(TelemetryNames.AttrStage, stage);
            return new TelemetryScope(activity, CommandStageDuration, CommandStageEvents, tags, false);
        }

        internal static void RecordCommandStage(string commandType, string stage, string outcome, DateTimeOffset start, DateTimeOffset end)
        {
            try
            {
                double seconds = Math.Max(0, (end - start).TotalSeconds);
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrCommandType, commandType);
                tags.Add(TelemetryNames.AttrStage, stage);
                tags.Add(TelemetryNames.AttrOutcome, outcome);
                CommandStageDuration.Record(seconds, tags);
                CommandStageEvents.Add(1, tags);

                // A stage that already happened (queued, parse) is emitted as a span with its real
                // start and end times so the trace waterfall shows where the time went.
                Activity? activity = _Source.StartActivity(TelemetryNames.SpanStagePrefix + stage, ActivityKind.Internal, default(ActivityContext), null, null, start);
                if (activity != null)
                {
                    activity.SetTag(TelemetryNames.AttrCommandType, commandType);
                    activity.SetTag(TelemetryNames.AttrStage, stage);
                    activity.SetTag(TelemetryNames.AttrOutcome, outcome);
                    activity.SetStatus(string.Equals(outcome, TelemetryNames.OutcomeSuccess, StringComparison.Ordinal) ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
                    activity.SetEndTime(end < start ? start.UtcDateTime : end.UtcDateTime);
                    activity.Dispose();
                }
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordCommandJob(string commandType, string outcome)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrCommandType, commandType);
                tags.Add(TelemetryNames.AttrOutcome, outcome);
                CommandJobs.Add(1, tags);
                if (string.Equals(outcome, TelemetryNames.OutcomeSuccess, StringComparison.Ordinal))
                {
                    Volatile.Write(ref _CommandLastSuccessSeconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                }
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordPoll(int pendingCommands)
        {
            Volatile.Write(ref _CommandQueueDepth, Math.Max(0, pendingCommands));
            Volatile.Write(ref _AgentLastPollSeconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        internal static void RecordFsBytes(string drive, string direction, long bytes)
        {
            if (bytes <= 0) return;
            try
            {
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrDrive, drive);
                tags.Add(TelemetryNames.AttrDirection, direction);
                FsBytes.Add(bytes, tags);
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordOpenHandle(string drive, int delta)
        {
            try
            {
                FsOpenHandles.Add(delta, new KeyValuePair<string, object?>(TelemetryNames.AttrDrive, drive));
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordS3Bytes(string drive, string direction, long bytes)
        {
            if (bytes <= 0) return;
            try
            {
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrDrive, drive);
                tags.Add(TelemetryNames.AttrDirection, direction);
                S3Bytes.Add(bytes, tags);
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordSuppressedS3Error(string drive, string operation, Exception exception)
        {
            try
            {
                string errorType = ErrorType(exception);
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrDrive, drive);
                tags.Add(TelemetryNames.AttrS3Operation, operation);
                tags.Add(TelemetryNames.AttrErrorType, errorType);
                S3SuppressedErrors.Add(1, tags);

                Activity? current = Activity.Current;
                if (current != null)
                {
                    AddException(current, exception);
                    current.SetTag(TelemetryNames.AttrErrorType, errorType);
                    current.SetStatus(ActivityStatusCode.Error, "suppressed: " + errorType);
                }
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordBatchDeleteFallback(string drive, string reason)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrDrive, drive);
                tags.Add(TelemetryNames.AttrReason, reason);
                S3BatchDeleteFallbacks.Add(1, tags);
                Activity.Current?.AddEvent(new ActivityEvent("batch_delete.fallback", default, new ActivityTagsCollection { { TelemetryNames.AttrReason, reason } }));
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordCacheLookup(string drive, string kind, string result)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrDrive, drive);
                tags.Add(TelemetryNames.AttrCacheKind, kind);
                tags.Add(TelemetryNames.AttrCacheResult, result);
                CacheLookups.Add(1, tags);
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordCacheEntries(string drive, string kind, int delta)
        {
            if (delta == 0) return;
            try
            {
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrDrive, drive);
                tags.Add(TelemetryNames.AttrCacheKind, kind);
                CacheEntries.Add(delta, tags);
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordCacheInvalidation(string drive, string scope)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrDrive, drive);
                tags.Add(TelemetryNames.AttrCacheScope, scope);
                CacheInvalidations.Add(1, tags);
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordLockWait(string drive, double seconds, string outcome, bool contended)
        {
            try
            {
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrDrive, drive);
                tags.Add(TelemetryNames.AttrOutcome, outcome);
                LockWaitDuration.Record(seconds, tags);
                if (contended) LockContentions.Add(1, new KeyValuePair<string, object?>(TelemetryNames.AttrDrive, drive));
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordLockHeld(string drive, int delta)
        {
            try
            {
                LockHeld.Add(delta, new KeyValuePair<string, object?>(TelemetryNames.AttrDrive, drive));
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordCrash(Exception exception)
        {
            try
            {
                Crashes.Add(1, new KeyValuePair<string, object?>(TelemetryNames.AttrErrorType, ErrorType(exception)));
            }
            catch (Exception)
            {
            }
        }

        internal static void RecordLog(string severity)
        {
            try
            {
                LogMessages.Add(1, new KeyValuePair<string, object?>(TelemetryNames.AttrSeverity, severity));
            }
            catch (Exception)
            {
            }
        }

        internal static void RegisterMountManager(MountManager manager)
        {
            if (manager == null) return;
            lock (_MountSync)
            {
                _MountManagers.RemoveAll(reference => !reference.TryGetTarget(out _));
                _MountManagers.Add(new WeakReference<MountManager>(manager));
            }
        }

        internal static string ErrorType(Exception exception)
        {
            if (exception == null) return "unknown";

            Exception effective = exception;
            if (effective is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
            {
                effective = aggregate.InnerExceptions[0];
            }

            string result;
            if (effective is AmazonS3Exception s3 && !string.IsNullOrEmpty(s3.ErrorCode))
            {
                result = s3.ErrorCode;
            }
            else
            {
                result = effective.GetType().Name;
            }

            if (result.Length > 64) result = result.Substring(0, 64);
            return result;
        }

        internal static string OutcomeFor(Exception exception)
        {
            if (exception is OperationCanceledException) return TelemetryNames.OutcomeCancelled;
            return TelemetryNames.OutcomeError;
        }

        internal static string OutcomeFor(NtStatus status)
        {
            switch (status)
            {
                case NtStatus.Success:
                    return TelemetryNames.OutcomeSuccess;
                case NtStatus.ObjectNameNotFound:
                case NtStatus.ObjectPathNotFound:
                    return TelemetryNames.OutcomeNotFound;
                case NtStatus.ObjectNameCollision:
                    return TelemetryNames.OutcomeConflict;
                case NtStatus.DirectoryNotEmpty:
                case NtStatus.AccessDenied:
                case NtStatus.InvalidParameter:
                case NtStatus.NotImplemented:
                    return TelemetryNames.OutcomeRejected;
                case NtStatus.Unsuccessful:
                    return TelemetryNames.OutcomeCancelled;
                default:
                    return TelemetryNames.OutcomeError;
            }
        }

        internal static bool IsNotFound(Exception exception)
        {
            if (exception is System.Collections.Generic.KeyNotFoundException) return true;
            if (exception is System.IO.FileNotFoundException) return true;
            if (exception is AmazonS3Exception s3)
            {
                if (s3.StatusCode == System.Net.HttpStatusCode.NotFound) return true;
                if (string.Equals(s3.ErrorCode, "NoSuchKey", StringComparison.Ordinal)) return true;
                if (string.Equals(s3.ErrorCode, "NotFound", StringComparison.Ordinal)) return true;
            }

            return false;
        }

        internal static void AddException(Activity? activity, Exception exception)
        {
            if (activity == null || exception == null) return;
            try
            {
                ActivityTagsCollection tags = new ActivityTagsCollection
                {
                    { "exception.type", exception.GetType().FullName },
                    { "exception.message", exception.Message },
                    { "exception.stacktrace", exception.ToString() }
                };
                activity.AddEvent(new ActivityEvent("exception", default, tags));
            }
            catch (Exception)
            {
            }
        }

        private static Histogram<double> CreateHistogram(string name)
        {
            MetricDescriptor descriptor = Describe(name);
            return _Meter.CreateHistogram<double>(descriptor.Name, descriptor.Unit, descriptor.Description);
        }

        private static Counter<long> CreateCounter(string name)
        {
            MetricDescriptor descriptor = Describe(name);
            return _Meter.CreateCounter<long>(descriptor.Name, descriptor.Unit, descriptor.Description);
        }

        private static UpDownCounter<long> CreateUpDown(string name)
        {
            MetricDescriptor descriptor = Describe(name);
            return _Meter.CreateUpDownCounter<long>(descriptor.Name, descriptor.Unit, descriptor.Description);
        }

        private static ObservableGauge<T> CreateGauge<T>(string name, Func<IEnumerable<Measurement<T>>> observe) where T : struct
        {
            MetricDescriptor descriptor = Describe(name);
            return _Meter.CreateObservableGauge<T>(descriptor.Name, observe, descriptor.Unit, descriptor.Description);
        }

        private static MetricDescriptor Describe(string name)
        {
            MetricDescriptor? descriptor = S3DriveMetricCatalog.Find(name);
            if (descriptor == null) throw new InvalidOperationException("Metric " + name + " is not declared in S3DriveMetricCatalog.");
            return descriptor;
        }

        private static List<DriveStatus> SnapshotDrives()
        {
            List<DriveStatus> drives = new List<DriveStatus>();
            List<MountManager> managers = new List<MountManager>();
            lock (_MountSync)
            {
                foreach (WeakReference<MountManager> reference in _MountManagers)
                {
                    if (reference.TryGetTarget(out MountManager? manager)) managers.Add(manager);
                }
            }

            foreach (MountManager manager in managers)
            {
                try
                {
                    drives.AddRange(manager.BuildStatus().Drives);
                }
                catch (Exception)
                {
                }
            }

            return drives;
        }

        private static IEnumerable<Measurement<int>> ObserveDrivesMounted()
        {
            int mounted = 0;
            foreach (DriveStatus drive in SnapshotDrives())
            {
                if (drive.MountState == DriveMountStateEnum.Mounted) mounted++;
            }

            return new Measurement<int>[] { new Measurement<int>(mounted) };
        }

        private static IEnumerable<Measurement<int>> ObserveDriveStates()
        {
            List<Measurement<int>> result = new List<Measurement<int>>();
            foreach (DriveStatus drive in SnapshotDrives())
            {
                result.Add(new Measurement<int>(1,
                    new KeyValuePair<string, object?>(TelemetryNames.AttrDrive, NormalizeDrive(drive.DriveLetter)),
                    new KeyValuePair<string, object?>(TelemetryNames.AttrState, drive.MountState.ToString())));
            }

            return result;
        }

        private static IEnumerable<Measurement<long>> ObserveCommandLastSuccess()
        {
            return new Measurement<long>[] { new Measurement<long>(Volatile.Read(ref _CommandLastSuccessSeconds)) };
        }

        private static IEnumerable<Measurement<int>> ObserveCommandQueueDepth()
        {
            return new Measurement<int>[] { new Measurement<int>(Volatile.Read(ref _CommandQueueDepth)) };
        }

        private static IEnumerable<Measurement<long>> ObserveAgentLastPoll()
        {
            return new Measurement<long>[] { new Measurement<long>(Volatile.Read(ref _AgentLastPollSeconds)) };
        }

        private static IEnumerable<Measurement<int>> ObserveBuildInfo()
        {
            return new Measurement<int>[]
            {
                new Measurement<int>(1,
                    new KeyValuePair<string, object?>(TelemetryNames.AttrVersion, _Version),
                    new KeyValuePair<string, object?>(TelemetryNames.AttrRuntime, Environment.Version.ToString()),
                    new KeyValuePair<string, object?>(TelemetryNames.AttrOsType, OsType()))
            };
        }

        private static IEnumerable<Measurement<int>> ObserveConfigCacheSeconds()
        {
            int value = Volatile.Read(ref _ConfigMetadataCacheSeconds);
            if (value < 0) return Array.Empty<Measurement<int>>();
            return new Measurement<int>[] { new Measurement<int>(value) };
        }

        private static IEnumerable<Measurement<int>> ObserveConfigDrives()
        {
            int total = Volatile.Read(ref _ConfigDrives);
            int auto = Volatile.Read(ref _ConfigAutoMountDrives);
            if (total < 0) return Array.Empty<Measurement<int>>();

            return new Measurement<int>[]
            {
                new Measurement<int>(auto, new KeyValuePair<string, object?>(TelemetryNames.AttrAutoMount, "true")),
                new Measurement<int>(total - auto, new KeyValuePair<string, object?>(TelemetryNames.AttrAutoMount, "false"))
            };
        }

        private static string OsType()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "darwin";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
            return "other";
        }

        private static string ResolveVersion()
        {
            try
            {
                Assembly assembly = typeof(S3DriveTelemetry).Assembly;
                AssemblyInformationalVersionAttribute? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string? version = informational?.InformationalVersion;
                if (string.IsNullOrEmpty(version)) version = assembly.GetName().Version?.ToString();
                if (string.IsNullOrEmpty(version)) return "0.0.0";

                int plus = version.IndexOf('+');
                return plus > 0 ? version.Substring(0, plus) : version;
            }
            catch (Exception)
            {
                return "0.0.0";
            }
        }
    }
}
