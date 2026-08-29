namespace S3Drive.Tui
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using S3Drive.Core.Concurrency;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Diagnostics;

    /// <summary>
    /// Starts the tray agent if it is not already running.
    /// </summary>
    internal static class AgentLauncher
    {
        /// <summary>
        /// Ensures the agent is running, starting it detached when absent.
        /// </summary>
        /// <param name="paths">The path resolver.</param>
        public static void EnsureRunning(S3DrivePaths paths)
        {
            try
            {
                if (AgentInstanceLock.IsRunning(paths.StateDirectory)) return;

                string? executablePath = ResolveAgentPath();
                if (executablePath == null)
                {
                    S3DriveLog.Warn("Could not locate the S3Drive agent executable.");
                    return;
                }

                ProcessStartInfo info = new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = true
                };
                Process.Start(info);
                S3DriveLog.Info("Started the S3Drive agent.");
            }
            catch (Exception ex)
            {
                S3DriveLog.Error("Failed to start the agent: " + ex.Message);
            }
        }

        private static string? ResolveAgentPath()
        {
            string executableName = OperatingSystem.IsWindows() ? "S3Drive.Agent.exe" : "S3Drive.Agent";
            string baseDirectory = AppContext.BaseDirectory;

            string beside = Path.Combine(baseDirectory, executableName);
            if (File.Exists(beside)) return beside;

            string trimmed = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string tuiSegment = Path.DirectorySeparatorChar + "S3Drive.Tui" + Path.DirectorySeparatorChar;
            string agentSegment = Path.DirectorySeparatorChar + "S3Drive.Agent" + Path.DirectorySeparatorChar;

            int index = trimmed.LastIndexOf(tuiSegment, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                string siblingDirectory = trimmed.Substring(0, index) + agentSegment + trimmed.Substring(index + tuiSegment.Length);
                string sibling = Path.Combine(siblingDirectory, executableName);
                if (File.Exists(sibling)) return sibling;
            }

            return null;
        }
    }
}
