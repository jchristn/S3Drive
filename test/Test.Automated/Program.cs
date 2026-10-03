namespace Test.Automated
{
    using System;
    using System.Threading.Tasks;
    using Test.Shared;
    using Test.Shared.Helpers;
    using Touchstone.Cli;

    internal static class Program
    {
        internal static async Task<int> Main(string[] args)
        {
            StorageTestConfig storage = StorageTestConfig.FromArgs(args);

            // Interactive harness modes for driving a real Dokan mount and a running agent by hand.
            if (HasFlag(args, "--mount-test"))
            {
                return await MountHarness.RunAsync(storage, GetArg(args, "--drive-letter"));
            }

            if (HasFlag(args, "--make-config"))
            {
                bool autoMount = !string.Equals(GetArg(args, "--auto-mount"), "false", StringComparison.OrdinalIgnoreCase);
                return await AgentControl.WriteConfigAsync(storage, GetArg(args, "--drive-letter"), autoMount);
            }

            if (HasFlag(args, "--send-command"))
            {
                return await AgentControl.SendCommandAsync(GetArg(args, "--send-command") ?? "reload", GetArg(args, "--drive-id"));
            }

            return await ConsoleRunner.RunAsync(S3DriveSuites.Build(storage), resultsPath: GetArg(args, "--results"));
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private static string? GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }

            return null;
        }
    }
}
