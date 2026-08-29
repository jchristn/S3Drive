namespace S3Drive.Core.Configuration
{
    using System;
    using System.IO;

    /// <summary>
    /// Resolves the on-disk locations S3Drive uses. The root defaults to a ".s3drive" folder in
    /// the user's home directory and can be overridden with the S3DRIVE_HOME environment
    /// variable or by passing an explicit root to the constructor.
    /// </summary>
    public class S3DrivePaths
    {
        /// <summary>
        /// Name of the environment variable that overrides the root directory.
        /// </summary>
        public const string HomeEnvironmentVariable = "S3DRIVE_HOME";

        private readonly string _Root;

        /// <summary>
        /// Initializes paths using the default root (S3DRIVE_HOME if set, otherwise ~/.s3drive).
        /// </summary>
        public S3DrivePaths()
            : this(ResolveDefaultRoot())
        {
        }

        /// <summary>
        /// Initializes paths using an explicit root directory.
        /// </summary>
        /// <param name="root">The root directory. Cannot be null or empty.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="root"/> is null or empty.</exception>
        public S3DrivePaths(string root)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("Root directory must be provided.", nameof(root));
            _Root = root;
        }

        /// <summary>
        /// The root directory.
        /// </summary>
        public string Root
        {
            get { return _Root; }
        }

        /// <summary>
        /// The configuration file (s3drive.json).
        /// </summary>
        public string ConfigFile
        {
            get { return Path.Combine(_Root, "s3drive.json"); }
        }

        /// <summary>
        /// The log directory.
        /// </summary>
        public string LogDirectory
        {
            get { return Path.Combine(_Root, "logs"); }
        }

        /// <summary>
        /// The crash-log directory.
        /// </summary>
        public string CrashLogDirectory
        {
            get { return Path.Combine(_Root, "crash-logs"); }
        }

        /// <summary>
        /// The state directory (agent lock, status, command channel, machine key).
        /// </summary>
        public string StateDirectory
        {
            get { return Path.Combine(_Root, "state"); }
        }

        /// <summary>
        /// The cache directory (per-mount write staging).
        /// </summary>
        public string CacheDirectory
        {
            get { return Path.Combine(_Root, "cache"); }
        }

        /// <summary>
        /// The single-instance agent lock file.
        /// </summary>
        public string AgentLockFile
        {
            get { return Path.Combine(StateDirectory, "agent.lock"); }
        }

        /// <summary>
        /// The machine-local key file used to encrypt secrets at rest.
        /// </summary>
        public string MachineKeyFile
        {
            get { return Path.Combine(StateDirectory, "dp.key"); }
        }

        /// <summary>
        /// The agent-published status file.
        /// </summary>
        public string StatusFile
        {
            get { return Path.Combine(StateDirectory, "status.json"); }
        }

        /// <summary>
        /// The directory the TUI drops command files into.
        /// </summary>
        public string CommandDirectory
        {
            get { return Path.Combine(StateDirectory, "commands"); }
        }

        /// <summary>
        /// Returns the per-drive cache directory for the given drive identifier.
        /// </summary>
        /// <param name="driveId">The drive identifier. Cannot be null or empty.</param>
        /// <returns>The absolute cache directory path for the drive.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="driveId"/> is null or empty.</exception>
        public string CacheDirectoryFor(string driveId)
        {
            if (string.IsNullOrEmpty(driveId)) throw new ArgumentException("Drive id must be provided.", nameof(driveId));
            return Path.Combine(CacheDirectory, driveId);
        }

        /// <summary>
        /// Creates the root, logs, crash-logs, state, command, and cache directories if missing.
        /// </summary>
        public void EnsureDirectories()
        {
            Directory.CreateDirectory(_Root);
            Directory.CreateDirectory(LogDirectory);
            Directory.CreateDirectory(CrashLogDirectory);
            Directory.CreateDirectory(StateDirectory);
            Directory.CreateDirectory(CommandDirectory);
            Directory.CreateDirectory(CacheDirectory);
        }

        private static string ResolveDefaultRoot()
        {
            string? overridden = Environment.GetEnvironmentVariable(HomeEnvironmentVariable);
            if (!string.IsNullOrEmpty(overridden)) return overridden;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".s3drive");
        }
    }
}
