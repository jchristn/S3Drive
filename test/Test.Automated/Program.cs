namespace Test.Automated
{
    using System;
    using System.Threading.Tasks;
    using Test.Automated.Harness;
    using Test.Automated.Tests;

    internal static class Program
    {
        internal static async Task<int> Main(string[] args)
        {
            StorageTestConfig storage = StorageTestConfig.FromArgs(args);

            if (HasFlag(args, "--mount-test"))
            {
                return await MountHarness.RunAsync(storage, GetArg(args, "--drive-letter"));
            }

            TestRunner runner = new TestRunner();
            KeyMapperTests.Register(runner);
            MetadataCacheTests.Register(runner);
            ConfigModelTests.Register(runner);
            SettingsManagerTests.Register(runner);
            CryptoTests.Register(runner);
            SecurityAndLockTests.Register(runner);
            SharingTests.Register(runner);
            IpcTests.Register(runner);
            FileSystemTests.Register(runner);
            StorageIntegrationTests.Register(runner, storage);

            return await runner.RunAsync();
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
