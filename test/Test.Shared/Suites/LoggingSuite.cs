namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using S3Drive.Core.Diagnostics;
    using Test.Shared.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Tests for <see cref="S3DriveLog"/>: the in-process message sink, file logging, crash
    /// reports, and the guarantee that logging never throws into callers.
    /// </summary>
    public static class LoggingSuite
    {
        private const string SuiteId = "Logging";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TestCases.Create(SuiteId, "SinkReceivesSeverities", "S3DriveLog raises MessageLogged with the severity name for each level", () =>
                {
                    string marker = Guid.NewGuid().ToString("N");
                    List<string> seen = new List<string>();
                    Action<string, string> handler = (severity, message) =>
                    {
                        if (message.Contains(marker, StringComparison.Ordinal))
                        {
                            lock (seen) seen.Add(severity + ":" + message);
                        }
                    };

                    S3DriveLog.MessageLogged += handler;
                    try
                    {
                        S3DriveLog.Debug("d " + marker);
                        S3DriveLog.Info("i " + marker);
                        S3DriveLog.Warn("w " + marker);
                        S3DriveLog.Error("e " + marker);
                    }
                    finally
                    {
                        S3DriveLog.MessageLogged -= handler;
                    }

                    Assert.Equal(4, seen.Count);
                    Assert.Equal("Debug:d " + marker, seen[0]);
                    Assert.Equal("Info:i " + marker, seen[1]);
                    Assert.Equal("Warn:w " + marker, seen[2]);
                    Assert.Equal("Error:e " + marker, seen[3]);
                }),

                TestCases.Create(SuiteId, "NullMessageBecomesEmpty", "S3DriveLog delivers a null message as an empty string", () =>
                {
                    bool sawEmpty = false;
                    Action<string, string> handler = (severity, message) =>
                    {
                        if (message == null) throw new AssertException("message must never be null");
                        if (message.Length == 0 && severity == "Warn") sawEmpty = true;
                    };

                    S3DriveLog.MessageLogged += handler;
                    try
                    {
                        S3DriveLog.Warn(null!);
                    }
                    finally
                    {
                        S3DriveLog.MessageLogged -= handler;
                    }

                    Assert.True(sawEmpty);
                }),

                TestCases.Create(SuiteId, "ThrowingSinkIsIsolated", "S3DriveLog never propagates an exception from a subscriber", () =>
                {
                    Action<string, string> handler = (severity, message) => throw new InvalidOperationException("subscriber failure");
                    S3DriveLog.MessageLogged += handler;
                    try
                    {
                        S3DriveLog.Info("should not throw");
                        S3DriveLog.Error("should not throw");
                    }
                    finally
                    {
                        S3DriveLog.MessageLogged -= handler;
                    }
                }),

                TestCases.Create(SuiteId, "UninitializedCallsAreSafe", "S3DriveLog tolerates exception, crash, flush, and null inputs without initialization", () =>
                {
                    S3DriveLog.Exception(null!, "m", "x");
                    S3DriveLog.Exception(new InvalidOperationException("x"), null!, null!);
                    S3DriveLog.WriteCrash(null!, "ctx");
                    S3DriveLog.Flush();
                }),

                TestCases.Create(SuiteId, "InitializeGuards", "S3DriveLog.Initialize rejects empty directories and application name", () =>
                {
                    Assert.Throws<ArgumentException>(() => S3DriveLog.Initialize(string.Empty, "c", "a", false));
                    Assert.Throws<ArgumentException>(() => S3DriveLog.Initialize("l", string.Empty, "a", false));
                    Assert.Throws<ArgumentException>(() => S3DriveLog.Initialize("l", "c", string.Empty, false));
                }),

                TestCases.Create(SuiteId, "FileLoggingAndCrashReport", "S3DriveLog writes log lines to file and crash reports to the crash directory", () =>
                {
                    Temp.WithDir(root =>
                    {
                        string logs = Path.Combine(root, "logs");
                        string crashes = Path.Combine(root, "crash-logs");
                        string marker = Guid.NewGuid().ToString("N");
                        try
                        {
                            S3DriveLog.Initialize(logs, crashes, "S3DriveTest", false);
                            Assert.True(Directory.Exists(logs));
                            Assert.True(Directory.Exists(crashes));

                            S3DriveLog.Info("file line " + marker);
                            S3DriveLog.WriteCrash(new InvalidOperationException("boom " + marker), "testing crash");
                            S3DriveLog.Flush();
                        }
                        finally
                        {
                            S3DriveLog.Dispose();
                        }

                        string[] crashFiles = Directory.GetFiles(crashes, "crash-*.log");
                        Assert.Equal(1, crashFiles.Length);
                        string report = File.ReadAllText(crashFiles[0]);
                        Assert.Contains(report, "testing crash");
                        Assert.Contains(report, "boom " + marker);

                        bool found = false;
                        foreach (string file in Directory.GetFiles(logs))
                        {
                            if (ReadShared(file).Contains("file line " + marker, StringComparison.Ordinal)) found = true;
                        }

                        Assert.True(found, "log file should contain the logged line");
                    });
                }),

                TestCases.Create(SuiteId, "DisposeIsIdempotent", "S3DriveLog.Dispose is safe to call repeatedly and logging continues to the sink", () =>
                {
                    S3DriveLog.Dispose();
                    S3DriveLog.Dispose();

                    string marker = Guid.NewGuid().ToString("N");
                    bool delivered = false;
                    Action<string, string> handler = (severity, message) =>
                    {
                        if (message.Contains(marker, StringComparison.Ordinal)) delivered = true;
                    };

                    S3DriveLog.MessageLogged += handler;
                    try
                    {
                        S3DriveLog.Info(marker);
                    }
                    finally
                    {
                        S3DriveLog.MessageLogged -= handler;
                    }

                    Assert.True(delivered);
                })
            };

            return new TestSuiteDescriptor(SuiteId, "Logging", cases);
        }

        private static string ReadShared(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
