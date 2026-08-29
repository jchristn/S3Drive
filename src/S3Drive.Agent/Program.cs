namespace S3Drive.Agent
{
    using System;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using S3Drive.Core.Concurrency;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Diagnostics;

    internal static class Program
    {
        internal static int Main(string[] args)
        {
            S3DrivePaths paths = new S3DrivePaths();
            try
            {
                paths.EnsureDirectories();
            }
            catch (Exception)
            {
            }

            S3DriveLog.Initialize(paths.LogDirectory, paths.CrashLogDirectory, "S3Drive.Agent", false);
            RegisterGlobalHandlers();

            FileLockHandle? instance = AgentInstanceLock.TryAcquire(paths.StateDirectory);
            if (instance == null)
            {
                S3DriveLog.Info("Another S3Drive agent is already running; exiting.");
                S3DriveLog.Flush();
                S3DriveLog.Dispose();
                return 0;
            }

            S3DriveLog.Info("S3Drive agent starting.");
            try
            {
                using (instance)
                {
                    return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
                }
            }
            catch (Exception ex)
            {
                S3DriveLog.WriteCrash(ex, "running the agent");
                return 2;
            }
            finally
            {
                S3DriveLog.Info("S3Drive agent exiting.");
                S3DriveLog.Flush();
                S3DriveLog.Dispose();
            }
        }

        private static void RegisterGlobalHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex) S3DriveLog.WriteCrash(ex, "unhandled exception in S3Drive.Agent");
            };
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                S3DriveLog.WriteCrash(e.Exception, "unobserved background task in S3Drive.Agent");
                e.SetObserved();
            };
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
        }
    }
}
