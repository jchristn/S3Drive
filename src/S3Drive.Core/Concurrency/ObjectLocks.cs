namespace S3Drive.Core.Concurrency
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Padlocks;

    /// <summary>
    /// Coarse per-object named locks that serialize access to a single object key within a
    /// drive. Each mounted drive owns one instance, so keys are naturally scoped per drive. The
    /// design prioritizes consistency and coherency of the backing S3 data over concurrency:
    /// conflicting operations on the same object run one at a time. Backed by Padlock.
    /// </summary>
    public sealed class ObjectLocks
    {
        private readonly Padlock<string> _Padlock = new Padlock<string>(1, 64);

        /// <summary>
        /// Acquires the lock for a key, blocking until it is available.
        /// </summary>
        /// <param name="key">The object key. Cannot be null.</param>
        /// <returns>A disposable handle; disposing it releases the lock.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
        public IDisposable Acquire(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return _Padlock.Lock(key);
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
            return _Padlock.LockAsync(key, token);
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
    }
}
