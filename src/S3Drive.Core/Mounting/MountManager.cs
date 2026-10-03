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
    using S3Drive.Core.Diagnostics;
    using S3Drive.Core.FileSystem;
    using S3Drive.Core.Ipc;
    using S3Drive.Core.Security;
    using S3Drive.Core.Storage;
    using S3Drive.Core.Telemetry;

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
            S3DriveTelemetry.RegisterMountManager(this);
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

            using (TelemetryScope scope = S3DriveTelemetry.StartMount(TelemetryNames.MountOperationMount, S3DriveTelemetry.NormalizeDrive(profile.DriveLetter), profile.Id))
            {
                try
                {
                    bool mounted = await MountCoreAsync(profile, token).ConfigureAwait(false);
                    scope.Complete(mounted ? TelemetryNames.OutcomeSuccess : TelemetryNames.OutcomeSkipped);
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        private async Task<bool> MountCoreAsync(DriveProfile profile, CancellationToken token)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Mounting requires Windows and the Dokan driver.");

            lock (_Sync)
            {
                if (_Mounts.ContainsKey(profile.Id)) return false;
            }

            string secret;
            using (TelemetryScope stage = S3DriveTelemetry.StartMountStage(TelemetryNames.StageDecryptCredentials))
            {
                try
                {
                    secret = await _Protector.UnprotectAsync(profile.SecretKeyEncrypted, token).ConfigureAwait(false);
                    stage.Complete(TelemetryNames.OutcomeSuccess);
                }
                catch (Exception ex)
                {
                    stage.Fail(ex);
                    throw;
                }
            }

            string stagingDirectory = _Paths.CacheDirectoryFor(profile.Id);
            RunMountStage(TelemetryNames.StagePrepareStaging, () => ResetDirectory(stagingDirectory));

            BlobS3Store? built = null;
            RunMountStage(TelemetryNames.StageBuildStore, () => built = new BlobS3Store(profile, secret));
            BlobS3Store store = built!;

            MetadataCache cache = new MetadataCache(_MetadataCacheSeconds, profile.DriveLetter);
            ObjectLocks locks = new ObjectLocks(profile.DriveLetter);
            CancellationTokenSource cts = new CancellationTokenSource();
            InstrumentedS3Store instrumented = new InstrumentedS3Store(store, profile.DriveLetter, profile.Provider, profile.Bucket);

            string label = string.IsNullOrEmpty(profile.Name) ? "S3Drive" : profile.Name;
            S3DriveFileSystem fileSystem = new S3DriveFileSystem(instrumented, cache, locks, stagingDirectory, label, cts.Token, profile.DriveLetter);

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

                DokanInstance instance;
                using (TelemetryScope stage = S3DriveTelemetry.StartMountStage(TelemetryNames.StageDokanMount))
                {
                    try
                    {
                        instance = builder.Build(fileSystem);
                        stage.Complete(TelemetryNames.OutcomeSuccess);
                    }
                    catch (Exception stageException)
                    {
                        stage.Fail(stageException);
                        throw;
                    }
                }

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
            return true;
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

            using (TelemetryScope scope = S3DriveTelemetry.StartMount(TelemetryNames.MountOperationUnmount, S3DriveTelemetry.NormalizeDrive(entry.Profile.DriveLetter), driveId))
            {
                UnmountEntry(entry, scope);
            }

            RaiseStatus();
            return Task.CompletedTask;
        }

        private void UnmountEntry(MountEntry entry, TelemetryScope scope)
        {
            entry.Status.MountState = DriveMountStateEnum.Unmounting;
            RaiseStatus();

            try
            {
                entry.Cts.Cancel();
                if (OperatingSystem.IsWindows())
                {
                    using (TelemetryScope stage = S3DriveTelemetry.StartMountStage(TelemetryNames.StageDokanUnmount))
                    {
                        try
                        {
                            entry.Instance?.Dispose();
                            entry.Dokan?.Dispose();
                            stage.Complete(TelemetryNames.OutcomeSuccess);
                        }
                        catch (Exception stageException)
                        {
                            stage.Fail(stageException);
                            throw;
                        }
                    }
                }

                scope.Complete(TelemetryNames.OutcomeSuccess);
            }
            catch (Exception ex)
            {
                // Teardown continues regardless; the failure is recorded so it is not invisible.
                scope.Fail(ex);
                S3DriveLog.Warn("unmount of " + entry.Profile.Name + " did not complete cleanly: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                entry.Store.Dispose();
                entry.Cts.Dispose();
                SafeDeleteDirectory(entry.StagingDirectory);
            }
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

        private static void RunMountStage(string stage, Action action)
        {
            using (TelemetryScope scope = S3DriveTelemetry.StartMountStage(stage))
            {
                try
                {
                    action();
                    scope.Complete(TelemetryNames.OutcomeSuccess);
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
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
