namespace S3Drive.Core.Configuration
{
    /// <summary>
    /// Logging configuration for S3Drive.
    /// </summary>
    public class LoggingSettings
    {
        /// <summary>
        /// When true, log lines are also written to the console. Defaults to false so the TUI
        /// display is never corrupted by log output.
        /// </summary>
        public bool ConsoleLogging { get; set; } = false;

        /// <summary>
        /// When true, log lines are written to dated files in the logs directory. Defaults to true.
        /// </summary>
        public bool FileLogging { get; set; } = true;
    }
}
