namespace S3Drive.Agent
{
    using System;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// Launches the S3Drive TUI as an independent process.
    /// </summary>
    internal static class TerminalLauncher
    {
        /// <summary>
        /// Launches the TUI, discovering the executable beside the agent or in the sibling
        /// development build directory.
        /// </summary>
        public static void LaunchTui()
        {
            string? executablePath = ResolveTuiPath();
            if (executablePath == null) return;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    ProcessStartInfo info = new ProcessStartInfo(executablePath)
                    {
                        UseShellExecute = true
                    };
                    Process.Start(info);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", "-a Terminal \"" + executablePath + "\"");
                }
                else
                {
                    ProcessStartInfo info = new ProcessStartInfo
                    {
                        FileName = "x-terminal-emulator",
                        Arguments = "-e \"" + executablePath + "\"",
                        UseShellExecute = false
                    };
                    Process.Start(info);
                }
            }
            catch (Exception)
            {
            }
        }

        private static string? ResolveTuiPath()
        {
            string executableName = OperatingSystem.IsWindows() ? "S3Drive.Tui.exe" : "S3Drive.Tui";
            string baseDirectory = AppContext.BaseDirectory;

            string beside = Path.Combine(baseDirectory, executableName);
            if (File.Exists(beside)) return beside;

            string trimmed = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string agentSegment = Path.DirectorySeparatorChar + "S3Drive.Agent" + Path.DirectorySeparatorChar;
            string tuiSegment = Path.DirectorySeparatorChar + "S3Drive.Tui" + Path.DirectorySeparatorChar;

            int index = trimmed.LastIndexOf(agentSegment, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                string siblingDirectory = trimmed.Substring(0, index) + tuiSegment + trimmed.Substring(index + agentSegment.Length);
                string sibling = Path.Combine(siblingDirectory, executableName);
                if (File.Exists(sibling)) return sibling;
            }

            return null;
        }
    }
}
