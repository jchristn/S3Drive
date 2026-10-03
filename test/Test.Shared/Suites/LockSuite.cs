namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Concurrency;
    using S3Drive.Core.Helpers;
    using Test.Shared.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Tests for <see cref="IdGenerator"/>, <see cref="ObjectLocks"/>,
    /// <see cref="AgentInstanceLock"/>, and <see cref="FileLockHandle"/>.
    /// </summary>
    public static class LockSuite
    {
        private const string SuiteId = "Locks";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TestCases.Create(SuiteId, "IdGeneratorUniquePrefixed", "IdGenerator produces unique drv_-prefixed ids", () =>
                {
                    HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < 1000; i++)
                    {
                        string id = IdGenerator.GenerateDriveId();
                        Assert.True(id.StartsWith("drv_", StringComparison.Ordinal), "prefix");
                        Assert.True(seen.Add(id), "duplicate id " + id);
                    }
                }),

                TestCases.Create(SuiteId, "IdGeneratorClampsLength", "IdGenerator clamps the id length to 16..64", () =>
                {
                    int original = IdGenerator.IdLength;
                    try
                    {
                        IdGenerator.IdLength = 1000;
                        Assert.Equal(64, IdGenerator.IdLength);
                        IdGenerator.IdLength = 1;
                        Assert.Equal(16, IdGenerator.IdLength);
                        IdGenerator.IdLength = -1;
                        Assert.Equal(16, IdGenerator.IdLength);
                        IdGenerator.IdLength = 32;
                        Assert.Equal(32, IdGenerator.IdLength);
                    }
                    finally
                    {
                        IdGenerator.IdLength = original;
                    }
                }),

                TestCases.Create(SuiteId, "IdGeneratorHonorsLength", "IdGenerator produces longer ids for a longer configured length", () =>
                {
                    int original = IdGenerator.IdLength;
                    try
                    {
                        IdGenerator.IdLength = 16;
                        string shortId = IdGenerator.GenerateDriveId();
                        IdGenerator.IdLength = 48;
                        string longId = IdGenerator.GenerateDriveId();
                        Assert.True(longId.Length > shortId.Length, "expected " + longId + " to be longer than " + shortId);
                    }
                    finally
                    {
                        IdGenerator.IdLength = original;
                    }
                }),

                TestCases.Create(SuiteId, "ObjectLocksAcquireRelease", "ObjectLocks acquire and release a key", () =>
                {
                    ObjectLocks locks = new ObjectLocks();
                    Assert.False(locks.IsLocked("k"));
                    using (locks.Acquire("k"))
                    {
                        Assert.True(locks.IsLocked("k"));
                    }

                    Assert.False(locks.IsLocked("k"));
                }),

                TestCases.Create(SuiteId, "ObjectLocksAsyncAcquireRelease", "ObjectLocks async acquisition locks and releases the key", async ct =>
                {
                    ObjectLocks locks = new ObjectLocks();
                    using (await locks.AcquireAsync("k", ct).ConfigureAwait(false))
                    {
                        Assert.True(locks.IsLocked("k"));
                    }

                    Assert.False(locks.IsLocked("k"));
                    using (locks.Acquire("k"))
                    {
                        Assert.True(locks.IsLocked("k"), "re-acquire after release");
                    }
                }),

                TestCases.Create(SuiteId, "ObjectLocksSerializeSameKey", "ObjectLocks block a second acquisition of a held key", async ct =>
                {
                    ObjectLocks locks = new ObjectLocks();
                    IDisposable held = locks.Acquire("k");
                    bool blocked = false;
                    using (CancellationTokenSource cts = new CancellationTokenSource(200))
                    {
                        try
                        {
                            using (await locks.AcquireAsync("k", cts.Token).ConfigureAwait(false))
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
                }),

                TestCases.Create(SuiteId, "ObjectLocksWaiterProceedsAfterRelease", "ObjectLocks let a waiter proceed once the holder releases", async ct =>
                {
                    ObjectLocks locks = new ObjectLocks();
                    IDisposable held = locks.Acquire("k");
                    Task<IDisposable> waiter = locks.AcquireAsync("k", ct).AsTask();
                    await Task.Delay(50, ct).ConfigureAwait(false);
                    Assert.False(waiter.IsCompleted, "waiter must not complete while the key is held");

                    held.Dispose();
                    Task finished = await Task.WhenAny(waiter, Task.Delay(5000, ct)).ConfigureAwait(false);
                    Assert.True(ReferenceEquals(finished, waiter), "waiter should acquire after release");
                    (await waiter.ConfigureAwait(false)).Dispose();
                }),

                TestCases.Create(SuiteId, "ObjectLocksMutualExclusion", "ObjectLocks guarantee mutual exclusion under contention", async ct =>
                {
                    ObjectLocks locks = new ObjectLocks();
                    int inside = 0;
                    int maxInside = 0;
                    List<Task> workers = new List<Task>();
                    for (int i = 0; i < 16; i++)
                    {
                        workers.Add(Task.Run(async () =>
                        {
                            for (int j = 0; j < 10; j++)
                            {
                                using (await locks.AcquireAsync("shared", ct).ConfigureAwait(false))
                                {
                                    int now = Interlocked.Increment(ref inside);
                                    InterlockedMax(ref maxInside, now);
                                    await Task.Yield();
                                    Interlocked.Decrement(ref inside);
                                }
                            }
                        }, ct));
                    }

                    await Task.WhenAll(workers).ConfigureAwait(false);
                    Assert.Equal(1, maxInside, "at most one holder at a time");
                    Assert.False(locks.IsLocked("shared"));
                }),

                TestCases.Create(SuiteId, "ObjectLocksDifferentKeysIndependent", "ObjectLocks on different keys do not block each other", async ct =>
                {
                    ObjectLocks locks = new ObjectLocks();
                    using (locks.Acquire("a"))
                    {
                        using (CancellationTokenSource cts = new CancellationTokenSource(2000))
                        {
                            using (await locks.AcquireAsync("b", cts.Token).ConfigureAwait(false))
                            {
                                Assert.True(locks.IsLocked("a"));
                                Assert.True(locks.IsLocked("b"));
                            }
                        }
                    }
                }),

                TestCases.Create(SuiteId, "ObjectLocksKeysAreCaseSensitive", "ObjectLocks treat keys that differ only by case as distinct objects", () =>
                {
                    ObjectLocks locks = new ObjectLocks();
                    using (locks.Acquire("File.txt"))
                    {
                        Assert.False(locks.IsLocked("file.txt"));
                    }
                }),

                TestCases.Create(SuiteId, "ObjectLocksNullKeyThrows", "ObjectLocks reject a null key", () =>
                {
                    ObjectLocks locks = new ObjectLocks();
                    Assert.Throws<ArgumentNullException>(() => locks.Acquire(null!));
                    Assert.Throws<ArgumentNullException>(() => locks.AcquireAsync(null!));
                    Assert.Throws<ArgumentNullException>(() => locks.IsLocked(null!));
                }),

                TestCases.Create(SuiteId, "AgentInstanceLockSingleInstance", "AgentInstanceLock enforces a single instance", () =>
                {
                    Temp.WithDir(state =>
                    {
                        FileLockHandle? first = AgentInstanceLock.TryAcquire(state);
                        Assert.NotNull(first);
                        Assert.True(AgentInstanceLock.IsRunning(state));
                        FileLockHandle? second = AgentInstanceLock.TryAcquire(state);
                        Assert.Null(second);
                        first!.Dispose();
                        Assert.False(AgentInstanceLock.IsRunning(state));
                    });
                }),

                TestCases.Create(SuiteId, "AgentInstanceLockReacquire", "AgentInstanceLock can be re-acquired after release", () =>
                {
                    Temp.WithDir(state =>
                    {
                        FileLockHandle? first = AgentInstanceLock.TryAcquire(state);
                        Assert.NotNull(first);
                        first!.Dispose();
                        FileLockHandle? second = AgentInstanceLock.TryAcquire(state);
                        Assert.NotNull(second);
                        second!.Dispose();
                    });
                }),

                TestCases.Create(SuiteId, "AgentInstanceLockCreatesStateDirectory", "AgentInstanceLock creates a missing state directory", () =>
                {
                    Temp.WithDir(root =>
                    {
                        string state = Path.Combine(root, "state");
                        Assert.False(AgentInstanceLock.IsRunning(state));
                        Assert.True(File.Exists(Path.Combine(state, AgentInstanceLock.LockFileName)));
                    });
                }),

                TestCases.Create(SuiteId, "AgentInstanceLockRejectsEmptyDirectory", "AgentInstanceLock rejects an empty state directory", () =>
                {
                    Assert.Throws<ArgumentException>(() => AgentInstanceLock.TryAcquire(string.Empty));
                    Assert.Throws<ArgumentException>(() => AgentInstanceLock.IsRunning(null!));
                }),

                TestCases.Create(SuiteId, "FileLockHandleGuardsAndDoubleDispose", "FileLockHandle rejects null and tolerates double dispose", () =>
                {
                    Assert.Throws<ArgumentNullException>(() => new FileLockHandle(null!));
                    Temp.WithDir(state =>
                    {
                        FileLockHandle? handle = AgentInstanceLock.TryAcquire(state);
                        Assert.NotNull(handle);
                        handle!.Dispose();
                        handle.Dispose();
                    });
                })
            };

            return new TestSuiteDescriptor(SuiteId, "Locks and identifiers", cases);
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current = Volatile.Read(ref target);
            while (value > current)
            {
                int observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current) return;
                current = observed;
            }
        }
    }
}
