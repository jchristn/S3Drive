namespace Test.Automated
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Ipc;
    using S3Drive.Core.Security;

    /// <summary>
    /// Helpers used by the end-to-end agent test: writing a config with an auto-mount drive and
    /// sending commands to a running agent through the command channel. Both use the current
    /// S3DRIVE_HOME so tests can run against an isolated configuration directory.
    /// </summary>
    internal static class AgentControl
    {
        /// <summary>
        /// Writes an s3drive.json with a single auto-mount drive built from the storage config,
        /// encrypting the secret with the machine key under S3DRIVE_HOME.
        /// </summary>
        /// <param name="config">The storage configuration.</param>
        /// <param name="driveLetter">The drive letter to mount.</param>
        /// <returns>Zero on success; one on failure.</returns>
        public static async Task<int> WriteConfigAsync(StorageTestConfig config, string? driveLetter)
        {
            if (!config.Enabled)
            {
                Console.WriteLine("Config write requires an endpoint (--endpoint/--access-key/--secret-key/--bucket).");
                return 1;
            }

            S3DrivePaths paths = new S3DrivePaths();
            paths.EnsureDirectories();

            CredentialProtector protector = new CredentialProtector(paths.MachineKeyFile);
            string encrypted = await protector.ProtectAsync(config.Secret).ConfigureAwait(false);

            DriveProfile profile = config.Profile;
            profile.Id = "drv_test";
            profile.Name = "Less3 test";
            profile.SecretKeyEncrypted = encrypted;
            profile.AutoMount = true;
            if (!string.IsNullOrEmpty(driveLetter))
            {
                string cleaned = driveLetter.TrimEnd(':', '\\', ' ');
                if (cleaned.Length > 0) profile.DriveLetter = char.ToUpperInvariant(cleaned[0]) + ":";
            }

            S3DriveSettings settings = new S3DriveSettings();
            settings.Drives.Add(profile);
            await new SettingsManager(paths).SaveAsync(settings).ConfigureAwait(false);

            Console.WriteLine("Wrote config to " + paths.ConfigFile + " with drive " + profile.DriveLetter + " (auto-mount).");
            return 0;
        }

        /// <summary>
        /// Sends a command to the running agent through the command channel.
        /// </summary>
        /// <param name="type">The command type (mount, unmount, mount-all, unmount-all, share, unshare, reload).</param>
        /// <param name="driveId">The target drive id, when applicable.</param>
        /// <returns>Zero.</returns>
        public static async Task<int> SendCommandAsync(string type, string? driveId)
        {
            AgentCommandTypeEnum parsed = ParseType(type);
            S3DrivePaths paths = new S3DrivePaths();
            AgentCommand command = new AgentCommand { CommandType = parsed, DriveId = driveId };
            await CommandChannel.SendAsync(paths, command, CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine("Sent command " + parsed + (driveId != null ? " for " + driveId : string.Empty) + ".");
            return 0;
        }

        private static AgentCommandTypeEnum ParseType(string type)
        {
            switch (type.ToLowerInvariant())
            {
                case "mount": return AgentCommandTypeEnum.Mount;
                case "unmount": return AgentCommandTypeEnum.Unmount;
                case "mount-all": return AgentCommandTypeEnum.MountAll;
                case "unmount-all": return AgentCommandTypeEnum.UnmountAll;
                case "share": return AgentCommandTypeEnum.Share;
                case "unshare": return AgentCommandTypeEnum.Unshare;
                default: return AgentCommandTypeEnum.Reload;
            }
        }
    }
}
