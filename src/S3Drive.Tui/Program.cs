namespace S3Drive.Tui
{
    using System;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Diagnostics;
    using TUIKit.Hosting;

    internal static class Program
    {
        internal static async Task<int> Main(string[] args)
        {
            bool showSplash = !HasFlag(args, "--no-splash");

            S3DrivePaths paths = new S3DrivePaths();
            try
            {
                paths.EnsureDirectories();
            }
            catch (Exception)
            {
            }

            S3DriveLog.Initialize(paths.LogDirectory, paths.CrashLogDirectory, "S3Drive.Tui", false);
            RegisterGlobalHandlers();
            AgentLauncher.EnsureRunning(paths);

            try
            {
                TuiController controller = new TuiController(paths, showSplash);
                await TuiApp.RunAsync(controller.Configure).ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex)
            {
                S3DriveLog.WriteCrash(ex, "running the TUI");
                Console.WriteLine("S3Drive TUI crashed: " + ex.Message);
                return 2;
            }
            finally
            {
                S3DriveLog.Flush();
                S3DriveLog.Dispose();
            }
        }

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private static void RegisterGlobalHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex) S3DriveLog.WriteCrash(ex, "unhandled exception in S3Drive.Tui");
            };
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                S3DriveLog.WriteCrash(e.Exception, "unobserved background task in S3Drive.Tui");
                e.SetObserved();
            };
        }
    }
}
