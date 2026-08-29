namespace S3Drive.Core.Concurrency
{
    using System;
    using System.IO;

    /// <summary>
    /// Holds an exclusively opened lock file. Disposing the handle releases the lock.
    /// </summary>
    public sealed class FileLockHandle : IDisposable
    {
        private FileStream? _Stream;

        /// <summary>
        /// Initializes a new instance wrapping an exclusively opened file stream.
        /// </summary>
        /// <param name="stream">The exclusively opened stream. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
        public FileLockHandle(FileStream stream)
        {
            _Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        /// <summary>
        /// Releases the lock by disposing the underlying file stream.
        /// </summary>
        public void Dispose()
        {
            if (_Stream != null)
            {
                _Stream.Dispose();
                _Stream = null;
            }
        }
    }
}
