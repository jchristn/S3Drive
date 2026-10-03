namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Ipc;
    using S3Drive.Core.Mounting;
    using S3Drive.Core.Security;
    using Test.Shared.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the parts of <see cref="MountManager"/> that do not require the Dokan driver:
    /// argument validation, status reporting, settings, and unmount bookkeeping.
    /// </summary>
    public static class MountManagerSuite
    {
        private const string SuiteId = "MountManager";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TestCases.Create(SuiteId, "ConstructorGuards", "MountManager rejects null paths and protector", () =>
                {
                    S3DrivePaths paths = new S3DrivePaths(Path.Combine(Path.GetTempPath(), "unused"));
                    CredentialProtector protector = new CredentialProtector(paths.MachineKeyFile);
                    Assert.Throws<ArgumentNullException>(() => new MountManager(null!, protector));
                    Assert.Throws<ArgumentNullException>(() => new MountManager(paths, null!));
                }),

                TestCases.Create(SuiteId, "InitialStatusIsEmpty", "MountManager starts with no mounts and reports its process id", async ct =>
                {
                    await WithManagerAsync(async manager =>
                    {
                        Assert.Equal(0, manager.MountedIds().Count);
                        DateTime before = DateTime.UtcNow.AddSeconds(-5);
                        AgentStatus status = manager.BuildStatus();
                        Assert.Equal(0, status.Drives.Count);
                        Assert.Equal(Environment.ProcessId, status.ProcessId);
                        Assert.True(status.UpdatedUtc >= before, "status timestamp should be current");
                        await Task.CompletedTask.ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "MetadataCacheSecondsClamped", "MountManager clamps metadata cache seconds to 0..3600", async ct =>
                {
                    await WithManagerAsync(async manager =>
                    {
                        Assert.Equal(5, manager.MetadataCacheSeconds);
                        manager.MetadataCacheSeconds = 99999;
                        Assert.Equal(3600, manager.MetadataCacheSeconds);
                        manager.MetadataCacheSeconds = -1;
                        Assert.Equal(0, manager.MetadataCacheSeconds);
                        manager.MetadataCacheSeconds = 12;
                        Assert.Equal(12, manager.MetadataCacheSeconds);
                        await Task.CompletedTask.ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "MountNullProfileThrows", "MountManager.MountAsync rejects a null profile", async ct =>
                {
                    await WithManagerAsync(async manager =>
                    {
                        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.MountAsync(null!, ct)).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "MountFailsWithoutMounting", "MountManager.MountAsync fails cleanly (non-Windows or undecryptable secret) and records no mount", async ct =>
                {
                    await WithManagerAsync(async manager =>
                    {
                        int events = 0;
                        manager.StatusChanged += _ => events++;
                        DriveProfile profile = new DriveProfile
                        {
                            Id = "drv_bad",
                            Bucket = "b",
                            AccessKey = "a",
                            SecretKeyEncrypted = "not-a-valid-ciphertext",
                            DriveLetter = "Q:"
                        };

                        if (OperatingSystem.IsWindows())
                            await Assert.ThrowsAsync<S3DriveCryptoException>(() => manager.MountAsync(profile, ct)).ConfigureAwait(false);
                        else
                            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => manager.MountAsync(profile, ct)).ConfigureAwait(false);

                        Assert.Equal(0, manager.MountedIds().Count);
                        Assert.Equal(0, manager.BuildStatus().Drives.Count);
                        Assert.Equal(0, events, "a mount that never started must not publish status");
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "UnmountEmptyIdThrows", "MountManager.UnmountAsync rejects an empty drive id", async ct =>
                {
                    await WithManagerAsync(async manager =>
                    {
                        await Assert.ThrowsAsync<ArgumentException>(() => manager.UnmountAsync(string.Empty, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentException>(() => manager.UnmountAsync(null!, ct)).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "UnmountUnknownIsNoop", "MountManager unmount of an unknown drive and unmount-all are no-ops", async ct =>
                {
                    await WithManagerAsync(async manager =>
                    {
                        int events = 0;
                        manager.StatusChanged += _ => events++;
                        await manager.UnmountAsync("drv_unknown", ct).ConfigureAwait(false);
                        await manager.UnmountAllAsync(ct).ConfigureAwait(false);
                        Assert.Equal(0, events);
                        Assert.Equal(0, manager.MountedIds().Count);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "DisposeWithoutMountsSucceeds", "MountManager disposes cleanly with no mounts", async ct =>
                {
                    await WithManagerAsync(async manager =>
                    {
                        await manager.DisposeAsync().ConfigureAwait(false);
                        await manager.DisposeAsync().ConfigureAwait(false);
                    }).ConfigureAwait(false);
                })
            };

            return new TestSuiteDescriptor(SuiteId, "Mount manager", cases);
        }

        private static Task WithManagerAsync(Func<MountManager, Task> body)
        {
            return Temp.WithDirAsync(root =>
            {
                S3DrivePaths paths = new S3DrivePaths(root);
                MountManager manager = new MountManager(paths, new CredentialProtector(paths.MachineKeyFile));
                return body(manager);
            });
        }
    }
}
