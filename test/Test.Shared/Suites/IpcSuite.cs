namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Ipc;
    using Test.Shared.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the filesystem-based TUI/agent coordination: <see cref="CommandChannel"/> and
    /// <see cref="StatusStore"/>.
    /// </summary>
    public static class IpcSuite
    {
        private const string SuiteId = "Ipc";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TestCases.Create(SuiteId, "CommandSendListRead", "CommandChannel send, list, and read a command", async ct =>
                {
                    await WithPathsAsync(async paths =>
                    {
                        AgentCommand command = new AgentCommand { CommandType = AgentCommandTypeEnum.Mount, DriveId = "drv_1" };
                        await CommandChannel.SendAsync(paths, command, ct).ConfigureAwait(false);

                        IReadOnlyList<string> pending = CommandChannel.ListPending(paths);
                        Assert.Equal(1, pending.Count);
                        Assert.True(CommandChannel.TryRead(pending[0], out AgentCommand? parsed));
                        Assert.NotNull(parsed);
                        Assert.Equal(AgentCommandTypeEnum.Mount, parsed!.CommandType);
                        Assert.Equal("drv_1", parsed.DriveId);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "CommandEveryTypeRoundTrips", "CommandChannel round-trips every command type", async ct =>
                {
                    await WithPathsAsync(async paths =>
                    {
                        foreach (AgentCommandTypeEnum type in Enum.GetValues<AgentCommandTypeEnum>())
                        {
                            await CommandChannel.SendAsync(paths, new AgentCommand { CommandType = type }, ct).ConfigureAwait(false);
                        }

                        IReadOnlyList<string> pending = CommandChannel.ListPending(paths);
                        Assert.Equal(Enum.GetValues<AgentCommandTypeEnum>().Length, pending.Count);

                        HashSet<AgentCommandTypeEnum> seen = new HashSet<AgentCommandTypeEnum>();
                        foreach (string file in pending)
                        {
                            Assert.True(CommandChannel.TryRead(file, out AgentCommand? parsed));
                            Assert.Null(parsed!.DriveId);
                            seen.Add(parsed.CommandType);
                        }

                        Assert.Equal(Enum.GetValues<AgentCommandTypeEnum>().Length, seen.Count);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "CommandWrittenAsNamedEnum", "CommandChannel writes the command type as a named enum value", async ct =>
                {
                    await WithPathsAsync(async paths =>
                    {
                        await CommandChannel.SendAsync(paths, new AgentCommand { CommandType = AgentCommandTypeEnum.UnmountAll }, ct).ConfigureAwait(false);
                        string json = await File.ReadAllTextAsync(CommandChannel.ListPending(paths)[0], ct).ConfigureAwait(false);
                        Assert.Contains(json, "UnmountAll");
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "CommandLeavesNoTempFile", "CommandChannel publishes atomically and leaves no temp file", async ct =>
                {
                    await WithPathsAsync(async paths =>
                    {
                        await CommandChannel.SendAsync(paths, new AgentCommand(), ct).ConfigureAwait(false);
                        Assert.Equal(0, Directory.GetFiles(paths.CommandDirectory, "*.tmp").Length);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ListPendingEmptyByDefault", "CommandChannel.ListPending is empty when the directory is missing or empty", () =>
                {
                    Temp.WithDir(root =>
                    {
                        S3DrivePaths paths = new S3DrivePaths(root);
                        Assert.Equal(0, CommandChannel.ListPending(paths).Count);
                        paths.EnsureDirectories();
                        Assert.Equal(0, CommandChannel.ListPending(paths).Count);
                    });
                }),

                TestCases.Create(SuiteId, "ListPendingIgnoresForeignFiles", "CommandChannel.ListPending ignores temp and unrelated files", () =>
                {
                    Temp.WithDir(root =>
                    {
                        S3DrivePaths paths = new S3DrivePaths(root);
                        paths.EnsureDirectories();
                        File.WriteAllText(Path.Combine(paths.CommandDirectory, "cmd-abc.json.tmp"), "{}");
                        File.WriteAllText(Path.Combine(paths.CommandDirectory, "notes.txt"), "x");
                        File.WriteAllText(Path.Combine(paths.CommandDirectory, "other.json"), "{}");
                        File.WriteAllText(Path.Combine(paths.CommandDirectory, "cmd-abc.json"), "{}");
                        IReadOnlyList<string> pending = CommandChannel.ListPending(paths);
                        Assert.Equal(1, pending.Count);
                        Assert.True(pending[0].EndsWith("cmd-abc.json", StringComparison.Ordinal));
                    });
                }),

                TestCases.Create(SuiteId, "ListPendingIsOrdered", "CommandChannel.ListPending returns files in ordinal order", () =>
                {
                    Temp.WithDir(root =>
                    {
                        S3DrivePaths paths = new S3DrivePaths(root);
                        paths.EnsureDirectories();
                        File.WriteAllText(Path.Combine(paths.CommandDirectory, "cmd-c.json"), "{}");
                        File.WriteAllText(Path.Combine(paths.CommandDirectory, "cmd-a.json"), "{}");
                        File.WriteAllText(Path.Combine(paths.CommandDirectory, "cmd-b.json"), "{}");
                        IReadOnlyList<string> pending = CommandChannel.ListPending(paths);
                        Assert.Equal(3, pending.Count);
                        Assert.True(pending[0].EndsWith("cmd-a.json", StringComparison.Ordinal));
                        Assert.True(pending[2].EndsWith("cmd-c.json", StringComparison.Ordinal));
                    });
                }),

                TestCases.Create(SuiteId, "TryReadRejectsMalformedJson", "CommandChannel.TryRead rejects malformed JSON", () =>
                {
                    Temp.WithDir(root =>
                    {
                        string file = Path.Combine(root, "bad.json");
                        File.WriteAllText(file, "{ not json");
                        Assert.False(CommandChannel.TryRead(file, out AgentCommand? command));
                        Assert.Null(command);
                    });
                }),

                TestCases.Create(SuiteId, "TryReadRejectsJsonNullAndUnknownType", "CommandChannel.TryRead rejects a JSON null and an unknown command type", () =>
                {
                    Temp.WithDir(root =>
                    {
                        string nullFile = Path.Combine(root, "null.json");
                        File.WriteAllText(nullFile, "null");
                        Assert.False(CommandChannel.TryRead(nullFile, out _));

                        string unknown = Path.Combine(root, "unknown.json");
                        File.WriteAllText(unknown, "{\"CommandType\":\"FormatDisk\"}");
                        Assert.False(CommandChannel.TryRead(unknown, out _));
                    });
                }),

                TestCases.Create(SuiteId, "TryReadMissingFile", "CommandChannel.TryRead returns false for a file that has already been consumed", () =>
                {
                    Temp.WithDir(root =>
                    {
                        Assert.False(CommandChannel.TryRead(Path.Combine(root, "gone.json"), out AgentCommand? command));
                        Assert.Null(command);
                    });
                }),

                TestCases.Create(SuiteId, "CommandArgumentGuards", "CommandChannel rejects null paths, null commands, and empty file paths", async ct =>
                {
                    S3DrivePaths paths = new S3DrivePaths(Path.Combine(Path.GetTempPath(), "unused"));
                    await Assert.ThrowsAsync<ArgumentNullException>(() => CommandChannel.SendAsync(null!, new AgentCommand(), ct)).ConfigureAwait(false);
                    await Assert.ThrowsAsync<ArgumentNullException>(() => CommandChannel.SendAsync(paths, null!, ct)).ConfigureAwait(false);
                    Assert.Throws<ArgumentNullException>(() => CommandChannel.ListPending(null!));
                    Assert.Throws<ArgumentException>(() => CommandChannel.TryRead(string.Empty, out _));
                    Assert.Throws<ArgumentException>(() => CommandChannel.TryRead(null!, out _));
                }),

                TestCases.Create(SuiteId, "StatusWriteRead", "StatusStore write and read round-trip", async ct =>
                {
                    await WithPathsAsync(async paths =>
                    {
                        AgentStatus status = new AgentStatus { ProcessId = 123 };
                        status.Drives.Add(new DriveStatus
                        {
                            DriveId = "drv_1",
                            Name = "Prod",
                            MountState = DriveMountStateEnum.Mounted,
                            DriveLetter = "S:\\"
                        });
                        status.Drives.Add(new DriveStatus
                        {
                            DriveId = "drv_2",
                            Name = "Broken",
                            MountState = DriveMountStateEnum.Failed,
                            LastError = "Access denied"
                        });
                        await StatusStore.WriteAsync(paths, status, ct).ConfigureAwait(false);

                        AgentStatus? back = await StatusStore.ReadAsync(paths, ct).ConfigureAwait(false);
                        Assert.NotNull(back);
                        Assert.Equal(123, back!.ProcessId);
                        Assert.Equal(2, back.Drives.Count);
                        Assert.Equal(DriveMountStateEnum.Mounted, back.Drives[0].MountState);
                        Assert.Equal("S:\\", back.Drives[0].DriveLetter);
                        Assert.Equal(DriveMountStateEnum.Failed, back.Drives[1].MountState);
                        Assert.Equal("Access denied", back.Drives[1].LastError);
                        Assert.Null(back.Drives[1].DriveLetter);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "StatusOverwrite", "StatusStore replaces the previous status and leaves no temp file", async ct =>
                {
                    await WithPathsAsync(async paths =>
                    {
                        await StatusStore.WriteAsync(paths, new AgentStatus { ProcessId = 1 }, ct).ConfigureAwait(false);
                        await StatusStore.WriteAsync(paths, new AgentStatus { ProcessId = 2 }, ct).ConfigureAwait(false);
                        AgentStatus? back = await StatusStore.ReadAsync(paths, ct).ConfigureAwait(false);
                        Assert.Equal(2, back!.ProcessId);
                        Assert.False(File.Exists(paths.StatusFile + ".tmp"));
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "StatusReadAbsent", "StatusStore read returns null when no status has been written", async ct =>
                {
                    await WithPathsAsync(async paths =>
                    {
                        Assert.Null(await StatusStore.ReadAsync(paths, ct).ConfigureAwait(false));
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "StatusReadMalformed", "StatusStore read returns null for a corrupt status file", async ct =>
                {
                    await WithPathsAsync(async paths =>
                    {
                        paths.EnsureDirectories();
                        await File.WriteAllTextAsync(paths.StatusFile, "{ \"ProcessId\": ", ct).ConfigureAwait(false);
                        Assert.Null(await StatusStore.ReadAsync(paths, ct).ConfigureAwait(false));
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "StatusArgumentGuards", "StatusStore rejects null paths and null status", async ct =>
                {
                    S3DrivePaths paths = new S3DrivePaths(Path.Combine(Path.GetTempPath(), "unused"));
                    await Assert.ThrowsAsync<ArgumentNullException>(() => StatusStore.WriteAsync(null!, new AgentStatus(), ct)).ConfigureAwait(false);
                    await Assert.ThrowsAsync<ArgumentNullException>(() => StatusStore.WriteAsync(paths, null!, ct)).ConfigureAwait(false);
                    await Assert.ThrowsAsync<ArgumentNullException>(() => StatusStore.ReadAsync(null!, ct)).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "StatusHonorsCancellation", "StatusStore write honors a canceled token", async ct =>
                {
                    await WithPathsAsync(async paths =>
                    {
                        using (CancellationTokenSource cts = new CancellationTokenSource())
                        {
                            cts.Cancel();
                            await Assert.ThrowsAsync<OperationCanceledException>(() => StatusStore.WriteAsync(paths, new AgentStatus(), cts.Token)).ConfigureAwait(false);
                        }

                        Assert.False(File.Exists(paths.StatusFile), "a canceled write must not publish a status file");
                    }).ConfigureAwait(false);
                })
            };

            return new TestSuiteDescriptor(SuiteId, "Agent coordination (IPC)", cases);
        }

        private static Task WithPathsAsync(Func<S3DrivePaths, Task> body)
        {
            return Temp.WithDirAsync(root => body(new S3DrivePaths(root)));
        }
    }
}
