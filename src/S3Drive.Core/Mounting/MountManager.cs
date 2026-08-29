namespace S3Drive.Core.Mounting
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using DokanNet;
    using DokanNet.Logging;
    using S3Drive.Core.Concurrency;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.FileSystem;
    using S3Drive.Core.Ipc;
    using S3Drive.Core.Security;
    using S3Drive.Core.Storage;

    /// <summary>
    /// Owns the set of active mounts. Each mount exposes one bucket as one drive letter; multiple
    /// drives can be mounted at once. S3Drive does not manage network sharing — a mounted drive can
    /// be shared from Windows Explorer like any other volume.
    /// </summary>
    public sealed class MountManager : IAsyncDisposable
    {
        private readonly object _Sync = new object();
        private readonly Dictionary<string, MountEntry> _Mounts = new Dictionary<string, MountEntry>(StringComparer.Ordinal);
        private readonly S3DrivePaths _Paths;
        private readonly CredentialProtector _Protector;
        private int _MetadataCacheSeconds = 5;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="paths">The path resolver. Cannot be null.</param>
        /// <param name="protector">The credential protector used to decrypt secret keys. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public MountManager(S3DrivePaths paths, CredentialProtector protector)
        {
            _Paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _Protector = protector ?? throw new ArgumentNullException(nameof(protector));
        }

        /// <summary>
        /// Raised whenever the set of mounts or their state changes.
        /// </summary>
        public event Action<AgentStatus>? StatusChanged;

        /// <summary>
        /// The metadata cache lifetime, in seconds, applied to newly mounted drives. Minimum 0,
        /// maximum 3600. Defaults to 5.
        /// </summary>
        public int MetadataCacheSeconds
        {
            get { return _MetadataCacheSeconds; }
            set { _MetadataCacheSeconds = Math.Clamp(value, 0, 3600); }
        }

        /// <summary>
        /// The identifiers of currently mounted drives.
        /// </summary>
        /// <returns>A snapshot of mounted drive identifiers. Never null.</returns>
        public IReadOnlyList<string> MountedIds()
        {
            lock (_Sync)
            {
                return new List<string>(_Mounts.Keys);
            }
        }

        /// <summary>
        /// Mounts a drive for the given profile. If the drive is already mounted, this is a no-op.
        /// </summary>
        /// <param name="profile">The drive profile. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> is null.</exception>
        /// <exception cref="PlatformNotSupportedException">Thrown when mounting is attempted off Windows.</exception>
        public async Task MountAsync(DriveProfile profile, CancellationToken token)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Mounting requires Windows and the Dokan driver.");

            lock (_Sync)
            {
                if (_Mounts.ContainsKey(profile.Id)) return;
            }

            string secret = await _Protector.UnprotectAsync(profile.SecretKeyEncrypted, token).ConfigureAwait(false);

            string stagingDirectory = _Paths.CacheDirectoryFor(profile.Id);
            ResetDirectory(stagingDirectory);

            BlobS3Store store = new BlobS3Store(profile, secret);
            MetadataCache cache = new MetadataCache(_MetadataCacheSeconds);
            ObjectLocks locks = new ObjectLocks();
            CancellationTokenSource cts = new CancellationTokenSource();

            string label = string.IsNullOrEmpty(profile.Name) ? "S3Drive" : profile.Name;
            S3DriveFileSystem fileSystem = new S3DriveFileSystem(store, cache, locks, stagingDirectory, label, cts.Token);

            MountEntry entry = new MountEntry(profile, store, cts, stagingDirectory);

            try
            {
                string mountPoint = ToMountPoint(profile.DriveLetter);
                Dokan dokan = new Dokan(new NullLogger());
                DokanInstanceBuilder builder = new DokanInstanceBuilder(dokan)
                    .ConfigureOptions(options =>
                    {
                        options.MountPoint = mountPoint;
                        options.Options = DokanOptions.MountManager | DokanOptions.EnableNetworkUnmount;
                    });

                DokanInstance instance = builder.Build(fileSystem);

                entry.Dokan = dokan;
                entry.Instance = instance;
                entry.Status.MountState = DriveMountStateEnum.Mounted;
                entry.Status.DriveLetter = mountPoint;
            }
            catch (Exception ex)
            {
                entry.Status.MountState = DriveMountStateEnum.Failed;
                entry.Status.LastError = ex.Message;
                cts.Cancel();
                cts.Dispose();
                store.Dispose();
                RaiseStatus();
                throw;
            }

            lock (_Sync)
            {
                _Mounts[profile.Id] = entry;
            }

            RaiseStatus();
        }

        /// <summary>
        /// Unmounts a drive. Unmounting an unknown drive is a no-op.
        /// </summary>
        /// <param name="driveId">The drive identifier. Cannot be null or empty.</param>
        /// <param name="token">A cancellation token.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="driveId"/> is null or empty.</exception>
        public Task UnmountAsync(string driveId, CancellationToken token)
        {
            if (string.IsNullOrEmpty(driveId)) throw new ArgumentException("Drive id must be provided.", nameof(driveId));

            MountEntry? entry;
            lock (_Sync)
            {
                if (!_Mounts.TryGetValue(driveId, out entry)) return Task.CompletedTask;
                _Mounts.Remove(driveId);
            }

            entry.Status.MountState = DriveMountStateEnum.Unmounting;
            RaiseStatus();

            try
            {
                entry.Cts.Cancel();
                if (OperatingSystem.IsWindows())
                {
                    entry.Instance?.Dispose();
                    entry.Dokan?.Dispose();
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                entry.Store.Dispose();
                entry.Cts.Dispose();
                SafeDeleteDirectory(entry.StagingDirectory);
            }

            RaiseStatus();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Unmounts every mounted drive.
        /// </summary>
        /// <param name="token">A cancellation token.</param>
        public async Task UnmountAllAsync(CancellationToken token)
        {
            IReadOnlyList<string> ids = MountedIds();
            foreach (string id in ids)
            {
                await UnmountAsync(id, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Builds a snapshot of the current agent status.
        /// </summary>
        /// <returns>The current status. Never null.</returns>
        public AgentStatus BuildStatus()
        {
            AgentStatus status = new AgentStatus
            {
                UpdatedUtc = DateTime.UtcNow,
                ProcessId = Environment.ProcessId
            };

            lock (_Sync)
            {
                foreach (MountEntry entry in _Mounts.Values)
                {
                    status.Drives.Add(CloneStatus(entry.Status));
                }
            }

            return status;
        }

        /// <summary>
        /// Unmounts everything and releases resources.
        /// </summary>
        /// <returns>A task that completes when teardown finishes.</returns>
        public async ValueTask DisposeAsync()
        {
            await UnmountAllAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private void RaiseStatus()
        {
            Action<AgentStatus>? handler = StatusChanged;
            if (handler == null) return;

            try
            {
                handler(BuildStatus());
            }
            catch (Exception)
            {
            }
        }

        private static DriveStatus CloneStatus(DriveStatus source)
        {
            return new DriveStatus
            {
                DriveId = source.DriveId,
                Name = source.Name,
                MountState = source.MountState,
                DriveLetter = source.DriveLetter,
                LastError = source.LastError
            };
        }

        private static string ToMountPoint(string driveLetter)
        {
            string trimmed = driveLetter.Trim().TrimEnd('\\', ':', ' ');
            if (trimmed.Length == 0) throw new ArgumentException("Drive letter must be provided.", nameof(driveLetter));
            char letter = char.ToUpperInvariant(trimmed[0]);
            return letter + ":\\";
        }

        private static void ResetDirectory(string path)
        {
            SafeDeleteDirectory(path);
            Directory.CreateDirectory(path);
        }

        private static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception)
            {
            }
        }

        private sealed class MountEntry
        {
            public MountEntry(DriveProfile profile, BlobS3Store store, CancellationTokenSource cts, string stagingDirectory)
            {
                Profile = profile;
                Store = store;
                Cts = cts;
                StagingDirectory = stagingDirectory;
                Status = new DriveStatus
                {
                    DriveId = profile.Id,
                    Name = profile.Name,
                    MountState = DriveMountStateEnum.Mounting
                };
            }

            public DriveProfile Profile { get; }

            public BlobS3Store Store { get; }

            public CancellationTokenSource Cts { get; }

            public string StagingDirectory { get; }

            public DriveStatus Status { get; }

            public Dokan? Dokan { get; set; }

            public DokanInstance? Instance { get; set; }
        }
    }
}
