namespace Test.Automated
{
    using System.Threading.Tasks;
    using Test.Automated.Harness;
    using Test.Automated.Tests;

    internal static class Program
    {
        internal static async Task<int> Main(string[] args)
        {
            StorageTestConfig storage = StorageTestConfig.FromArgs(args);

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
    }
}
