namespace S3Drive.Core.Diagnostics
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Threading;
    using SyslogLogging;

    /// <summary>
    /// Static, null-safe logging facade over SyslogLogging. Every method is a no-op before
    /// <see cref="Initialize"/> is called and never throws into the caller. Log lines are also
    /// raised on <see cref="MessageLogged"/> so the TUI can mirror them into an on-screen pane.
    /// </summary>
    public static class S3DriveLog
    {
        private static readonly object _Sync = new object();
        private static LoggingModule? _Log;
        private static string _CrashLogDirectory = string.Empty;

        /// <summary>
        /// Raised for each log line as (severity name, message). Handlers must not throw; a
        /// throwing handler is caught and ignored.
        /// </summary>
        public static event Action<string, string>? MessageLogged;

        /// <summary>
        /// Initializes the logger. Any previously initialized logger is disposed first.
        /// </summary>
        /// <param name="logDirectory">The directory for dated log files. Cannot be null or empty.</param>
        /// <param name="crashLogDirectory">The directory for crash reports. Cannot be null or empty.</param>
        /// <param name="applicationName">The application name recorded on each line. Cannot be null or empty.</param>
        /// <param name="enableConsole">Whether log lines are also written to the console.</param>
        /// <exception cref="ArgumentException">Thrown when a required directory or the application name is null or empty.</exception>
        public static void Initialize(string logDirectory, string crashLogDirectory, string applicationName, bool enableConsole)
        {
            if (string.IsNullOrEmpty(logDirectory)) throw new ArgumentException("Log directory must be provided.", nameof(logDirectory));
            if (string.IsNullOrEmpty(crashLogDirectory)) throw new ArgumentException("Crash log directory must be provided.", nameof(crashLogDirectory));
            if (string.IsNullOrEmpty(applicationName)) throw new ArgumentException("Application name must be provided.", nameof(applicationName));

            lock (_Sync)
            {
                _Log?.Dispose();

                Directory.CreateDirectory(logDirectory);
                Directory.CreateDirectory(crashLogDirectory);
                _CrashLogDirectory = crashLogDirectory;

                string filename = Path.Combine(logDirectory, "s3drive.log");
                LoggingModule module = new LoggingModule(filename, FileLoggingMode.FileWithDate, enableConsole);
                module.Settings.ApplicationName = applicationName;
                module.Settings.MinimumSeverity = Severity.Debug;
                module.Settings.EnableConsole = enableConsole;
                _Log = module;
            }
        }

        /// <summary>
        /// Writes a debug-severity message.
        /// </summary>
        /// <param name="message">The message. Null is treated as an empty string.</param>
        public static void Debug(string message)
        {
            SafeLog(Severity.Debug, message);
        }

        /// <summary>
        /// Writes an informational message.
        /// </summary>
        /// <param name="message">The message. Null is treated as an empty string.</param>
        public static void Info(string message)
        {
            SafeLog(Severity.Info, message);
        }

        /// <summary>
        /// Writes a warning message.
        /// </summary>
        /// <param name="message">The message. Null is treated as an empty string.</param>
        public static void Warn(string message)
        {
            SafeLog(Severity.Warn, message);
        }

        /// <summary>
        /// Writes an error message.
        /// </summary>
        /// <param name="message">The message. Null is treated as an empty string.</param>
        public static void Error(string message)
        {
            SafeLog(Severity.Error, message);
        }

        /// <summary>
        /// Writes an exception with module and method context.
        /// </summary>
        /// <param name="exception">The exception. Null is ignored.</param>
        /// <param name="module">The module name. Null is treated as an empty string.</param>
        /// <param name="method">The method name. Null is treated as an empty string.</param>
        public static void Exception(Exception exception, string module, string method)
        {
            if (exception == null) return;

            LoggingModule? log = _Log;
            if (log == null) return;

            try
            {
                log.Exception(exception, module ?? string.Empty, method ?? string.Empty);
            }
            catch (Exception)
            {
                // Logging must never throw into the caller.
            }
        }

        /// <summary>
        /// Writes a standalone crash report file and mirrors a critical line to the log.
        /// </summary>
        /// <param name="exception">The exception. Null is ignored.</param>
        /// <param name="context">A short description of what was happening. May be null.</param>
        public static void WriteCrash(Exception exception, string context)
        {
            if (exception == null) return;

            string? path = null;
            try
            {
                string directory = _CrashLogDirectory;
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                    path = Path.Combine(directory, "crash-" + stamp + ".log");

                    StringBuilder report = new StringBuilder();
                    report.AppendLine("S3Drive crash report");
                    report.AppendLine("Time (UTC): " + DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture));
                    report.AppendLine("Context:    " + (context ?? string.Empty));
                    report.AppendLine("OS:         " + Environment.OSVersion);
                    report.AppendLine("Runtime:    " + Environment.Version);
                    report.AppendLine("64-bit:     " + Environment.Is64BitProcess);
                    report.AppendLine();
                    report.AppendLine(exception.ToString());
                    File.WriteAllText(path, report.ToString());
                }
            }
            catch (Exception)
            {
                // Never throw from crash handling.
            }

            try
            {
                LoggingModule? log = _Log;
                log?.Critical("Crash while " + (context ?? "unknown") + (path != null ? " - report: " + path : string.Empty));
            }
            catch (Exception)
            {
            }

            Exception(exception, "S3Drive", context ?? "crash");
            Flush();
        }

        /// <summary>
        /// Flushes any buffered log output.
        /// </summary>
        public static void Flush()
        {
            LoggingModule? log = _Log;
            if (log == null) return;

            try
            {
                log.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Flushes and disposes the logger.
        /// </summary>
        public static void Dispose()
        {
            lock (_Sync)
            {
                if (_Log == null) return;
                try
                {
                    _Log.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                }

                _Log.Dispose();
                _Log = null;
            }
        }

        private static void SafeLog(Severity severity, string message)
        {
            string text = message ?? string.Empty;

            LoggingModule? log = _Log;
            if (log != null)
            {
                try
                {
                    log.Log(severity, text);
                }
                catch (Exception)
                {
                }
            }

            Action<string, string>? sink = MessageLogged;
            if (sink != null)
            {
                try
                {
                    sink(severity.ToString(), text);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
