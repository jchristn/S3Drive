namespace S3Drive.Core.Concurrency
{
    using System;
    using System.Threading;
    using S3Drive.Core.Telemetry;

    /// <summary>
    /// Wraps a held per-object lock so its release is reflected in the <c>s3drive.lock.held</c>
    /// gauge. Disposing the handle releases the underlying lock exactly once.
    /// </summary>
    internal sealed class TrackedLockHandle : IDisposable
    {
        private readonly string _Drive;
        private IDisposable? _Inner;

        internal TrackedLockHandle(IDisposable inner, string drive)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _Drive = drive;
            S3DriveTelemetry.RecordLockHeld(_Drive, 1);
        }

        public void Dispose()
        {
            IDisposable? inner = Interlocked.Exchange(ref _Inner, null);
            if (inner == null) return;

            try
            {
                inner.Dispose();
            }
            finally
            {
                S3DriveTelemetry.RecordLockHeld(_Drive, -1);
            }
        }
    }
}
