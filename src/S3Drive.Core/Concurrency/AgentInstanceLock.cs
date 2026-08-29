namespace S3Drive.Core.Concurrency
{
    using System;
    using System.IO;

    /// <summary>
    /// Provides a cross-process single-instance guard for the tray agent using an exclusively
    /// opened lock file. The first process to acquire the lock holds it for its lifetime; a
    /// second acquisition fails until the first releases it.
    /// </summary>
    public static class AgentInstanceLock
    {
        /// <summary>
        /// The lock file name within the state directory.
        /// </summary>
        public const string LockFileName = "agent.lock";

        /// <summary>
        /// Attempts to acquire the single-instance lock.
        /// </summary>
        /// <param name="stateDirectory">The state directory holding the lock file. Cannot be null or empty.</param>
        /// <returns>A held <see cref="FileLockHandle"/> on success, or null if another instance holds it.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="stateDirectory"/> is null or empty.</exception>
        public static FileLockHandle? TryAcquire(string stateDirectory)
        {
            if (string.IsNullOrEmpty(stateDirectory)) throw new ArgumentException("State directory must be provided.", nameof(stateDirectory));

            Directory.CreateDirectory(stateDirectory);
            string lockPath = Path.Combine(stateDirectory, LockFileName);

            try
            {
                FileStream stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new FileLockHandle(stream);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Determines whether an agent is currently running by probing the lock.
        /// </summary>
        /// <param name="stateDirectory">The state directory holding the lock file. Cannot be null or empty.</param>
        /// <returns>True if another process holds the lock; otherwise false.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="stateDirectory"/> is null or empty.</exception>
        public static bool IsRunning(string stateDirectory)
        {
            FileLockHandle? handle = TryAcquire(stateDirectory);
            if (handle == null) return true;
            handle.Dispose();
            return false;
        }
    }
}
