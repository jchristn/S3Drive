namespace S3Drive.Core.Sharing
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;

    /// <summary>
    /// Creates and removes SMB shares on Windows by invoking the built-in SMB PowerShell cmdlets
    /// (New-SmbShare, Remove-SmbShare, Get-SmbShare). Creating or removing a share requires
    /// administrator privileges; when the process is not elevated, the underlying command fails
    /// and the failure is surfaced to the caller.
    /// </summary>
    public sealed class WindowsSmbShareManager : ISmbShareManager
    {
        /// <inheritdoc />
        public bool IsSupported
        {
            get { return OperatingSystem.IsWindows(); }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the mount path or share name is invalid.</exception>
        /// <exception cref="SmbShareException">Thrown when the share cannot be created.</exception>
        public async Task CreateShareAsync(DriveProfile profile, string mountPath, CancellationToken token)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (string.IsNullOrEmpty(mountPath)) throw new ArgumentException("Mount path must be provided.", nameof(mountPath));

            string? shareName = profile.Share.ShareName;
            if (!ShareNameValidator.IsValid(shareName)) throw new ArgumentException("Share name is missing or invalid.", nameof(profile));

            bool exists = await ShareExistsAsync(shareName!, token).ConfigureAwait(false);
            if (exists) await RemoveShareAsync(shareName!, token).ConfigureAwait(false);

            StringBuilder script = new StringBuilder();
            script.Append("New-SmbShare -Name ");
            script.Append(Quote(shareName!));
            script.Append(" -Path ");
            script.Append(Quote(mountPath));

            List<string> principals = NormalizePrincipals(profile.Share.AllowedPrincipals);
            string principalList = string.Join(",", principals.ConvertAll(Quote));

            if (profile.Share.Access == ShareAccessEnum.ReadWrite)
            {
                script.Append(" -FullAccess ");
            }
            else
            {
                script.Append(" -ReadAccess ");
            }

            script.Append(principalList);

            if (!string.IsNullOrEmpty(profile.Share.Description))
            {
                script.Append(" -Description ");
                script.Append(Quote(profile.Share.Description!));
            }

            CommandResult result = await RunPowerShellAsync(script.ToString(), token).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new SmbShareException("Failed to create SMB share '" + shareName + "'. " + result.StandardError.Trim());
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">Thrown when <paramref name="shareName"/> is null or empty.</exception>
        /// <exception cref="SmbShareException">Thrown when the share cannot be removed.</exception>
        public async Task RemoveShareAsync(string shareName, CancellationToken token)
        {
            if (string.IsNullOrEmpty(shareName)) throw new ArgumentException("Share name must be provided.", nameof(shareName));

            bool exists = await ShareExistsAsync(shareName, token).ConfigureAwait(false);
            if (!exists) return;

            string command = "Remove-SmbShare -Name " + Quote(shareName) + " -Force";
            CommandResult result = await RunPowerShellAsync(command, token).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new SmbShareException("Failed to remove SMB share '" + shareName + "'. " + result.StandardError.Trim());
            }
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">Thrown when <paramref name="shareName"/> is null or empty.</exception>
        public async Task<bool> ShareExistsAsync(string shareName, CancellationToken token)
        {
            if (string.IsNullOrEmpty(shareName)) throw new ArgumentException("Share name must be provided.", nameof(shareName));
            if (!IsSupported) return false;

            string command = "if (Get-SmbShare -Name " + Quote(shareName) + " -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }";
            CommandResult result = await RunPowerShellAsync(command, token).ConfigureAwait(false);
            return result.ExitCode == 0;
        }

        private static List<string> NormalizePrincipals(List<string> principals)
        {
            List<string> normalized = new List<string>();
            foreach (string principal in principals)
            {
                if (!string.IsNullOrWhiteSpace(principal)) normalized.Add(principal.Trim());
            }

            if (normalized.Count == 0) normalized.Add("Authenticated Users");
            return normalized;
        }

        private static string Quote(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private static async Task<CommandResult> RunPowerShellAsync(string command, CancellationToken token)
        {
            ProcessStartInfo info = new ProcessStartInfo("powershell.exe");
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add(command);
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;

            using (Process process = new Process())
            {
                process.StartInfo = info;
                process.Start();

                Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
                Task<string> stderr = process.StandardError.ReadToEndAsync(token);
                await process.WaitForExitAsync(token).ConfigureAwait(false);

                string outText = await stdout.ConfigureAwait(false);
                string errText = await stderr.ConfigureAwait(false);
                return new CommandResult(process.ExitCode, outText, errText);
            }
        }

        private readonly struct CommandResult
        {
            public CommandResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }

            public int ExitCode { get; }

            public string StandardOutput { get; }

            public string StandardError { get; }
        }
    }
}
