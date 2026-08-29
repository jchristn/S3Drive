namespace S3Drive.Agent
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Diagnostics;
    using S3Drive.Core.Ipc;
    using S3Drive.Core.Mounting;
    using S3Drive.Core.Security;

    /// <summary>
    /// The background host that owns the mount manager, processes commands from the TUI, and
    /// publishes status. Runs independently of any TUI.
    /// </summary>
    internal sealed class AgentHost
    {
        private readonly S3DrivePaths _Paths;
        private readonly SettingsManager _SettingsManager;
        private readonly MountManager _Mounts;
        private CancellationTokenSource? _Cts;
        private S3DriveSettings _Settings = new S3DriveSettings();

        /// <summary>
        /// Raised when the published status changes.
        /// </summary>
        public event Action<AgentStatus>? StatusChanged;

        /// <summary>
        /// Initializes a new host.
        /// </summary>
        /// <param name="paths">The path resolver.</param>
        public AgentHost(S3DrivePaths paths)
        {
            _Paths = paths;
            _SettingsManager = new SettingsManager(paths);
            CredentialProtector protector = new CredentialProtector(paths.MachineKeyFile);
            _Mounts = new MountManager(paths, protector);
            _Mounts.StatusChanged += _ => PublishStatusFireAndForget();
        }

        /// <summary>
        /// Starts the host loop on a background task.
        /// </summary>
        public void Start()
        {
            _Cts = new CancellationTokenSource();
            CancellationToken token = _Cts.Token;
            _ = Task.Run(() => RunAsync(token));
        }

        /// <summary>
        /// Stops the loop and unmounts everything.
        /// </summary>
        public void Stop()
        {
            _Cts?.Cancel();
            try
            {
                _Mounts.UnmountAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Launches the TUI.
        /// </summary>
        public void LaunchTui()
        {
            TerminalLauncher.LaunchTui();
        }

        /// <summary>
        /// The current full status (all configured drives).
        /// </summary>
        /// <returns>The status snapshot.</returns>
        public AgentStatus CurrentStatus()
        {
            return BuildFullStatus();
        }

        /// <summary>
        /// Mounts a drive by id (invoked from the tray).
        /// </summary>
        /// <param name="driveId">The drive id.</param>
        /// <returns>A task.</returns>
        public async Task MountAsync(string driveId)
        {
            DriveProfile? profile = Find(driveId);
            if (profile == null) return;
            try
            {
                await _Mounts.MountAsync(profile, Token()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                S3DriveLog.Error("Mount failed for " + profile.Name + ": " + ex.Message);
            }

            await PublishStatusAsync(Token()).ConfigureAwait(false);
        }

        /// <summary>
        /// Unmounts a drive by id (invoked from the tray).
        /// </summary>
        /// <param name="driveId">The drive id.</param>
        /// <returns>A task.</returns>
        public async Task UnmountAsync(string driveId)
        {
            await _Mounts.UnmountAsync(driveId, Token()).ConfigureAwait(false);
            await PublishStatusAsync(Token()).ConfigureAwait(false);
        }

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                _Settings = await _SettingsManager.LoadAsync(token).ConfigureAwait(false);
                _Mounts.MetadataCacheSeconds = _Settings.MetadataCacheSeconds;
                await AutoMountAsync(token).ConfigureAwait(false);
                await PublishStatusAsync(token).ConfigureAwait(false);

                while (!token.IsCancellationRequested)
                {
                    await DrainCommandsAsync(token).ConfigureAwait(false);
                    try
                    {
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                S3DriveLog.WriteCrash(ex, "agent host loop");
            }
        }

        private async Task AutoMountAsync(CancellationToken token)
        {
            foreach (DriveProfile profile in _Settings.Drives)
            {
                if (!profile.AutoMount) continue;
                try
                {
                    await _Mounts.MountAsync(profile, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    S3DriveLog.Error("Auto-mount failed for " + profile.Name + ": " + ex.Message);
                }
            }
        }

        private async Task DrainCommandsAsync(CancellationToken token)
        {
            foreach (string file in CommandChannel.ListPending(_Paths))
            {
                if (!CommandChannel.TryRead(file, out AgentCommand? command) || command == null)
                {
                    TryDelete(file);
                    continue;
                }

                try
                {
                    await ExecuteAsync(command, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    S3DriveLog.Error("Command " + command.CommandType + " failed: " + ex.Message);
                }
                finally
                {
                    TryDelete(file);
                }
            }
        }

        private async Task ExecuteAsync(AgentCommand command, CancellationToken token)
        {
            switch (command.CommandType)
            {
                case AgentCommandTypeEnum.Reload:
                    _Settings = await _SettingsManager.LoadAsync(token).ConfigureAwait(false);
                    _Mounts.MetadataCacheSeconds = _Settings.MetadataCacheSeconds;
                    await AutoMountAsync(token).ConfigureAwait(false);
                    break;
                case AgentCommandTypeEnum.Mount:
                    DriveProfile? mountProfile = Find(command.DriveId);
                    if (mountProfile != null) await _Mounts.MountAsync(mountProfile, token).ConfigureAwait(false);
                    break;
                case AgentCommandTypeEnum.Unmount:
                    if (!string.IsNullOrEmpty(command.DriveId)) await _Mounts.UnmountAsync(command.DriveId, token).ConfigureAwait(false);
                    break;
                case AgentCommandTypeEnum.MountAll:
                    foreach (DriveProfile profile in _Settings.Drives)
                    {
                        await _Mounts.MountAsync(profile, token).ConfigureAwait(false);
                    }

                    break;
                case AgentCommandTypeEnum.UnmountAll:
                    await _Mounts.UnmountAllAsync(token).ConfigureAwait(false);
                    break;
                default:
                    break;
            }

            await PublishStatusAsync(token).ConfigureAwait(false);
        }

        private AgentStatus BuildFullStatus()
        {
            AgentStatus mountStatus = _Mounts.BuildStatus();
            Dictionary<string, DriveStatus> byId = new Dictionary<string, DriveStatus>(StringComparer.Ordinal);
            foreach (DriveStatus status in mountStatus.Drives)
            {
                byId[status.DriveId] = status;
            }

            AgentStatus full = new AgentStatus
            {
                ProcessId = Environment.ProcessId,
                UpdatedUtc = DateTime.UtcNow
            };

            foreach (DriveProfile profile in _Settings.Drives)
            {
                if (byId.TryGetValue(profile.Id, out DriveStatus? existing))
                {
                    full.Drives.Add(existing);
                }
                else
                {
                    full.Drives.Add(new DriveStatus
                    {
                        DriveId = profile.Id,
                        Name = profile.Name,
                        MountState = DriveMountStateEnum.Unmounted
                    });
                }
            }

            return full;
        }

        private async Task PublishStatusAsync(CancellationToken token)
        {
            AgentStatus status = BuildFullStatus();
            try
            {
                await StatusStore.WriteAsync(_Paths, status, token).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            Action<AgentStatus>? handler = StatusChanged;
            if (handler != null)
            {
                try
                {
                    handler(status);
                }
                catch (Exception)
                {
                }
            }
        }

        private void PublishStatusFireAndForget()
        {
            _ = Task.Run(() => PublishStatusAsync(Token()));
        }

        private DriveProfile? Find(string? driveId)
        {
            if (string.IsNullOrEmpty(driveId)) return null;
            foreach (DriveProfile profile in _Settings.Drives)
            {
                if (string.Equals(profile.Id, driveId, StringComparison.Ordinal)) return profile;
            }

            return null;
        }

        private CancellationToken Token()
        {
            return _Cts?.Token ?? CancellationToken.None;
        }

        private static void TryDelete(string file)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception)
            {
            }
        }
    }
}
