namespace S3Drive.Tui
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// Copies text to the system clipboard by piping it to the platform clipboard tool
    /// (clip on Windows, pbcopy on macOS, xclip on Linux).
    /// </summary>
    internal static class Clipboard
    {
        /// <summary>
        /// Attempts to copy text to the clipboard.
        /// </summary>
        /// <param name="text">The text to copy.</param>
        /// <returns>True when the copy succeeded; otherwise false.</returns>
        public static bool TryCopy(string text)
        {
            try
            {
                ProcessStartInfo info = BuildStartInfo();
                info.RedirectStandardInput = true;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;

                using (Process? process = Process.Start(info))
                {
                    if (process == null) return false;
                    process.StandardInput.Write(text ?? string.Empty);
                    process.StandardInput.Close();
                    process.WaitForExit(3000);
                    return process.HasExited && process.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static ProcessStartInfo BuildStartInfo()
        {
            if (OperatingSystem.IsWindows()) return new ProcessStartInfo("clip");
            if (OperatingSystem.IsMacOS()) return new ProcessStartInfo("pbcopy");

            ProcessStartInfo info = new ProcessStartInfo("xclip");
            info.ArgumentList.Add("-selection");
            info.ArgumentList.Add("clipboard");
            return info;
        }
    }
}
