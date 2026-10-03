namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using Test.Shared.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Tests for <see cref="SettingsManager"/>: load, save, parsing, and environment overrides.
    /// </summary>
    public static class SettingsManagerSuite
    {
        private const string SuiteId = "SettingsManager";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TestCases.Create(SuiteId, "CreatesDefaultFile", "SettingsManager.LoadAsync creates a default config file when absent", async ct =>
                {
                    await WithRootAsync(async paths =>
                    {
                        SettingsManager manager = new SettingsManager(paths, _ => null);
                        S3DriveSettings settings = await manager.LoadAsync(ct).ConfigureAwait(false);
                        Assert.NotNull(settings);
                        Assert.True(File.Exists(paths.ConfigFile));
                        Assert.Equal(0, settings.Drives.Count);
                        Assert.Equal(5, settings.MetadataCacheSeconds);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "SaveLoadRoundTrip", "SettingsManager save and load round-trips every drive field", async ct =>
                {
                    await WithRootAsync(async paths =>
                    {
                        SettingsManager manager = new SettingsManager(paths, _ => null);

                        S3DriveSettings settings = new S3DriveSettings();
                        settings.MetadataCacheSeconds = 17;
                        settings.Logging.ConsoleLogging = true;
                        settings.Drives.Add(new DriveProfile
                        {
                            Id = "drv_x",
                            Name = "Archive",
                            Bucket = "bucket-1",
                            Provider = S3ProviderEnum.S3Compatible,
                            ServiceUrl = "http://127.0.0.1:9000",
                            UseSsl = false,
                            Region = "eu-west-1",
                            AccessKey = "AKIA",
                            SecretKeyEncrypted = "ciphertext",
                            UsePathStyle = true,
                            DriveLetter = "S:",
                            AutoMount = false
                        });
                        await manager.SaveAsync(settings, ct).ConfigureAwait(false);

                        S3DriveSettings loaded = await manager.LoadAsync(ct).ConfigureAwait(false);
                        Assert.Equal(17, loaded.MetadataCacheSeconds);
                        Assert.True(loaded.Logging.ConsoleLogging);
                        Assert.Equal(1, loaded.Drives.Count);
                        DriveProfile drive = loaded.Drives[0];
                        Assert.Equal("drv_x", drive.Id);
                        Assert.Equal("Archive", drive.Name);
                        Assert.Equal("bucket-1", drive.Bucket);
                        Assert.Equal(S3ProviderEnum.S3Compatible, drive.Provider);
                        Assert.Equal("http://127.0.0.1:9000", drive.ServiceUrl);
                        Assert.False(drive.UseSsl);
                        Assert.Equal("eu-west-1", drive.Region);
                        Assert.Equal("AKIA", drive.AccessKey);
                        Assert.Equal("ciphertext", drive.SecretKeyEncrypted);
                        Assert.True(drive.UsePathStyle);
                        Assert.Equal("S:", drive.DriveLetter);
                        Assert.False(drive.AutoMount);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "MultipleDrivesPreserveOrder", "SettingsManager preserves multiple drives in order", async ct =>
                {
                    await WithRootAsync(async paths =>
                    {
                        SettingsManager manager = new SettingsManager(paths, _ => null);
                        S3DriveSettings settings = new S3DriveSettings();
                        settings.Drives.Add(new DriveProfile { Id = "drv_a", DriveLetter = "R:" });
                        settings.Drives.Add(new DriveProfile { Id = "drv_b", DriveLetter = "S:" });
                        settings.Drives.Add(new DriveProfile { Id = "drv_c", DriveLetter = "T:" });
                        await manager.SaveAsync(settings, ct).ConfigureAwait(false);

                        S3DriveSettings loaded = await manager.LoadAsync(ct).ConfigureAwait(false);
                        Assert.Equal(3, loaded.Drives.Count);
                        Assert.Equal("drv_a", loaded.Drives[0].Id);
                        Assert.Equal("drv_b", loaded.Drives[1].Id);
                        Assert.Equal("drv_c", loaded.Drives[2].Id);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "SaveLeavesNoTempFile", "SettingsManager.SaveAsync writes atomically and leaves no temp file", async ct =>
                {
                    await WithRootAsync(async paths =>
                    {
                        SettingsManager manager = new SettingsManager(paths, _ => null);
                        await manager.SaveAsync(new S3DriveSettings(), ct).ConfigureAwait(false);
                        await manager.SaveAsync(new S3DriveSettings(), ct).ConfigureAwait(false);
                        Assert.True(File.Exists(paths.ConfigFile));
                        Assert.False(File.Exists(paths.ConfigFile + ".tmp"));
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "AppliesEnvironmentOverrides", "SettingsManager applies S3DRIVE_* environment overrides", async ct =>
                {
                    await WithRootAsync(async paths =>
                    {
                        Dictionary<string, string?> env = new Dictionary<string, string?>
                        {
                            ["S3DRIVE_LOG_CONSOLE"] = "true",
                            ["S3DRIVE_LOG_FILE"] = "false",
                            ["S3DRIVE_METADATA_CACHE_SECONDS"] = "42",
                            ["S3DRIVE_MULTIPART_THRESHOLD_BYTES"] = "10485760"
                        };
                        SettingsManager manager = new SettingsManager(paths, name => env.TryGetValue(name, out string? v) ? v : null);

                        S3DriveSettings settings = await manager.LoadAsync(ct).ConfigureAwait(false);
                        Assert.True(settings.Logging.ConsoleLogging);
                        Assert.False(settings.Logging.FileLogging);
                        Assert.Equal(42, settings.MetadataCacheSeconds);
                        Assert.Equal(10485760L, settings.MultipartThresholdBytes);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "EnvironmentOverridesAreNotPersisted", "SettingsManager environment overrides do not rewrite the config file", async ct =>
                {
                    await WithRootAsync(async paths =>
                    {
                        await new SettingsManager(paths, _ => null).SaveAsync(new S3DriveSettings(), ct).ConfigureAwait(false);
                        SettingsManager overridden = new SettingsManager(paths, name => name == "S3DRIVE_METADATA_CACHE_SECONDS" ? "99" : null);
                        Assert.Equal(99, (await overridden.LoadAsync(ct).ConfigureAwait(false)).MetadataCacheSeconds);
                        Assert.Equal(5, (await new SettingsManager(paths, _ => null).LoadAsync(ct).ConfigureAwait(false)).MetadataCacheSeconds);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "IgnoresInvalidEnvironmentValues", "SettingsManager ignores malformed or blank environment overrides", () =>
                {
                    Dictionary<string, string?> env = new Dictionary<string, string?>
                    {
                        ["S3DRIVE_LOG_CONSOLE"] = "yes please",
                        ["S3DRIVE_LOG_FILE"] = "   ",
                        ["S3DRIVE_METADATA_CACHE_SECONDS"] = "ten",
                        ["S3DRIVE_MULTIPART_THRESHOLD_BYTES"] = "1.5e9"
                    };
                    SettingsManager manager = new SettingsManager(new S3DrivePaths(Path.Combine("C:", "x")), name => env.TryGetValue(name, out string? v) ? v : null);
                    S3DriveSettings settings = new S3DriveSettings();
                    manager.ApplyEnvironmentOverrides(settings);
                    Assert.False(settings.Logging.ConsoleLogging);
                    Assert.True(settings.Logging.FileLogging);
                    Assert.Equal(5, settings.MetadataCacheSeconds);
                    Assert.Equal(16L * 1024 * 1024, settings.MultipartThresholdBytes);
                }),

                TestCases.Create(SuiteId, "EnvironmentOverridesAreClampedAndTrimmed", "SettingsManager trims override values and clamps them to valid ranges", () =>
                {
                    Dictionary<string, string?> env = new Dictionary<string, string?>
                    {
                        ["S3DRIVE_LOG_CONSOLE"] = "  TRUE  ",
                        ["S3DRIVE_METADATA_CACHE_SECONDS"] = "999999",
                        ["S3DRIVE_MULTIPART_THRESHOLD_BYTES"] = "1"
                    };
                    SettingsManager manager = new SettingsManager(new S3DrivePaths(Path.Combine("C:", "x")), name => env.TryGetValue(name, out string? v) ? v : null);
                    S3DriveSettings settings = new S3DriveSettings();
                    manager.ApplyEnvironmentOverrides(settings);
                    Assert.True(settings.Logging.ConsoleLogging);
                    Assert.Equal(3600, settings.MetadataCacheSeconds);
                    Assert.Equal(5L * 1024 * 1024, settings.MultipartThresholdBytes);
                }),

                TestCases.Create(SuiteId, "ParseClampsOutOfRangeValues", "SettingsManager.Parse clamps out-of-range values found in the file", () =>
                {
                    SettingsManager manager = new SettingsManager(new S3DrivePaths(Path.Combine("C:", "x")), _ => null);
                    S3DriveSettings settings = manager.Parse("{\"MetadataCacheSeconds\": 99999, \"MultipartThresholdBytes\": 0}");
                    Assert.Equal(3600, settings.MetadataCacheSeconds);
                    Assert.Equal(5L * 1024 * 1024, settings.MultipartThresholdBytes);
                }),

                TestCases.Create(SuiteId, "ParseJsonNullYieldsDefaults", "SettingsManager.Parse returns defaults for a JSON null or empty object", () =>
                {
                    SettingsManager manager = new SettingsManager(new S3DrivePaths(Path.Combine("C:", "x")), _ => null);
                    Assert.NotNull(manager.Parse("null"));
                    S3DriveSettings empty = manager.Parse("{}");
                    Assert.Equal(5, empty.MetadataCacheSeconds);
                    Assert.NotNull(empty.Drives);
                    S3DriveSettings nullDrives = manager.Parse("{\"Drives\": null, \"Logging\": null}");
                    Assert.NotNull(nullDrives.Drives);
                    Assert.NotNull(nullDrives.Logging);
                }),

                TestCases.Create(SuiteId, "ParseRejectsNull", "SettingsManager.Parse rejects a null string", () =>
                {
                    SettingsManager manager = new SettingsManager(new S3DrivePaths(Path.Combine("C:", "x")), _ => null);
                    Assert.Throws<ArgumentNullException>(() => manager.Parse(null!));
                }),

                TestCases.Create(SuiteId, "ParseRejectsMalformedJson", "SettingsManager.Parse rejects malformed JSON", () =>
                {
                    SettingsManager manager = new SettingsManager(new S3DrivePaths(Path.Combine("C:", "x")), _ => null);
                    Assert.Throws<JsonException>(() => manager.Parse("{ \"Drives\": [ "));
                }),

                TestCases.Create(SuiteId, "LoadRejectsCorruptFile", "SettingsManager.LoadAsync surfaces a corrupt config file instead of overwriting it", async ct =>
                {
                    await WithRootAsync(async paths =>
                    {
                        paths.EnsureDirectories();
                        await File.WriteAllTextAsync(paths.ConfigFile, "this is not json", ct).ConfigureAwait(false);
                        SettingsManager manager = new SettingsManager(paths, _ => null);
                        await Assert.ThrowsAsync<JsonException>(() => manager.LoadAsync(ct)).ConfigureAwait(false);
                        Assert.Equal("this is not json", await File.ReadAllTextAsync(paths.ConfigFile, ct).ConfigureAwait(false));
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "SaveRejectsNull", "SettingsManager.SaveAsync rejects null settings", async ct =>
                {
                    SettingsManager manager = new SettingsManager(new S3DrivePaths(Path.Combine("C:", "x")), _ => null);
                    await Assert.ThrowsAsync<ArgumentNullException>(() => manager.SaveAsync(null!, ct)).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ApplyOverridesRejectsNull", "SettingsManager.ApplyEnvironmentOverrides rejects null settings", () =>
                {
                    SettingsManager manager = new SettingsManager(new S3DrivePaths(Path.Combine("C:", "x")), _ => null);
                    Assert.Throws<ArgumentNullException>(() => manager.ApplyEnvironmentOverrides(null!));
                }),

                TestCases.Create(SuiteId, "HonorsCancellation", "SettingsManager load and save honor a canceled token", async ct =>
                {
                    await WithRootAsync(async paths =>
                    {
                        SettingsManager manager = new SettingsManager(paths, _ => null);
                        using (CancellationTokenSource cts = new CancellationTokenSource())
                        {
                            cts.Cancel();
                            await Assert.ThrowsAsync<OperationCanceledException>(() => manager.LoadAsync(cts.Token)).ConfigureAwait(false);
                            await Assert.ThrowsAsync<OperationCanceledException>(() => manager.SaveAsync(new S3DriveSettings(), cts.Token)).ConfigureAwait(false);
                        }

                        Assert.False(File.Exists(paths.ConfigFile), "a canceled save must not write the file");
                    }).ConfigureAwait(false);
                })
            };

            return new TestSuiteDescriptor(SuiteId, "Settings manager", cases);
        }

        private static Task WithRootAsync(Func<S3DrivePaths, Task> body)
        {
            return Temp.WithDirAsync(root => body(new S3DrivePaths(root)));
        }
    }
}
