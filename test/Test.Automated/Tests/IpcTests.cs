namespace Test.Automated.Tests
{
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Ipc;
    using Test.Automated.Harness;

    /// <summary>
    /// Tests for <see cref="CommandChannel"/> and <see cref="StatusStore"/>.
    /// </summary>
    public static class IpcTests
    {
        /// <summary>
        /// Registers the tests.
        /// </summary>
        /// <param name="runner">The runner.</param>
        public static void Register(TestRunner runner)
        {
            runner.Add("CommandChannel send, list, read", async () =>
            {
                string root = Temp.NewDir();
                try
                {
                    S3DrivePaths paths = new S3DrivePaths(root);
                    AgentCommand command = new AgentCommand { CommandType = AgentCommandTypeEnum.Mount, DriveId = "drv_1" };
                    await CommandChannel.SendAsync(paths, command, CancellationToken.None);

                    System.Collections.Generic.IReadOnlyList<string> pending = CommandChannel.ListPending(paths);
                    Assert.Equal(1, pending.Count);
                    Assert.True(CommandChannel.TryRead(pending[0], out AgentCommand? parsed));
                    Assert.NotNull(parsed);
                    Assert.Equal(AgentCommandTypeEnum.Mount, parsed!.CommandType);
                    Assert.Equal("drv_1", parsed.DriveId);
                }
                finally
                {
                    Temp.Delete(root);
                }
            });

            runner.Add("CommandChannel ListPending is empty by default", () =>
            {
                string root = Temp.NewDir();
                try
                {
                    S3DrivePaths paths = new S3DrivePaths(root);
                    Assert.Equal(0, CommandChannel.ListPending(paths).Count);
                }
                finally
                {
                    Temp.Delete(root);
                }
            });

            runner.Add("CommandChannel TryRead rejects malformed json", () =>
            {
                string root = Temp.NewDir();
                try
                {
                    string file = Path.Combine(root, "bad.json");
                    File.WriteAllText(file, "{ not json");
                    Assert.False(CommandChannel.TryRead(file, out AgentCommand? command));
                    Assert.Null(command);
                }
                finally
                {
                    Temp.Delete(root);
                }
            });

            runner.Add("StatusStore write and read", async () =>
            {
                string root = Temp.NewDir();
                try
                {
                    S3DrivePaths paths = new S3DrivePaths(root);
                    AgentStatus status = new AgentStatus { ProcessId = 123 };
                    status.Drives.Add(new DriveStatus
                    {
                        DriveId = "drv_1",
                        Name = "Prod",
                        MountState = DriveMountStateEnum.Mounted,
                        Shared = true,
                        ShareName = "S3Drive-Prod"
                    });
                    await StatusStore.WriteAsync(paths, status, CancellationToken.None);

                    AgentStatus? back = await StatusStore.ReadAsync(paths, CancellationToken.None);
                    Assert.NotNull(back);
                    Assert.Equal(123, back!.ProcessId);
                    Assert.Equal(1, back.Drives.Count);
                    Assert.Equal(DriveMountStateEnum.Mounted, back.Drives[0].MountState);
                    Assert.True(back.Drives[0].Shared);
                    Assert.Equal("S3Drive-Prod", back.Drives[0].ShareName);
                }
                finally
                {
                    Temp.Delete(root);
                }
            });

            runner.Add("StatusStore read returns null when absent", async () =>
            {
                string root = Temp.NewDir();
                try
                {
                    S3DrivePaths paths = new S3DrivePaths(root);
                    Assert.Null(await StatusStore.ReadAsync(paths, CancellationToken.None));
                }
                finally
                {
                    Temp.Delete(root);
                }
            });
        }
    }
}
