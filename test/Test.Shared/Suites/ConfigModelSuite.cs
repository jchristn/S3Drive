namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using S3Drive.Core;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Ipc;
    using S3Drive.Core.Serialization;
    using S3Drive.Core.Storage;
    using Test.Shared.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the configuration and contract models, <see cref="S3DrivePaths"/>, and the
    /// shared JSON options.
    /// </summary>
    public static class ConfigModelSuite
    {
        private const string SuiteId = "ConfigModel";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TestCases.Create(SuiteId, "SettingsDefaults", "S3DriveSettings has documented defaults", () =>
                {
                    S3DriveSettings settings = new S3DriveSettings();
                    Assert.Equal(5, settings.MetadataCacheSeconds);
                    Assert.Equal(16L * 1024 * 1024, settings.MultipartThresholdBytes);
                    Assert.False(settings.Logging.ConsoleLogging);
                    Assert.True(settings.Logging.FileLogging);
                    Assert.Equal(0, settings.Drives.Count);
                }),

                TestCases.Create(SuiteId, "SettingsClampsMetadataCacheSeconds", "S3DriveSettings clamps metadata cache seconds to 0..3600", () =>
                {
                    S3DriveSettings settings = new S3DriveSettings();
                    settings.MetadataCacheSeconds = 99999;
                    Assert.Equal(3600, settings.MetadataCacheSeconds);
                    settings.MetadataCacheSeconds = -5;
                    Assert.Equal(0, settings.MetadataCacheSeconds);
                    settings.MetadataCacheSeconds = 30;
                    Assert.Equal(30, settings.MetadataCacheSeconds);
                }),

                TestCases.Create(SuiteId, "SettingsClampsMultipartThreshold", "S3DriveSettings clamps the multipart threshold to 5 MiB..5 GiB", () =>
                {
                    S3DriveSettings settings = new S3DriveSettings();
                    settings.MultipartThresholdBytes = 1;
                    Assert.Equal(5L * 1024 * 1024, settings.MultipartThresholdBytes);
                    settings.MultipartThresholdBytes = long.MaxValue;
                    Assert.Equal(5L * 1024 * 1024 * 1024, settings.MultipartThresholdBytes);
                    settings.MultipartThresholdBytes = 64L * 1024 * 1024;
                    Assert.Equal(64L * 1024 * 1024, settings.MultipartThresholdBytes);
                }),

                TestCases.Create(SuiteId, "SettingsCoalescesNulls", "S3DriveSettings coalesces null collections and sections", () =>
                {
                    S3DriveSettings settings = new S3DriveSettings();
                    settings.Logging = null!;
                    Assert.NotNull(settings.Logging);
                    settings.Drives = null!;
                    Assert.NotNull(settings.Drives);
                }),

                TestCases.Create(SuiteId, "DriveProfileDefaults", "DriveProfile defaults to AWS S3, SSL, virtual-hosted addressing, and auto-mount", () =>
                {
                    DriveProfile profile = new DriveProfile();
                    Assert.Equal(S3ProviderEnum.AwsS3, profile.Provider);
                    Assert.True(profile.UseSsl);
                    Assert.False(profile.UsePathStyle);
                    Assert.True(profile.AutoMount);
                    Assert.Null(profile.ServiceUrl);
                    Assert.Null(profile.Region);
                    Assert.Equal(string.Empty, profile.SecretKeyEncrypted);
                }),

                TestCases.Create(SuiteId, "DriveProfileCoalescesNulls", "DriveProfile coalesces every null string to empty", () =>
                {
                    DriveProfile profile = new DriveProfile();
                    profile.Id = null!;
                    profile.Name = null!;
                    profile.Bucket = null!;
                    profile.AccessKey = null!;
                    profile.SecretKeyEncrypted = null!;
                    profile.DriveLetter = null!;
                    Assert.Equal(string.Empty, profile.Id);
                    Assert.Equal(string.Empty, profile.Name);
                    Assert.Equal(string.Empty, profile.Bucket);
                    Assert.Equal(string.Empty, profile.AccessKey);
                    Assert.Equal(string.Empty, profile.SecretKeyEncrypted);
                    Assert.Equal(string.Empty, profile.DriveLetter);
                }),

                TestCases.Create(SuiteId, "ContractModelsCoalesceNulls", "S3Entry, DriveStatus, and AgentStatus coalesce null members", () =>
                {
                    S3Entry entry = new S3Entry();
                    entry.Key = null!;
                    entry.Name = null!;
                    Assert.Equal(string.Empty, entry.Key);
                    Assert.Equal(string.Empty, entry.Name);
                    Assert.Equal(S3EntryTypeEnum.File, entry.EntryType);

                    DriveStatus drive = new DriveStatus();
                    drive.DriveId = null!;
                    drive.Name = null!;
                    Assert.Equal(string.Empty, drive.DriveId);
                    Assert.Equal(string.Empty, drive.Name);
                    Assert.Equal(DriveMountStateEnum.Unmounted, drive.MountState);

                    AgentStatus status = new AgentStatus();
                    status.Drives = null!;
                    Assert.NotNull(status.Drives);
                }),

                TestCases.Create(SuiteId, "JsonEnumsAsStringsRoundTrip", "S3DriveJson serializes enums as strings and round-trips settings", () =>
                {
                    S3DriveSettings settings = new S3DriveSettings();
                    DriveProfile profile = new DriveProfile
                    {
                        Id = "drv_1",
                        Provider = S3ProviderEnum.S3Compatible,
                        Bucket = "b"
                    };
                    settings.Drives.Add(profile);

                    string json = JsonSerializer.Serialize(settings, S3DriveJson.Options);
                    Assert.Contains(json, "S3Compatible");

                    S3DriveSettings? back = JsonSerializer.Deserialize<S3DriveSettings>(json, S3DriveJson.Options);
                    Assert.NotNull(back);
                    Assert.Equal(1, back!.Drives.Count);
                    Assert.Equal(S3ProviderEnum.S3Compatible, back.Drives[0].Provider);
                }),

                TestCases.Create(SuiteId, "JsonOmitsNulls", "S3DriveJson omits null members when writing", () =>
                {
                    DriveProfile profile = new DriveProfile { Id = "drv_1" };
                    string json = JsonSerializer.Serialize(profile, S3DriveJson.Options);
                    Assert.False(json.Contains("ServiceUrl", StringComparison.Ordinal), "null ServiceUrl should be omitted");
                    Assert.False(json.Contains("Region", StringComparison.Ordinal), "null Region should be omitted");
                }),

                TestCases.Create(SuiteId, "JsonCaseInsensitive", "S3DriveJson reads property names case-insensitively", () =>
                {
                    DriveProfile? profile = JsonSerializer.Deserialize<DriveProfile>("{\"bucket\":\"lower\",\"PROVIDER\":\"S3Compatible\"}", S3DriveJson.Options);
                    Assert.NotNull(profile);
                    Assert.Equal("lower", profile!.Bucket);
                    Assert.Equal(S3ProviderEnum.S3Compatible, profile.Provider);
                }),

                TestCases.Create(SuiteId, "JsonRejectsUnknownEnum", "S3DriveJson rejects an unknown enum value", () =>
                {
                    Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DriveProfile>("{\"Provider\":\"Floppy\"}", S3DriveJson.Options));
                }),

                TestCases.Create(SuiteId, "JsonOptionsAreShared", "S3DriveJson.Options returns a single shared instance", () =>
                {
                    Assert.True(ReferenceEquals(S3DriveJson.Options, S3DriveJson.Options));
                }),

                TestCases.Create(SuiteId, "ConstantsAreStable", "Constants expose the product name and drive id prefix", () =>
                {
                    Assert.Equal("S3Drive", Constants.ProductName);
                    Assert.Equal("drv_", Constants.DriveIdPrefix);
                    Assert.True(Constants.RepositoryUrl.StartsWith("https://", StringComparison.Ordinal));
                }),

                TestCases.Create(SuiteId, "PathsDeriveFromRoot", "S3DrivePaths derives every path from an explicit root", () =>
                {
                    string root = Path.Combine("C:", "root");
                    S3DrivePaths paths = new S3DrivePaths(root);
                    Assert.Equal(root, paths.Root);
                    Assert.Equal(Path.Combine(root, "s3drive.json"), paths.ConfigFile);
                    Assert.Equal(Path.Combine(root, "logs"), paths.LogDirectory);
                    Assert.Equal(Path.Combine(root, "crash-logs"), paths.CrashLogDirectory);
                    Assert.Equal(Path.Combine(root, "state"), paths.StateDirectory);
                    Assert.Equal(Path.Combine(root, "cache"), paths.CacheDirectory);
                    Assert.Equal(Path.Combine(root, "state", "agent.lock"), paths.AgentLockFile);
                    Assert.Equal(Path.Combine(root, "state", "dp.key"), paths.MachineKeyFile);
                    Assert.Equal(Path.Combine(root, "state", "status.json"), paths.StatusFile);
                    Assert.Equal(Path.Combine(root, "state", "commands"), paths.CommandDirectory);
                    Assert.Equal(Path.Combine(root, "cache", "drv_1"), paths.CacheDirectoryFor("drv_1"));
                }),

                TestCases.Create(SuiteId, "PathsRejectEmptyRootAndDriveId", "S3DrivePaths rejects an empty root and an empty drive id", () =>
                {
                    Assert.Throws<ArgumentException>(() => new S3DrivePaths(string.Empty));
                    Assert.Throws<ArgumentException>(() => new S3DrivePaths(null!));
                    S3DrivePaths paths = new S3DrivePaths(Path.Combine("C:", "root"));
                    Assert.Throws<ArgumentException>(() => paths.CacheDirectoryFor(string.Empty));
                    Assert.Throws<ArgumentException>(() => paths.CacheDirectoryFor(null!));
                }),

                TestCases.Create(SuiteId, "PathsHonorHomeOverride", "S3DrivePaths default root honors S3DRIVE_HOME", () =>
                {
                    string? original = Environment.GetEnvironmentVariable(S3DrivePaths.HomeEnvironmentVariable);
                    string root = Temp.NewDir();
                    try
                    {
                        Environment.SetEnvironmentVariable(S3DrivePaths.HomeEnvironmentVariable, root);
                        Assert.Equal(root, new S3DrivePaths().Root);
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable(S3DrivePaths.HomeEnvironmentVariable, original);
                        Temp.Delete(root);
                    }
                }),

                TestCases.Create(SuiteId, "PathsDefaultUnderUserProfile", "S3DrivePaths default root is ~/.s3drive when S3DRIVE_HOME is unset", () =>
                {
                    string? original = Environment.GetEnvironmentVariable(S3DrivePaths.HomeEnvironmentVariable);
                    try
                    {
                        Environment.SetEnvironmentVariable(S3DrivePaths.HomeEnvironmentVariable, null);
                        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".s3drive");
                        Assert.Equal(expected, new S3DrivePaths().Root);
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable(S3DrivePaths.HomeEnvironmentVariable, original);
                    }
                }),

                TestCases.Create(SuiteId, "PathsEnsureDirectoriesCreatesTree", "S3DrivePaths.EnsureDirectories creates the tree and is idempotent", () =>
                {
                    string root = Path.Combine(Temp.NewDir(), "nested", "home");
                    try
                    {
                        S3DrivePaths paths = new S3DrivePaths(root);
                        paths.EnsureDirectories();
                        paths.EnsureDirectories();
                        Assert.True(Directory.Exists(paths.LogDirectory));
                        Assert.True(Directory.Exists(paths.CrashLogDirectory));
                        Assert.True(Directory.Exists(paths.StateDirectory));
                        Assert.True(Directory.Exists(paths.CommandDirectory));
                        Assert.True(Directory.Exists(paths.CacheDirectory));
                    }
                    finally
                    {
                        Temp.Delete(Path.GetDirectoryName(Path.GetDirectoryName(root))!);
                    }
                })
            };

            return new TestSuiteDescriptor(SuiteId, "Configuration models", cases);
        }
    }
}
