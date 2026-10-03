namespace S3Drive.Core.Concurrency
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Padlocks;
    using S3Drive.Core.Telemetry;

    /// <summary>
    /// Coarse per-object named locks that serialize access to a single object key within a
    /// drive. Each mounted drive owns one instance, so keys are naturally scoped per drive. The
    /// design prioritizes consistency and coherency of the backing S3 data over concurrency:
    /// conflicting operations on the same object run one at a time. Backed by Padlock.
    /// </summary>
    public sealed class ObjectLocks
    {
        private readonly Padlock<string> _Padlock = new Padlock<string>(1, 64);
        private readonly string _Drive;

        /// <summary>
        /// Initializes a new lock set.
        /// </summary>
        public ObjectLocks() : this(null)
        {
        }

        /// <summary>
        /// Initializes a new lock set whose telemetry is labeled with a drive letter.
        /// </summary>
        /// <param name="driveLetter">The drive letter reported as the <c>s3drive.drive</c> telemetry label. Null or invalid values report <c>unknown</c>.</param>
        public ObjectLocks(string? driveLetter)
        {
            _Drive = S3DriveTelemetry.NormalizeDrive(driveLetter);
        }

        /// <summary>
        /// Acquires the lock for a key, blocking until it is available.
        /// </summary>
        /// <param name="key">The object key. Cannot be null.</param>
        /// <returns>A disposable handle; disposing it releases the lock.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
        public IDisposable Acquire(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            bool contended = _Padlock.IsLocked(key);
            long start = Stopwatch.GetTimestamp();
            using (Activity? activity = StartWaitSpan(contended))
            {
                IDisposable handle;
                try
                {
                    handle = _Padlock.Lock(key);
                }
                catch (Exception ex)
                {
                    S3DriveTelemetry.RecordLockWait(_Drive, Stopwatch.GetElapsedTime(start).TotalSeconds, S3DriveTelemetry.OutcomeFor(ex), contended);
                    S3DriveTelemetry.AddException(activity, ex);
                    activity?.SetStatus(ActivityStatusCode.Error, S3DriveTelemetry.ErrorType(ex));
                    throw;
                }

                S3DriveTelemetry.RecordLockWait(_Drive, Stopwatch.GetElapsedTime(start).TotalSeconds, TelemetryNames.OutcomeSuccess, contended);
                return new TrackedLockHandle(handle, _Drive);
            }
        }

        /// <summary>
        /// Acquires the lock for a key asynchronously.
        /// </summary>
        /// <param name="key">The object key. Cannot be null.</param>
        /// <param name="token">A cancellation token that bounds the wait.</param>
        /// <returns>A disposable handle; disposing it releases the lock.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
        public ValueTask<IDisposable> AcquireAsync(string key, CancellationToken token = default)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return AcquireTrackedAsync(key, token);
        }

        /// <summary>
        /// Indicates whether a key is currently locked.
        /// </summary>
        /// <param name="key">The object key. Cannot be null.</param>
        /// <returns>True if the key is held; otherwise false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
        public bool IsLocked(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return _Padlock.IsLocked(key);
        }

        private async ValueTask<IDisposable> AcquireTrackedAsync(string key, CancellationToken token)
        {
            bool contended = _Padlock.IsLocked(key);
            long start = Stopwatch.GetTimestamp();
            using (Activity? activity = StartWaitSpan(contended))
            {
                IDisposable handle;
                try
                {
                    handle = await _Padlock.LockAsync(key, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    S3DriveTelemetry.RecordLockWait(_Drive, Stopwatch.GetElapsedTime(start).TotalSeconds, S3DriveTelemetry.OutcomeFor(ex), contended);
                    S3DriveTelemetry.AddException(activity, ex);
                    if (!(ex is OperationCanceledException)) activity?.SetStatus(ActivityStatusCode.Error, S3DriveTelemetry.ErrorType(ex));
                    throw;
                }

                S3DriveTelemetry.RecordLockWait(_Drive, Stopwatch.GetElapsedTime(start).TotalSeconds, TelemetryNames.OutcomeSuccess, contended);
                return new TrackedLockHandle(handle, _Drive);
            }
        }

        private Activity? StartWaitSpan(bool contended)
        {
            Activity? activity = S3DriveTelemetry.StartActivity(TelemetryNames.SpanStagePrefix + TelemetryNames.StageLockWait, ActivityKind.Internal);
            if (activity != null)
            {
                activity.SetTag(TelemetryNames.AttrDrive, _Drive);
                activity.SetTag(TelemetryNames.AttrStage, TelemetryNames.StageLockWait);
                activity.SetTag("s3drive.lock.contended", contended);
            }

            return activity;
        }
    }
}
