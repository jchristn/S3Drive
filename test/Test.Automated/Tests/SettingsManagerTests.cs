namespace Test.Automated.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using Test.Automated.Harness;

    /// <summary>
    /// Tests for <see cref="SettingsManager"/>.
    /// </summary>
    public static class SettingsManagerTests
    {
        /// <summary>
        /// Registers the tests.
        /// </summary>
        /// <param name="runner">The runner.</param>
        public static void Register(TestRunner runner)
        {
            runner.Add("SettingsManager creates default file", async () =>
            {
                string root = Temp.NewDir();
                try
                {
                    S3DrivePaths paths = new S3DrivePaths(root);
                    SettingsManager manager = new SettingsManager(paths, _ => null);
                    S3DriveSettings settings = await manager.LoadAsync();
                    Assert.NotNull(settings);
                    Assert.True(File.Exists(paths.ConfigFile));
                }
                finally
                {
                    Temp.Delete(root);
                }
            });

            runner.Add("SettingsManager save and load round-trip", async () =>
            {
                string root = Temp.NewDir();
                try
                {
                    S3DrivePaths paths = new S3DrivePaths(root);
                    SettingsManager manager = new SettingsManager(paths, _ => null);

                    S3DriveSettings settings = new S3DriveSettings();
                    settings.Drives.Add(new DriveProfile
                    {
                        Id = "drv_x",
                        Bucket = "bucket-1",
                        Provider = S3ProviderEnum.S3Compatible,
                        DriveLetter = "S:"
                    });
                    await manager.SaveAsync(settings);

                    S3DriveSettings loaded = await manager.LoadAsync();
                    Assert.Equal(1, loaded.Drives.Count);
                    Assert.Equal("drv_x", loaded.Drives[0].Id);
                    Assert.Equal("bucket-1", loaded.Drives[0].Bucket);
                    Assert.Equal(S3ProviderEnum.S3Compatible, loaded.Drives[0].Provider);
                }
                finally
                {
                    Temp.Delete(root);
                }
            });

            runner.Add("SettingsManager applies environment overrides", async () =>
            {
                string root = Temp.NewDir();
                try
                {
                    S3DrivePaths paths = new S3DrivePaths(root);
                    Dictionary<string, string?> env = new Dictionary<string, string?>
                    {
                        ["S3DRIVE_LOG_CONSOLE"] = "true",
                        ["S3DRIVE_LOG_FILE"] = "false",
                        ["S3DRIVE_METADATA_CACHE_SECONDS"] = "42",
                        ["S3DRIVE_MULTIPART_THRESHOLD_BYTES"] = "10485760"
                    };
                    SettingsManager manager = new SettingsManager(paths, name => env.TryGetValue(name, out string? v) ? v : null);

                    S3DriveSettings settings = await manager.LoadAsync();
                    Assert.True(settings.Logging.ConsoleLogging);
                    Assert.False(settings.Logging.FileLogging);
                    Assert.Equal(42, settings.MetadataCacheSeconds);
                    Assert.Equal(10485760L, settings.MultipartThresholdBytes);
                }
                finally
                {
                    Temp.Delete(root);
                }
            });

            runner.Add("SettingsManager Parse rejects null", () =>
            {
                SettingsManager manager = new SettingsManager(new S3DrivePaths(Path.Combine("C:", "x")), _ => null);
                Assert.Throws<ArgumentNullException>(() => manager.Parse(null!));
            });

            runner.Add("SettingsManager SaveAsync rejects null settings", async () =>
            {
                SettingsManager manager = new SettingsManager(new S3DrivePaths(Path.Combine("C:", "x")), _ => null);
                await Assert.ThrowsAsync<ArgumentNullException>(() => manager.SaveAsync(null!));
            });
        }
    }
}
