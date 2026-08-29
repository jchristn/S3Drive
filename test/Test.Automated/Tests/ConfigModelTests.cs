namespace Test.Automated.Tests
{
    using System.IO;
    using System.Text.Json;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Serialization;
    using S3Drive.Core.Sharing;
    using Test.Automated.Harness;

    /// <summary>
    /// Tests for the configuration models, paths, and JSON options.
    /// </summary>
    public static class ConfigModelTests
    {
        /// <summary>
        /// Registers the tests.
        /// </summary>
        /// <param name="runner">The runner.</param>
        public static void Register(TestRunner runner)
        {
            runner.Add("Settings clamps metadata cache seconds", () =>
            {
                S3DriveSettings settings = new S3DriveSettings();
                settings.MetadataCacheSeconds = 99999;
                Assert.Equal(3600, settings.MetadataCacheSeconds);
                settings.MetadataCacheSeconds = -5;
                Assert.Equal(0, settings.MetadataCacheSeconds);
            });

            runner.Add("Settings clamps multipart threshold", () =>
            {
                S3DriveSettings settings = new S3DriveSettings();
                settings.MultipartThresholdBytes = 1;
                Assert.Equal(5L * 1024 * 1024, settings.MultipartThresholdBytes);
            });

            runner.Add("Settings coalesces nulls", () =>
            {
                S3DriveSettings settings = new S3DriveSettings();
                settings.Logging = null!;
                Assert.NotNull(settings.Logging);
                settings.Drives = null!;
                Assert.NotNull(settings.Drives);
            });

            runner.Add("DriveProfile coalesces nulls", () =>
            {
                DriveProfile profile = new DriveProfile();
                profile.Id = null!;
                Assert.Equal(string.Empty, profile.Id);
                profile.Bucket = null!;
                Assert.Equal(string.Empty, profile.Bucket);
                profile.Share = null!;
                Assert.NotNull(profile.Share);
            });

            runner.Add("SmbShareSettings coalesces principals", () =>
            {
                SmbShareSettings share = new SmbShareSettings();
                share.AllowedPrincipals = null!;
                Assert.NotNull(share.AllowedPrincipals);
                Assert.Equal(0, share.AllowedPrincipals.Count);
            });

            runner.Add("Json serializes enums as strings and round-trips", () =>
            {
                S3DriveSettings settings = new S3DriveSettings();
                DriveProfile profile = new DriveProfile
                {
                    Id = "drv_1",
                    Provider = S3ProviderEnum.S3Compatible,
                    Bucket = "b"
                };
                profile.Share.Access = ShareAccessEnum.ReadWrite;
                settings.Drives.Add(profile);

                string json = JsonSerializer.Serialize(settings, S3DriveJson.Options);
                Assert.Contains(json, "S3Compatible");
                Assert.Contains(json, "ReadWrite");

                S3DriveSettings? back = JsonSerializer.Deserialize<S3DriveSettings>(json, S3DriveJson.Options);
                Assert.NotNull(back);
                Assert.Equal(1, back!.Drives.Count);
                Assert.Equal(S3ProviderEnum.S3Compatible, back.Drives[0].Provider);
                Assert.Equal(ShareAccessEnum.ReadWrite, back.Drives[0].Share.Access);
            });

            runner.Add("Paths derive from explicit root", () =>
            {
                S3DrivePaths paths = new S3DrivePaths(Path.Combine("C:", "root"));
                Assert.Contains(paths.ConfigFile, "s3drive.json");
                Assert.Contains(paths.LogDirectory, "logs");
                Assert.Contains(paths.CrashLogDirectory, "crash-logs");
                Assert.Contains(paths.AgentLockFile, "agent.lock");
                Assert.Contains(paths.MachineKeyFile, "dp.key");
                Assert.Contains(paths.StatusFile, "status.json");
                Assert.Contains(paths.CommandDirectory, "commands");
                Assert.Contains(paths.CacheDirectoryFor("drv_1"), "drv_1");
            });

            runner.Add("Paths reject empty root and drive id", () =>
            {
                Assert.Throws<System.ArgumentException>(() => new S3DrivePaths(string.Empty));
                S3DrivePaths paths = new S3DrivePaths(Path.Combine("C:", "root"));
                Assert.Throws<System.ArgumentException>(() => paths.CacheDirectoryFor(string.Empty));
            });

            runner.Add("Paths EnsureDirectories creates tree", () =>
            {
                string root = Temp.NewDir();
                try
                {
                    S3DrivePaths paths = new S3DrivePaths(root);
                    paths.EnsureDirectories();
                    Assert.True(Directory.Exists(paths.LogDirectory));
                    Assert.True(Directory.Exists(paths.CrashLogDirectory));
                    Assert.True(Directory.Exists(paths.StateDirectory));
                    Assert.True(Directory.Exists(paths.CommandDirectory));
                    Assert.True(Directory.Exists(paths.CacheDirectory));
                }
                finally
                {
                    Temp.Delete(root);
                }
            });
        }
    }
}
