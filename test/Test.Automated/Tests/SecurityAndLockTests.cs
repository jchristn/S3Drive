namespace Test.Automated.Tests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Concurrency;
    using S3Drive.Core.Helpers;
    using Test.Automated.Harness;

    /// <summary>
    /// Tests for <see cref="IdGenerator"/>, <see cref="ObjectLocks"/>, and
    /// <see cref="AgentInstanceLock"/>.
    /// </summary>
    public static class SecurityAndLockTests
    {
        /// <summary>
        /// Registers the tests.
        /// </summary>
        /// <param name="runner">The runner.</param>
        public static void Register(TestRunner runner)
        {
            runner.Add("IdGenerator produces unique prefixed ids", () =>
            {
                string a = IdGenerator.GenerateDriveId();
                string b = IdGenerator.GenerateDriveId();
                Assert.True(a.StartsWith("drv_", StringComparison.Ordinal), "prefix");
                Assert.False(a == b, "unique");
            });

            runner.Add("IdGenerator clamps length", () =>
            {
                int original = IdGenerator.IdLength;
                try
                {
                    IdGenerator.IdLength = 1000;
                    Assert.Equal(64, IdGenerator.IdLength);
                    IdGenerator.IdLength = 1;
                    Assert.Equal(16, IdGenerator.IdLength);
                }
                finally
                {
                    IdGenerator.IdLength = original;
                }
            });

            runner.Add("ObjectLocks acquire and release", () =>
            {
                ObjectLocks locks = new ObjectLocks();
                Assert.False(locks.IsLocked("k"));
                using (locks.Acquire("k"))
                {
                    Assert.True(locks.IsLocked("k"));
                }

                Assert.False(locks.IsLocked("k"));
            });

            runner.Add("ObjectLocks serialize the same key", async () =>
            {
                ObjectLocks locks = new ObjectLocks();
                IDisposable held = locks.Acquire("k");
                bool blocked = false;
                using (CancellationTokenSource cts = new CancellationTokenSource(200))
                {
                    try
                    {
                        using (await locks.AcquireAsync("k", cts.Token))
                        {
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        blocked = true;
                    }
                }

                held.Dispose();
                Assert.True(blocked, "second acquisition should block while held");
            });

            runner.Add("ObjectLocks different keys do not block", async () =>
            {
                ObjectLocks locks = new ObjectLocks();
                using (locks.Acquire("a"))
                {
                    using (await locks.AcquireAsync("b", CancellationToken.None))
                    {
                        Assert.True(true);
                    }
                }
            });

            runner.Add("ObjectLocks null key throws", () =>
            {
                ObjectLocks locks = new ObjectLocks();
                Assert.Throws<ArgumentNullException>(() => locks.Acquire(null!));
            });

            runner.Add("AgentInstanceLock enforces single instance", () =>
            {
                string state = Temp.NewDir();
                try
                {
                    FileLockHandle? first = AgentInstanceLock.TryAcquire(state);
                    Assert.NotNull(first);
                    Assert.True(AgentInstanceLock.IsRunning(state));
                    FileLockHandle? second = AgentInstanceLock.TryAcquire(state);
                    Assert.Null(second);
                    first!.Dispose();
                    Assert.False(AgentInstanceLock.IsRunning(state));
                }
                finally
                {
                    Temp.Delete(state);
                }
            });
        }
    }
}
