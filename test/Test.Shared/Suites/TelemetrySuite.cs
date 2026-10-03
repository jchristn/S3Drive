namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using DokanNet;
    using S3Drive.Core.Concurrency;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Diagnostics;
    using S3Drive.Core.FileSystem;
    using S3Drive.Core.Ipc;
    using S3Drive.Core.Mounting;
    using S3Drive.Core.Security;
    using S3Drive.Core.Serialization;
    using S3Drive.Core.Storage;
    using S3Drive.Core.Telemetry;
    using Test.Shared.Fakes;
    using Test.Shared.Helpers;
    using Touchstone.Core;
    using FA = DokanNet.FileAccess;
    using N = S3Drive.Core.Telemetry.TelemetryNames;

    /// <summary>
    /// Proves S3Drive telemetry is emitted: the metric catalog matches the instruments, every
    /// inventory category (filesystem operations and stages, S3 integration, cache, locks, mount
    /// lifecycle, the command pipeline, IPC and configuration I/O, logging, build and config
    /// gauges) records metrics and spans, failure paths carry outcome and error type, trace context
    /// crosses the command-file boundary, labels stay bounded, and emission without a listener is safe.
    /// </summary>
    public static class TelemetrySuite
    {
        private const string SuiteId = "Telemetry";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <param name="storage">The storage integration configuration; live cases are skipped when it is not enabled.</param>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build(StorageTestConfig storage)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));

            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            AddCatalogCases(cases);
            AddSafetyCases(cases);
            AddFileSystemCases(cases);
            AddStorageCases(cases);
            AddCacheAndLockCases(cases);
            AddMountCases(cases);
            AddCommandCases(cases);
            AddIpcAndLoggingCases(cases);
            AddSettingsCases(cases);
            AddLiveCases(cases, storage);
            return new TestSuiteDescriptor(SuiteId, "Telemetry (metrics, traces, propagation)", cases);
        }

        private static void AddCatalogCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(TestCases.Create(SuiteId, "CatalogMatchesInstruments", "Every instrument on the S3Drive meter is in the catalog with the same unit, and vice versa", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    Assert.NotNull(S3DriveTelemetry.Meter);
                    IReadOnlyDictionary<string, string?> published = capture.Published;
                    foreach (MetricDescriptor descriptor in S3DriveMetricCatalog.All)
                    {
                        Assert.True(published.ContainsKey(descriptor.Name), "instrument not published: " + descriptor.Name);
                        Assert.Equal(descriptor.Unit, published[descriptor.Name], "unit mismatch for " + descriptor.Name);
                    }

                    foreach (string name in published.Keys)
                    {
                        Assert.NotNull(S3DriveMetricCatalog.Find(name), "published instrument missing from catalog: " + name);
                    }
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "CatalogConventions", "Catalog names are unique, product-prefixed, durations are seconds histograms with ascending buckets", () =>
            {
                HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
                foreach (MetricDescriptor descriptor in S3DriveMetricCatalog.All)
                {
                    Assert.True(names.Add(descriptor.Name), "duplicate " + descriptor.Name);
                    Assert.True(descriptor.Name.StartsWith("s3drive.", StringComparison.Ordinal), "unprefixed " + descriptor.Name);
                    if (descriptor.Kind == MetricKindEnum.Histogram)
                    {
                        Assert.Equal("s", descriptor.Unit);
                        Assert.NotNull(descriptor.Buckets);
                        for (int i = 1; i < descriptor.Buckets!.Length; i++) Assert.True(descriptor.Buckets[i] > descriptor.Buckets[i - 1], "buckets not ascending for " + descriptor.Name);
                    }
                    else
                    {
                        Assert.Null(descriptor.Buckets);
                    }

                    foreach (string label in descriptor.LabelKeys)
                    {
                        Assert.False(label == N.AttrDriveId || label == N.AttrObjectKey || label == N.AttrBucket, "unbounded label " + label + " on " + descriptor.Name);
                    }
                }

                Assert.Null(S3DriveMetricCatalog.Find("s3drive.not.a.metric"));
                Assert.Null(S3DriveMetricCatalog.Find(null));
                Assert.Throws<ArgumentException>(() => new MetricDescriptor(string.Empty, MetricKindEnum.Counter, "1", "d", Array.Empty<string>(), null));
                Assert.Throws<ArgumentNullException>(() => new MetricDescriptor("x", MetricKindEnum.Counter, "1", "d", null!, null));
            }));

            cases.Add(TestCases.Create(SuiteId, "SourceNamesStable", "Meter and activity source names and the version are stable public contract", () =>
            {
                Assert.Equal("S3Drive", N.MeterName);
                Assert.Equal("S3Drive", N.ActivitySourceName);
                Assert.Equal(N.MeterName, S3DriveTelemetry.Meter.Name);
                Assert.Equal(N.ActivitySourceName, S3DriveTelemetry.ActivitySource.Name);
                Assert.False(string.IsNullOrEmpty(S3DriveTelemetry.Version));
                Assert.False(S3DriveTelemetry.Version.Contains('+'), "version must not carry build metadata");
            }));
        }

        private static void AddSafetyCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(TestCases.Create(SuiteId, "NoListenerDoesNotThrow", "Every instrumented path runs without any listener subscribed", async ct =>
            {
                WithFs("N", 60, (fs, store) => WriteThenRead(fs, "\\quiet.txt", "hello"));

                MetadataCache cache = new MetadataCache(0, "N");
                Assert.False(cache.TryGetHead("k", out _));
                cache.InvalidatePrefix("p/");
                cache.Clear();

                ObjectLocks locks = new ObjectLocks("N");
                using (locks.Acquire("k"))
                {
                }

                using (await locks.AcquireAsync("k", ct).ConfigureAwait(false))
                {
                }

                S3DriveTelemetry.SetConfigSnapshot(-5, -1, 9);
                S3DriveLog.Debug("telemetry no-listener check");

                await Temp.WithDirAsync(async root =>
                {
                    S3DrivePaths paths = new S3DrivePaths(root);
                    await CommandChannel.SendAsync(paths, new AgentCommand { CommandType = AgentCommandTypeEnum.Reload }, ct).ConfigureAwait(false);
                    int processed = await CommandDispatcher.DrainAsync(paths, (command, token) => Task.CompletedTask, ct).ConfigureAwait(false);
                    Assert.Equal(1, processed);
                }).ConfigureAwait(false);
            }));

            cases.Add(TestCases.Create(SuiteId, "LabelsBoundedAndKeyFree", "Every metric label is declared in the catalog and no metric or span carries an object key by default", () =>
            {
                const string Secret = "telemetry-private-name";
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    Assert.False(S3DriveTelemetry.IncludeObjectKeys);
                    WithFs("B", 60, (fs, store) =>
                    {
                        WriteThenRead(fs, "\\" + Secret + ".txt", "payload");
                        IList<FileInformation> files;
                        fs.FindFiles("\\", out files, new FakeDokanFileInfo());
                        fs.MoveFile("\\" + Secret + ".txt", "\\" + Secret + "-2.txt", false, new FakeDokanFileInfo());
                    });

                    Assert.True(capture.All.Count > 0);
                    foreach (CapturedMeasurement measurement in capture.All)
                    {
                        MetricDescriptor? descriptor = S3DriveMetricCatalog.Find(measurement.Name);
                        Assert.NotNull(descriptor, "uncatalogued " + measurement.Name);
                        foreach (KeyValuePair<string, string?> tag in measurement.Tags)
                        {
                            Assert.True(ContainsLabel(descriptor!, tag.Key), "undeclared label " + tag.Key + " on " + measurement.Name);
                            Assert.False((tag.Value ?? string.Empty).Contains(Secret), "object key leaked into " + measurement.Name);
                        }
                    }

                    foreach (Activity span in capture.AllSpans)
                    {
                        foreach (KeyValuePair<string, object?> tag in span.TagObjects)
                        {
                            Assert.False((Convert.ToString(tag.Value) ?? string.Empty).Contains(Secret), "object key leaked into span " + span.DisplayName + " tag " + tag.Key);
                        }
                    }
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "ObjectKeysOptIn", "IncludeObjectKeys adds s3drive.object.key to spans but never to metrics", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    S3DriveTelemetry.IncludeObjectKeys = true;
                    try
                    {
                        WithFs("O", 0, (fs, store) => WriteThenRead(fs, "\\dir\\opt-in.txt", "x"));
                    }
                    finally
                    {
                        S3DriveTelemetry.IncludeObjectKeys = false;
                    }

                    Assert.True(capture.Spans(N.SpanFsPrefix + N.FsCreateFile, N.AttrDrive + "=O", N.AttrObjectKey + "=dir/opt-in.txt").Count >= 1);
                    Assert.True(capture.Spans(N.SpanS3Prefix + N.S3PutObject, N.AttrDrive + "=O", N.AttrObjectKey + "=dir/opt-in.txt").Count >= 1);
                    foreach (CapturedMeasurement measurement in capture.All)
                    {
                        Assert.Null(measurement.Tag(N.AttrObjectKey));
                    }
                }
            }));
        }

        private static void AddFileSystemCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(TestCases.Create(SuiteId, "FsWriteReadMetricsAndNestedSpans", "A write then read records fs/stage/S3 metrics and nests S3 client spans under stage and fs spans", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    WithFs("Q", 60, (fs, store) => WriteThenRead(fs, "\\a.txt", "hello world"));

                    Assert.True(capture.Sum(N.FsOperations, N.AttrDrive + "=Q", N.AttrFsOperation + "=" + N.FsCreateFile, N.AttrOutcome + "=success") >= 2);
                    Assert.Equal(2.0, capture.Sum(N.FsOperations, N.AttrDrive + "=Q", N.AttrFsOperation + "=" + N.FsCleanup, N.AttrOutcome + "=success"));
                    Assert.Equal(11.0, capture.Sum(N.FsBytes, N.AttrDrive + "=Q", N.AttrDirection + "=write"));
                    Assert.Equal(11.0, capture.Sum(N.FsBytes, N.AttrDrive + "=Q", N.AttrDirection + "=read"));
                    Assert.True(capture.Find(N.FsOperationDuration, N.AttrDrive + "=Q", N.AttrFsOperation + "=" + N.FsReadFile).Count >= 1);
                    Assert.True(capture.Find(N.FsStageDuration, N.AttrDrive + "=Q", N.AttrStage + "=" + N.StageUpload, N.AttrOutcome + "=success").Count == 1);
                    Assert.True(capture.Find(N.FsStageDuration, N.AttrDrive + "=Q", N.AttrStage + "=" + N.StageDownload, N.AttrOutcome + "=success").Count == 1);
                    Assert.Equal(1.0, capture.Sum(N.S3Requests, N.AttrDrive + "=Q", N.AttrS3Operation + "=" + N.S3PutObject, N.AttrOutcome + "=success", N.AttrProvider + "=s3_compatible"));
                    Assert.Equal(1.0, capture.Sum(N.S3Requests, N.AttrDrive + "=Q", N.AttrS3Operation + "=" + N.S3GetObject, N.AttrOutcome + "=success"));
                    Assert.Equal(11.0, capture.Sum(N.S3Bytes, N.AttrDrive + "=Q", N.AttrDirection + "=upload"));
                    Assert.Equal(11.0, capture.Sum(N.S3Bytes, N.AttrDrive + "=Q", N.AttrDirection + "=download"));
                    Assert.Equal(0.0, capture.Sum(N.FsOpenHandles, N.AttrDrive + "=Q"), "every opened handle was closed");
                    Assert.Equal(0.0, capture.Sum(N.LockHeld, N.AttrDrive + "=Q"), "every lock was released");

                    Activity put = Single(capture.Spans(N.SpanS3Prefix + N.S3PutObject, N.AttrDrive + "=Q"));
                    Activity upload = Single(capture.Spans(N.SpanStagePrefix + N.StageUpload, N.AttrDrive + "=Q"));
                    Activity cleanup = Single(capture.Spans(N.SpanFsPrefix + N.FsCleanup, N.AttrDrive + "=Q"));
                    Assert.Equal(ActivityKind.Client, put.Kind);
                    Assert.Equal(ActivityKind.Server, cleanup.Kind);
                    Assert.Equal(upload.SpanId, put.ParentSpanId, "S3 span nests under the upload stage");
                    Assert.Equal(cleanup.SpanId, upload.ParentSpanId, "upload stage nests under fs cleanup");
                    Assert.Equal(ActivityStatusCode.Ok, put.Status);
                    Assert.Equal("aws-api", Convert.ToString(put.GetTagItem(N.AttrRpcSystem)));
                    Assert.Equal("bucket", Convert.ToString(put.GetTagItem(N.AttrBucket)));
                    Assert.True(capture.Spans(N.SpanStagePrefix + N.StageLockWait, N.AttrDrive + "=Q").Count >= 2);

                    Activity get = Single(capture.Spans(N.SpanS3Prefix + N.S3GetObject, N.AttrDrive + "=Q"));
                    Activity download = Single(capture.Spans(N.SpanStagePrefix + N.StageDownload, N.AttrDrive + "=Q"));
                    Activity read = Single(capture.Spans(N.SpanFsPrefix + N.FsReadFile, N.AttrDrive + "=Q"));
                    Assert.Equal(download.SpanId, get.ParentSpanId);
                    Assert.Equal(read.SpanId, download.ParentSpanId, "the first read (which stages the object) is a span; later chunk reads are metrics only");
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "FsNotFoundOutcome", "Opening a missing file records not_found with the NTSTATUS as error.type", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    WithFs("F", 0, (fs, store) =>
                    {
                        Assert.Equal(NtStatus.ObjectNameNotFound, Open(fs, "\\missing.txt", FA.ReadData, FileMode.Open, new FakeDokanFileInfo()));
                    });

                    Assert.Equal(1.0, capture.Sum(N.FsOperations, N.AttrDrive + "=F", N.AttrFsOperation + "=" + N.FsCreateFile, N.AttrOutcome + "=not_found", N.AttrErrorType + "=ObjectNameNotFound"));
                    Assert.True(capture.Sum(N.S3Requests, N.AttrDrive + "=F", N.AttrS3Operation + "=" + N.S3HeadObject, N.AttrOutcome + "=not_found") >= 1);
                    Activity span = Single(capture.Spans(N.SpanFsPrefix + N.FsCreateFile, N.AttrDrive + "=F"));
                    Assert.Equal("ObjectNameNotFound", Convert.ToString(span.GetTagItem(N.AttrNtStatus)));
                    Assert.Equal(ActivityStatusCode.Ok, span.Status, "not_found is an expected outcome, not a span error");
                    Assert.Equal(0.0, capture.Sum(N.FsOpenHandles, N.AttrDrive + "=F"), "a failed open holds no handle");
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "FsStoreFailureRecordsErrorType", "A store failure maps to outcome=error with the exception type on fs and S3 metrics and an exception event on the spans", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    WithFs("E", 0, (fs, store) =>
                    {
                        store.FailWith = new IOException("endpoint unreachable");
                        IList<FileInformation> files;
                        Assert.Equal(NtStatus.Error, fs.FindFiles("\\", out files, new FakeDokanFileInfo()));
                    });

                    Assert.Equal(1.0, capture.Sum(N.FsOperations, N.AttrDrive + "=E", N.AttrFsOperation + "=" + N.FsFindFiles, N.AttrOutcome + "=error", N.AttrErrorType + "=IOException"));
                    Assert.Equal(1.0, capture.Sum(N.S3Requests, N.AttrDrive + "=E", N.AttrS3Operation + "=" + N.S3ListObjects, N.AttrOutcome + "=error", N.AttrErrorType + "=IOException"));
                    Activity fsSpan = Single(capture.Spans(N.SpanFsPrefix + N.FsFindFiles, N.AttrDrive + "=E"));
                    Activity s3Span = Single(capture.Spans(N.SpanS3Prefix + N.S3ListObjects, N.AttrDrive + "=E"));
                    Assert.Equal(ActivityStatusCode.Error, fsSpan.Status);
                    Assert.Equal(ActivityStatusCode.Error, s3Span.Status);
                    Assert.True(HasExceptionEvent(fsSpan) && HasExceptionEvent(s3Span), "both spans carry the exception event");
                    Assert.Equal(fsSpan.SpanId, s3Span.ParentSpanId);
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "FsCancelledOutcome", "Operations aborted by an unmount (cancelled token) record outcome=cancelled, not error", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    cts.Cancel();
                    WithFs("X", 0, (fs, store) =>
                    {
                        IList<FileInformation> files;
                        Assert.Equal(NtStatus.Unsuccessful, fs.FindFiles("\\", out files, new FakeDokanFileInfo()));
                    }, cts.Token);

                    Assert.Equal(1.0, capture.Sum(N.FsOperations, N.AttrDrive + "=X", N.AttrFsOperation + "=" + N.FsFindFiles, N.AttrOutcome + "=cancelled"));
                    Assert.Equal(1.0, capture.Sum(N.S3Requests, N.AttrDrive + "=X", N.AttrOutcome + "=cancelled"));
                    Assert.Equal(ActivityStatusCode.Unset, Single(capture.Spans(N.SpanFsPrefix + N.FsFindFiles, N.AttrDrive + "=X")).Status);
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "FsCleanupFailureRecorded", "A failed upload on close is recorded (cleanup error, upload stage error, warning logged) instead of vanishing", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    WithFs("W", 0, (fs, store) =>
                    {
                        FakeDokanFileInfo info = new FakeDokanFileInfo();
                        Assert.Equal(NtStatus.Success, Open(fs, "\\lost.txt", FA.WriteData, FileMode.CreateNew, info));
                        byte[] data = Encoding.UTF8.GetBytes("data");
                        Assert.Equal(NtStatus.Success, fs.WriteFile("\\lost.txt", data, out int written, 0, info));
                        store.FailWith = new UnauthorizedAccessException("denied");
                        fs.Cleanup("\\lost.txt", info);
                        fs.CloseFile("\\lost.txt", info);
                    });

                    Assert.Equal(1.0, capture.Sum(N.FsOperations, N.AttrDrive + "=W", N.AttrFsOperation + "=" + N.FsCleanup, N.AttrOutcome + "=error", N.AttrErrorType + "=UnauthorizedAccessException"));
                    Assert.Equal(1, capture.Find(N.FsStageDuration, N.AttrDrive + "=W", N.AttrStage + "=" + N.StageUpload, N.AttrOutcome + "=error").Count);
                    Assert.True(capture.Sum(N.LogMessages, N.AttrSeverity + "=Warn") >= 1);
                    Assert.Equal(ActivityStatusCode.Error, Single(capture.Spans(N.SpanFsPrefix + N.FsCleanup, N.AttrDrive + "=W")).Status);
                    Assert.Equal(0.0, capture.Sum(N.LockHeld, N.AttrDrive + "=W"), "the lock is released even when the upload fails");
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "FsDirectoryMoveStages", "A directory rename records list_all, copy, and delete stages and the key count", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    WithFs("D", 0, (fs, store) =>
                    {
                        store.Seed("src/a.txt", Encoding.UTF8.GetBytes("a"));
                        store.Seed("src/b.txt", Encoding.UTF8.GetBytes("b"));
                        Assert.Equal(NtStatus.Success, fs.MoveFile("\\src", "\\dst", false, new FakeDokanFileInfo()));
                    });

                    foreach (string stage in new string[] { N.StageListAll, N.StageCopy, N.StageDelete })
                    {
                        Assert.Equal(1, capture.Find(N.FsStageDuration, N.AttrDrive + "=D", N.AttrStage + "=" + stage, N.AttrOutcome + "=success").Count, "stage " + stage);
                    }

                    Assert.Equal(2.0, capture.Sum(N.S3Requests, N.AttrDrive + "=D", N.AttrS3Operation + "=" + N.S3CopyObject, N.AttrOutcome + "=success"));
                    Activity move = Single(capture.Spans(N.SpanFsPrefix + N.FsMoveFile, N.AttrDrive + "=D"));
                    Assert.Equal("2", Convert.ToString(move.GetTagItem(N.AttrCount)));
                    Activity deleteMany = Single(capture.Spans(N.SpanS3Prefix + N.S3DeleteObjects, N.AttrDrive + "=D"));
                    Assert.Equal("2", Convert.ToString(deleteMany.GetTagItem(N.AttrCount)));
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "FsMkdirAndDeleteStages", "Creating and deleting a directory records mkdir and delete stages", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    WithFs("K", 0, (fs, store) =>
                    {
                        FakeDokanFileInfo info = new FakeDokanFileInfo { IsDirectory = true };
                        Assert.Equal(NtStatus.Success, Open(fs, "\\newdir", FA.GenericAll, FileMode.CreateNew, info));
                        Assert.Equal(NtStatus.Success, fs.DeleteDirectory("\\newdir", info));
                        fs.Cleanup("\\newdir", info);
                        fs.CloseFile("\\newdir", info);
                    });

                    Assert.Equal(1, capture.Find(N.FsStageDuration, N.AttrDrive + "=K", N.AttrStage + "=" + N.StageMkdir, N.AttrOutcome + "=success").Count);
                    Assert.Equal(1, capture.Find(N.FsStageDuration, N.AttrDrive + "=K", N.AttrStage + "=" + N.StageDelete, N.AttrOutcome + "=success").Count);
                    Assert.Equal(1.0, capture.Sum(N.FsOperations, N.AttrDrive + "=K", N.AttrFsOperation + "=" + N.FsDeleteDirectory, N.AttrOutcome + "=success"));
                }
            }));
        }

        private static void AddStorageCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(TestCases.Create(SuiteId, "S3DecoratorOutcomesAndBytes", "The instrumented store records outcome, bytes, and counts per operation", async ct =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    FakeS3Store fake = new FakeS3Store();
                    InstrumentedS3Store store = new InstrumentedS3Store(fake, "s:", S3ProviderEnum.AwsS3, "b");
                    Assert.True(ReferenceEquals(fake, store.Inner));

                    Assert.Null(await store.HeadAsync("missing", ct).ConfigureAwait(false));
                    await store.PutAsync("k", new byte[] { 1, 2, 3 }, ct).ConfigureAwait(false);
                    byte[] got = await store.GetAsync("k", ct).ConfigureAwait(false);
                    Assert.Equal(3, got.Length);
                    Assert.True(await store.ExistsAsync("k", ct).ConfigureAwait(false));
                    await store.CopyAsync("k", "k2", ct).ConfigureAwait(false);
                    IReadOnlyList<string> keys = await store.ListAllKeysAsync(string.Empty, ct).ConfigureAwait(false);
                    await store.DeleteManyAsync(new List<string>(keys), ct).ConfigureAwait(false);
                    await store.DeleteAsync("gone", ct).ConfigureAwait(false);
                    fake.ConnectivityResult = false;
                    Assert.False(await store.ValidateConnectivityAsync(ct).ConfigureAwait(false));

                    Assert.Equal(1.0, capture.Sum(N.S3Requests, N.AttrDrive + "=S", N.AttrProvider + "=aws_s3", N.AttrS3Operation + "=" + N.S3HeadObject, N.AttrOutcome + "=not_found"));
                    Assert.Equal(3.0, capture.Sum(N.S3Bytes, N.AttrDrive + "=S", N.AttrDirection + "=upload"));
                    Assert.Equal(3.0, capture.Sum(N.S3Bytes, N.AttrDrive + "=S", N.AttrDirection + "=download"));
                    foreach (string op in new string[] { N.S3PutObject, N.S3GetObject, N.S3ObjectExists, N.S3CopyObject, N.S3ListAllKeys, N.S3DeleteObjects, N.S3DeleteObject })
                    {
                        Assert.Equal(1.0, capture.Sum(N.S3Requests, N.AttrDrive + "=S", N.AttrS3Operation + "=" + op, N.AttrOutcome + "=success"), "operation " + op);
                        Assert.Equal(1, capture.Find(N.S3RequestDuration, N.AttrDrive + "=S", N.AttrS3Operation + "=" + op).Count, "latency " + op);
                    }

                    Assert.Equal(1.0, capture.Sum(N.S3Requests, N.AttrDrive + "=S", N.AttrS3Operation + "=" + N.S3ValidateConnectivity, N.AttrOutcome + "=error", N.AttrErrorType + "=ConnectivityFailed"));
                    Assert.Equal("2", Convert.ToString(Single(capture.Spans(N.SpanS3Prefix + N.S3DeleteObjects, N.AttrDrive + "=S")).GetTagItem(N.AttrCount)));
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "S3DecoratorPreservesExceptions", "The instrumented store rethrows the original exception and records it", async ct =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    FakeS3Store fake = new FakeS3Store();
                    fake.FailWith = new InvalidOperationException("boom");
                    InstrumentedS3Store store = new InstrumentedS3Store(fake, "T", S3ProviderEnum.S3Compatible, null);

                    await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAsync("k", ct)).ConfigureAwait(false);
                    await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListAsync(string.Empty, ct)).ConfigureAwait(false);
                    Assert.Throws<ArgumentNullException>(() => new InstrumentedS3Store(null!, "T", S3ProviderEnum.AwsS3, null));

                    Assert.Equal(2.0, capture.Sum(N.S3Requests, N.AttrDrive + "=T", N.AttrOutcome + "=error", N.AttrErrorType + "=InvalidOperationException"));
                    Activity span = Single(capture.Spans(N.SpanS3Prefix + N.S3GetObject, N.AttrDrive + "=T"));
                    Assert.Equal(ActivityStatusCode.Error, span.Status);
                    Assert.True(HasExceptionEvent(span));
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "BlobStoreSuppressedHeadFailure", "A HEAD that fails for a reason other than not-found is still reported absent but counted as a suppressed S3 error", async ct =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    DriveProfile profile = new DriveProfile
                    {
                        Provider = S3ProviderEnum.S3Compatible,
                        ServiceUrl = "http://127.0.0.1:1/",
                        UseSsl = false,
                        UsePathStyle = true,
                        Bucket = "b",
                        AccessKey = "a",
                        DriveLetter = "U:"
                    };

                    using (BlobS3Store store = new BlobS3Store(profile, "secret"))
                    {
                        Assert.Null(await store.HeadAsync("k", ct).ConfigureAwait(false));
                    }

                    IReadOnlyList<CapturedMeasurement> suppressed = capture.Find(N.S3SuppressedErrors, N.AttrDrive + "=U", N.AttrS3Operation + "=" + N.S3HeadObject);
                    Assert.Equal(1, suppressed.Count);
                    Assert.False(string.IsNullOrEmpty(suppressed[0].Tag(N.AttrErrorType)));
                }
            }));
        }

        private static void AddCacheAndLockCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(TestCases.Create(SuiteId, "CacheLookupsEntriesInvalidations", "The metadata cache records hits, misses, entry deltas, and invalidations by scope", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    MetadataCache cache = new MetadataCache(60, "C");
                    Assert.False(cache.TryGetHead("p/k", out _));
                    cache.SetHead("p/k", new S3Entry { Key = "p/k" });
                    cache.SetHead("p/k", new S3Entry { Key = "p/k" });
                    Assert.True(cache.TryGetHead("p/k", out _));
                    cache.SetListing("p/", new List<S3Entry>());
                    Assert.True(cache.TryGetListing("p/", out _));
                    Assert.False(cache.TryGetListing("q/", out _));

                    Assert.Equal(1.0, capture.Sum(N.CacheEntries, N.AttrDrive + "=C", N.AttrCacheKind + "=head"), "overwriting a key does not double-count");
                    Assert.Equal(1.0, capture.Sum(N.CacheEntries, N.AttrDrive + "=C", N.AttrCacheKind + "=listing"));

                    cache.InvalidateKey("p/k");
                    cache.SetHead("r/x", null);
                    cache.InvalidatePrefix("r/");
                    cache.SetHead("z", null);
                    cache.Clear();

                    Assert.Equal(1.0, capture.Sum(N.CacheLookups, N.AttrDrive + "=C", N.AttrCacheKind + "=head", N.AttrCacheResult + "=hit"));
                    Assert.Equal(1.0, capture.Sum(N.CacheLookups, N.AttrDrive + "=C", N.AttrCacheKind + "=head", N.AttrCacheResult + "=miss"));
                    Assert.Equal(1.0, capture.Sum(N.CacheLookups, N.AttrDrive + "=C", N.AttrCacheKind + "=listing", N.AttrCacheResult + "=hit"));
                    Assert.Equal(1.0, capture.Sum(N.CacheLookups, N.AttrDrive + "=C", N.AttrCacheKind + "=listing", N.AttrCacheResult + "=miss"));
                    Assert.Equal(0.0, capture.Sum(N.CacheEntries, N.AttrDrive + "=C"), "entries return to zero after invalidation and clear");
                    Assert.Equal(1.0, capture.Sum(N.CacheInvalidations, N.AttrDrive + "=C", N.AttrCacheScope + "=key"));
                    Assert.Equal(1.0, capture.Sum(N.CacheInvalidations, N.AttrDrive + "=C", N.AttrCacheScope + "=prefix"));
                    Assert.Equal(1.0, capture.Sum(N.CacheInvalidations, N.AttrDrive + "=C", N.AttrCacheScope + "=clear"));
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "CacheDisabledBypass", "A disabled cache records lookups as bypass", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    MetadataCache cache = new MetadataCache(0, "Y");
                    Assert.False(cache.TryGetHead("k", out _));
                    Assert.False(cache.TryGetListing("p/", out _));
                    Assert.Equal(2.0, capture.Sum(N.CacheLookups, N.AttrDrive + "=Y", N.AttrCacheResult + "=bypass"));
                    Assert.Equal(0, capture.Find(N.CacheEntries, N.AttrDrive + "=Y").Count);
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "LockWaitHeldAndContention", "Per-object locks record wait time, held count, and contention", async ct =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    ObjectLocks locks = new ObjectLocks("L");
                    IDisposable first = locks.Acquire("k");
                    Assert.Equal(1.0, capture.Sum(N.LockHeld, N.AttrDrive + "=L"));

                    Task waiter = Task.Run(() =>
                    {
                        using (locks.Acquire("k"))
                        {
                        }
                    });

                    await Task.Delay(150, ct).ConfigureAwait(false);
                    first.Dispose();
                    first.Dispose();
                    await waiter.ConfigureAwait(false);

                    using (await locks.AcquireAsync("k", ct).ConfigureAwait(false))
                    {
                    }

                    Assert.Equal(0.0, capture.Sum(N.LockHeld, N.AttrDrive + "=L"), "double dispose releases once");
                    Assert.Equal(1.0, capture.Sum(N.LockContentions, N.AttrDrive + "=L"));
                    IReadOnlyList<CapturedMeasurement> waits = capture.Find(N.LockWaitDuration, N.AttrDrive + "=L", N.AttrOutcome + "=success");
                    Assert.Equal(3, waits.Count);
                    double longest = 0;
                    foreach (CapturedMeasurement wait in waits) longest = Math.Max(longest, wait.Value);
                    Assert.True(longest >= 0.05, "the contended wait was measured (" + longest + "s)");
                    Assert.True(capture.Spans(N.SpanStagePrefix + N.StageLockWait, N.AttrDrive + "=L", "s3drive.lock.contended=True").Count == 1);
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "LockWaitCancelled", "A cancelled async lock wait records outcome=cancelled and holds nothing", async ct =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    ObjectLocks locks = new ObjectLocks("V");
                    using (locks.Acquire("k"))
                    using (CancellationTokenSource cts = new CancellationTokenSource(100))
                    {
                        bool cancelled = false;
                        try
                        {
                            using (await locks.AcquireAsync("k", cts.Token).ConfigureAwait(false))
                            {
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled = true;
                        }

                        Assert.True(cancelled);
                    }

                    Assert.Equal(1, capture.Find(N.LockWaitDuration, N.AttrDrive + "=V", N.AttrOutcome + "=cancelled").Count);
                    Assert.Equal(0.0, capture.Sum(N.LockHeld, N.AttrDrive + "=V"));
                }
            }));
        }

        private static void AddMountCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(TestCases.Create(SuiteId, "MountFailureRecorded", "A failed mount records mount.operations outcome=error with error.type and an errored drive mount span", async ct =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        S3DrivePaths paths = new S3DrivePaths(root);
                        paths.EnsureDirectories();
                        await using (MountManager manager = new MountManager(paths, new CredentialProtector(paths.MachineKeyFile)))
                        {
                            DriveProfile profile = new DriveProfile
                            {
                                Id = "drv_telemetry",
                                Bucket = "b",
                                AccessKey = "a",
                                SecretKeyEncrypted = "not-a-valid-ciphertext",
                                DriveLetter = "M:"
                            };

                            bool threw = false;
                            try
                            {
                                await manager.MountAsync(profile, ct).ConfigureAwait(false);
                            }
                            catch (Exception)
                            {
                                threw = true;
                            }

                            Assert.True(threw);
                        }
                    }).ConfigureAwait(false);

                    IReadOnlyList<CapturedMeasurement> failures = capture.Find(N.MountOperations, N.AttrMountOperation + "=mount", N.AttrOutcome + "=error");
                    Assert.Equal(1, failures.Count);
                    string expectedType = OperatingSystem.IsWindows() ? "S3DriveCryptoException" : "PlatformNotSupportedException";
                    Assert.Equal(expectedType, failures[0].Tag(N.AttrErrorType));
                    Assert.Equal(1, capture.Find(N.MountDuration, N.AttrMountOperation + "=mount", N.AttrOutcome + "=error").Count);
                    Activity span = Single(capture.Spans(N.SpanDrivePrefix + N.MountOperationMount, N.AttrDrive + "=M"));
                    Assert.Equal(ActivityStatusCode.Error, span.Status);
                    Assert.Equal("drv_telemetry", Convert.ToString(span.GetTagItem(N.AttrDriveId)), "the drive id is a span attribute, never a metric label");
                    if (OperatingSystem.IsWindows())
                    {
                        Assert.Equal(1, capture.Find(N.MountStageDuration, N.AttrStage + "=" + N.StageDecryptCredentials, N.AttrOutcome + "=error").Count);
                    }
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "LifecycleGauges", "Observable gauges report mounted drives, build info, and safe config values", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    S3DriveTelemetry.SetConfigSnapshot(7, 3, 2);
                    capture.CollectObservables();

                    Assert.True(capture.Find(N.DrivesMounted).Count >= 1);
                    Assert.Equal(0.0, capture.Sum(N.DrivesMounted), "no drives are mounted in tests");
                    IReadOnlyList<CapturedMeasurement> build = capture.Find(N.BuildInfo);
                    Assert.Equal(1, build.Count);
                    Assert.Equal(1.0, build[0].Value);
                    Assert.Equal(S3DriveTelemetry.Version, build[0].Tag(N.AttrVersion));
                    Assert.False(string.IsNullOrEmpty(build[0].Tag(N.AttrOsType)));
                    Assert.Equal(7.0, capture.Sum(N.ConfigMetadataCacheSeconds));
                    Assert.Equal(2.0, capture.Sum(N.ConfigDrives, N.AttrAutoMount + "=true"));
                    Assert.Equal(1.0, capture.Sum(N.ConfigDrives, N.AttrAutoMount + "=false"));
                }
            }));
        }

        private static void AddCommandCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(TestCases.Create(SuiteId, "CommandPipelineJoinsSenderTrace", "A command carries the sender's traceparent; the agent's command span joins that trace with queued, parse, and execute stages", async ct =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        S3DrivePaths paths = new S3DrivePaths(root);
                        Activity sender = new Activity("tui-click");
                        sender.SetIdFormat(ActivityIdFormat.W3C);
                        sender.Start();
                        try
                        {
                            await CommandChannel.SendAsync(paths, new AgentCommand { CommandType = AgentCommandTypeEnum.Mount, DriveId = "drv_1" }, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            sender.Stop();
                        }

                        string file = CommandChannel.ListPending(paths)[0];
                        Assert.True(CommandChannel.TryRead(file, out AgentCommand? written));
                        Assert.NotNull(written!.TraceParent);
                        Assert.NotNull(written.CreatedUtc);

                        AgentCommand? received = null;
                        ActivityTraceId seenTrace = default;
                        int processed = await CommandDispatcher.DrainAsync(paths, (command, token) =>
                        {
                            received = command;
                            seenTrace = Activity.Current?.TraceId ?? default;
                            return Task.CompletedTask;
                        }, ct).ConfigureAwait(false);

                        Assert.Equal(1, processed);
                        Assert.NotNull(received);
                        Assert.Equal(0, CommandChannel.ListPending(paths).Count, "the command file was consumed");

                        Activity send = Single(capture.Spans(N.SpanIpcPrefix + N.IpcCommandSend));
                        Activity command = Single(capture.Spans(N.SpanCommandPrefix + "Mount"));
                        Assert.Equal(ActivityKind.Producer, send.Kind);
                        Assert.Equal(ActivityKind.Consumer, command.Kind);
                        Assert.Equal(sender.TraceId, send.TraceId);
                        Assert.Equal(sender.TraceId, command.TraceId, "the agent span joins the sender's trace");
                        Assert.Equal(send.SpanId, command.ParentSpanId);
                        Assert.Equal(sender.TraceId, seenTrace, "the executor runs inside the joined trace");
                        Assert.Equal(ActivityStatusCode.Ok, command.Status);
                        foreach (string stage in new string[] { N.StageQueued, N.StageParse, N.StageExecute })
                        {
                            Activity stageSpan = Single(capture.Spans(N.SpanStagePrefix + stage, N.AttrCommandType + "=Mount"));
                            Assert.Equal(command.SpanId, stageSpan.ParentSpanId, "stage " + stage + " nests under the command span");
                            Assert.Equal(1.0, capture.Sum(N.CommandStageEvents, N.AttrCommandType + "=Mount", N.AttrStage + "=" + stage, N.AttrOutcome + "=success"), "stage event " + stage);
                            Assert.Equal(1, capture.Find(N.CommandStageDuration, N.AttrCommandType + "=Mount", N.AttrStage + "=" + stage).Count, "stage latency " + stage);
                        }

                        Assert.Equal(1.0, capture.Sum(N.CommandJobs, N.AttrCommandType + "=Mount", N.AttrOutcome + "=success"));
                        Assert.Equal(1.0, capture.Sum(N.IpcOperations, N.AttrIpcOperation + "=" + N.IpcCommandSend, N.AttrOutcome + "=success"));

                        capture.CollectObservables();
                        Assert.True(capture.Sum(N.CommandLastSuccess) > 1_600_000_000, "last-success timestamp is set");
                        Assert.True(capture.Sum(N.AgentLastPoll) > 1_600_000_000, "heartbeat timestamp is set");
                    }).ConfigureAwait(false);
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "CommandPipelineFailure", "A failing command records job and execute-stage errors, an errored span, and is still consumed", async ct =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        S3DrivePaths paths = new S3DrivePaths(root);
                        await CommandChannel.SendAsync(paths, new AgentCommand { CommandType = AgentCommandTypeEnum.Unmount, DriveId = "drv_2" }, ct).ConfigureAwait(false);
                        await CommandChannel.SendAsync(paths, new AgentCommand { CommandType = AgentCommandTypeEnum.MountAll }, ct).ConfigureAwait(false);

                        int processed = await CommandDispatcher.DrainAsync(paths, (command, token) =>
                        {
                            if (command.CommandType == AgentCommandTypeEnum.Unmount) throw new InvalidOperationException("unmount failed");
                            return Task.CompletedTask;
                        }, ct).ConfigureAwait(false);

                        Assert.Equal(2, processed);
                        Assert.Equal(0, CommandChannel.ListPending(paths).Count);
                        capture.CollectObservables();
                        Assert.Equal(2.0, capture.Sum(N.CommandQueueDepth), "queue depth reflects the poll");
                    }).ConfigureAwait(false);

                    Assert.Equal(1.0, capture.Sum(N.CommandJobs, N.AttrCommandType + "=Unmount", N.AttrOutcome + "=error"));
                    Assert.Equal(1.0, capture.Sum(N.CommandJobs, N.AttrCommandType + "=MountAll", N.AttrOutcome + "=success"), "a failure does not stop later commands");
                    Assert.Equal(1.0, capture.Sum(N.CommandStageEvents, N.AttrCommandType + "=Unmount", N.AttrStage + "=" + N.StageExecute, N.AttrOutcome + "=error"));
                    Activity span = Single(capture.Spans(N.SpanCommandPrefix + "Unmount"));
                    Assert.Equal(ActivityStatusCode.Error, span.Status);
                    Assert.True(HasExceptionEvent(span));
                    Assert.True(capture.Sum(N.LogMessages, N.AttrSeverity + "=Error") >= 1);
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "CommandPipelineUnreadable", "An unreadable command file records a parse error under command type 'unknown' and is discarded", async ct =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        S3DrivePaths paths = new S3DrivePaths(root);
                        paths.EnsureDirectories();
                        File.WriteAllText(Path.Combine(paths.CommandDirectory, "cmd-garbage.json"), "{ not json");

                        bool executed = false;
                        int processed = await CommandDispatcher.DrainAsync(paths, (command, token) =>
                        {
                            executed = true;
                            return Task.CompletedTask;
                        }, ct).ConfigureAwait(false);

                        Assert.Equal(1, processed);
                        Assert.False(executed);
                        Assert.Equal(0, CommandChannel.ListPending(paths).Count, "the unreadable file was discarded");
                    }).ConfigureAwait(false);

                    Assert.Equal(1.0, capture.Sum(N.CommandJobs, N.AttrCommandType + "=" + N.CommandTypeUnknown, N.AttrOutcome + "=error"));
                    Assert.Equal(1.0, capture.Sum(N.CommandStageEvents, N.AttrCommandType + "=" + N.CommandTypeUnknown, N.AttrStage + "=" + N.StageParse, N.AttrOutcome + "=error"));
                    Assert.Equal(ActivityStatusCode.Error, Single(capture.Spans(N.SpanCommandPrefix + N.CommandTypeUnknown)).Status);
                    await Assert.ThrowsAsync<ArgumentNullException>(() => CommandDispatcher.DrainAsync(null!, (c, t) => Task.CompletedTask, ct)).ConfigureAwait(false);
                    await Assert.ThrowsAsync<ArgumentNullException>(() => CommandDispatcher.DrainAsync(new S3DrivePaths(Path.GetTempPath()), null!, ct)).ConfigureAwait(false);
                }
            }));
        }

        private static void AddIpcAndLoggingCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(TestCases.Create(SuiteId, "IpcAndConfigOperations", "Status and config reads and writes record ipc metrics; a corrupt status file records an error", async ct =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        S3DrivePaths paths = new S3DrivePaths(root);
                        SettingsManager manager = new SettingsManager(paths, name => null);
                        S3DriveSettings settings = await manager.LoadAsync(ct).ConfigureAwait(false);
                        await manager.SaveAsync(settings, ct).ConfigureAwait(false);
                        await StatusStore.WriteAsync(paths, new AgentStatus(), ct).ConfigureAwait(false);
                        Assert.NotNull(await StatusStore.ReadAsync(paths, ct).ConfigureAwait(false));
                        File.WriteAllText(paths.StatusFile, "{ corrupt");
                        Assert.Null(await StatusStore.ReadAsync(paths, ct).ConfigureAwait(false));
                    }).ConfigureAwait(false);

                    Assert.Equal(1.0, capture.Sum(N.IpcOperations, N.AttrIpcOperation + "=" + N.IpcConfigLoad, N.AttrOutcome + "=success"));
                    Assert.True(capture.Sum(N.IpcOperations, N.AttrIpcOperation + "=" + N.IpcConfigSave, N.AttrOutcome + "=success") >= 1);
                    Assert.Equal(1.0, capture.Sum(N.IpcOperations, N.AttrIpcOperation + "=" + N.IpcStatusWrite, N.AttrOutcome + "=success"));
                    Assert.Equal(1.0, capture.Sum(N.IpcOperations, N.AttrIpcOperation + "=" + N.IpcStatusRead, N.AttrOutcome + "=success"));
                    Assert.Equal(1.0, capture.Sum(N.IpcOperations, N.AttrIpcOperation + "=" + N.IpcStatusRead, N.AttrOutcome + "=error", N.AttrErrorType + "=JsonException"));
                    Assert.True(capture.Find(N.IpcDuration, N.AttrIpcOperation + "=" + N.IpcStatusWrite).Count == 1);
                }
            }));

            cases.Add(TestCases.Create(SuiteId, "LogAndCrashCounters", "Log lines are counted by severity and crash reports by error type", () =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    S3DriveLog.Warn("telemetry warn");
                    S3DriveLog.Error("telemetry error");
                    S3DriveLog.WriteCrash(new InvalidTimeZoneException("test crash"), "telemetry test");

                    Assert.True(capture.Sum(N.LogMessages, N.AttrSeverity + "=Warn") >= 1);
                    Assert.True(capture.Sum(N.LogMessages, N.AttrSeverity + "=Error") >= 1);
                    Assert.Equal(1.0, capture.Sum(N.Crashes, N.AttrErrorType + "=InvalidTimeZoneException"));
                }
            }));
        }

        private static void AddSettingsCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(TestCases.Create(SuiteId, "TelemetrySettingsDefaults", "Telemetry settings default to emit-only with loopback (127.0.0.1) endpoints and keys excluded", () =>
            {
                TelemetrySettings settings = new S3DriveSettings().Telemetry;
                Assert.True(settings.Enabled);
                Assert.Equal("s3drive-agent", settings.ServiceName);
                Assert.False(settings.OtlpEnabled);
                Assert.Equal("http://127.0.0.1:4317", settings.OtlpEndpoint);
                Assert.Equal("grpc", settings.OtlpProtocol);
                Assert.False(settings.PrometheusEnabled);
                Assert.Equal("127.0.0.1", settings.PrometheusHostname);
                Assert.Equal(9464, settings.PrometheusPort);
                Assert.False(settings.ExportLogs);
                Assert.False(settings.LokiEnabled);
                Assert.Equal("http://127.0.0.1:3100/otlp", settings.LokiEndpoint);
                Assert.Equal(1.0, settings.SamplingRatio);
                Assert.Equal(15000, settings.MetricsExportIntervalMs);
                Assert.False(settings.IncludeObjectKeys);
            }));

            cases.Add(TestCases.Create(SuiteId, "TelemetrySettingsClamps", "Telemetry settings clamp ranges and normalize empty and unknown values", () =>
            {
                TelemetrySettings settings = new TelemetrySettings();
                settings.PrometheusPort = 0;
                Assert.Equal(1, settings.PrometheusPort);
                settings.PrometheusPort = 70000;
                Assert.Equal(65535, settings.PrometheusPort);
                settings.SamplingRatio = 5;
                Assert.Equal(1.0, settings.SamplingRatio);
                settings.SamplingRatio = -1;
                Assert.Equal(0.0, settings.SamplingRatio);
                settings.SamplingRatio = double.NaN;
                Assert.Equal(1.0, settings.SamplingRatio);
                settings.MetricsExportIntervalMs = 10;
                Assert.Equal(1000, settings.MetricsExportIntervalMs);
                settings.MetricsExportIntervalMs = int.MaxValue;
                Assert.Equal(300000, settings.MetricsExportIntervalMs);
                settings.OtlpProtocol = "HTTPPROTOBUF";
                Assert.Equal("httpprotobuf", settings.OtlpProtocol);
                settings.OtlpProtocol = "bogus";
                Assert.Equal("grpc", settings.OtlpProtocol);
                settings.OtlpEndpoint = " ";
                Assert.Equal("http://127.0.0.1:4317", settings.OtlpEndpoint);
                settings.ServiceName = null!;
                Assert.Equal("s3drive-agent", settings.ServiceName);
                settings.PrometheusHostname = string.Empty;
                Assert.Equal("127.0.0.1", settings.PrometheusHostname);
                settings.LokiEndpoint = null!;
                Assert.Equal("http://127.0.0.1:3100/otlp", settings.LokiEndpoint);

                S3DriveSettings root = new S3DriveSettings();
                root.Telemetry = null!;
                Assert.NotNull(root.Telemetry);
            }));

            cases.Add(TestCases.Create(SuiteId, "TelemetryEnvironmentOverrides", "S3DRIVE_* environment variables override telemetry settings; invalid values are ignored", () =>
            {
                Dictionary<string, string> env = new Dictionary<string, string>
                {
                    { "S3DRIVE_TELEMETRY_ENABLED", "false" },
                    { "S3DRIVE_TELEMETRY_SERVICE_NAME", "s3drive-lab" },
                    { "S3DRIVE_OTLP_ENABLED", "true" },
                    { "S3DRIVE_OTLP_ENDPOINT", "http://127.0.0.1:4318" },
                    { "S3DRIVE_OTLP_PROTOCOL", "httpprotobuf" },
                    { "S3DRIVE_PROMETHEUS_ENABLED", "true" },
                    { "S3DRIVE_PROMETHEUS_HOSTNAME", "localhost" },
                    { "S3DRIVE_PROMETHEUS_PORT", "9999" },
                    { "S3DRIVE_TELEMETRY_EXPORT_LOGS", "true" },
                    { "S3DRIVE_LOKI_ENABLED", "true" },
                    { "S3DRIVE_LOKI_ENDPOINT", "http://127.0.0.1:3101/otlp" },
                    { "S3DRIVE_TELEMETRY_SAMPLING_RATIO", "0.25" },
                    { "S3DRIVE_TELEMETRY_INCLUDE_OBJECT_KEYS", "true" }
                };
                SettingsManager manager = new SettingsManager(new S3DrivePaths(Path.GetTempPath()), name => env.TryGetValue(name, out string? value) ? value : null);
                S3DriveSettings settings = new S3DriveSettings();
                manager.ApplyEnvironmentOverrides(settings);
                TelemetrySettings t = settings.Telemetry;
                Assert.False(t.Enabled);
                Assert.Equal("s3drive-lab", t.ServiceName);
                Assert.True(t.OtlpEnabled);
                Assert.Equal("http://127.0.0.1:4318", t.OtlpEndpoint);
                Assert.Equal("httpprotobuf", t.OtlpProtocol);
                Assert.True(t.PrometheusEnabled);
                Assert.Equal("localhost", t.PrometheusHostname);
                Assert.Equal(9999, t.PrometheusPort);
                Assert.True(t.ExportLogs);
                Assert.True(t.LokiEnabled);
                Assert.Equal("http://127.0.0.1:3101/otlp", t.LokiEndpoint);
                Assert.Equal(0.25, t.SamplingRatio);
                Assert.True(t.IncludeObjectKeys);

                Dictionary<string, string> bad = new Dictionary<string, string>
                {
                    { "S3DRIVE_OTLP_ENABLED", "maybe" },
                    { "S3DRIVE_PROMETHEUS_PORT", "lots" },
                    { "S3DRIVE_TELEMETRY_SAMPLING_RATIO", "half" }
                };
                SettingsManager badManager = new SettingsManager(new S3DrivePaths(Path.GetTempPath()), name => bad.TryGetValue(name, out string? value) ? value : null);
                S3DriveSettings untouched = new S3DriveSettings();
                badManager.ApplyEnvironmentOverrides(untouched);
                Assert.False(untouched.Telemetry.OtlpEnabled);
                Assert.Equal(9464, untouched.Telemetry.PrometheusPort);
                Assert.Equal(1.0, untouched.Telemetry.SamplingRatio);
            }));

            cases.Add(TestCases.Create(SuiteId, "TelemetrySettingsRoundTrip", "The telemetry section round-trips through s3drive.json and is defaulted when absent", () =>
            {
                S3DriveSettings settings = new S3DriveSettings();
                settings.Telemetry.OtlpEnabled = true;
                settings.Telemetry.PrometheusPort = 9465;
                settings.Telemetry.SamplingRatio = 0.5;
                string json = JsonSerializer.Serialize(settings, S3DriveJson.Options);
                SettingsManager manager = new SettingsManager(new S3DrivePaths(Path.GetTempPath()), name => null);
                S3DriveSettings parsed = manager.Parse(json);
                Assert.True(parsed.Telemetry.OtlpEnabled);
                Assert.Equal(9465, parsed.Telemetry.PrometheusPort);
                Assert.Equal(0.5, parsed.Telemetry.SamplingRatio);

                S3DriveSettings legacy = manager.Parse("{ \"metadataCacheSeconds\": 5 }");
                Assert.NotNull(legacy.Telemetry);
                Assert.False(legacy.Telemetry.OtlpEnabled);
            }));
        }

        private static void AddLiveCases(List<TestCaseDescriptor> cases, StorageTestConfig storage)
        {
            const string CaseId = "LiveEndpointClassification";
            const string Name = "Against a live endpoint, S3 calls are traced and a missing-key HEAD is not_found, not a suppressed error";
            if (!storage.Enabled)
            {
                cases.Add(TestCases.Skipped(SuiteId, CaseId, Name, "No storage endpoint configured (S3DRIVE_TEST_* or --endpoint)."));
                return;
            }

            cases.Add(TestCases.Create(SuiteId, CaseId, Name, async ct =>
            {
                using (TelemetryCapture capture = new TelemetryCapture())
                {
                    string prefix = "s3drive-telemetry-" + Guid.NewGuid().ToString("N") + "/";
                    using (BlobS3Store blob = new BlobS3Store(storage.Profile, storage.Secret))
                    {
                        InstrumentedS3Store store = new InstrumentedS3Store(blob, "I", storage.Profile.Provider, storage.Profile.Bucket);
                        Assert.True(await store.ValidateConnectivityAsync(ct).ConfigureAwait(false));
                        Assert.Null(await blob.HeadAsync(prefix + "missing.txt", ct).ConfigureAwait(false));
                        Assert.Null(await store.HeadAsync(prefix + "missing.txt", ct).ConfigureAwait(false));
                        await store.PutAsync(prefix + "a.txt", Encoding.UTF8.GetBytes("abc"), ct).ConfigureAwait(false);
                        Assert.Equal(3, (await store.GetAsync(prefix + "a.txt", ct).ConfigureAwait(false)).Length);
                        await store.DeleteManyAsync(new List<string> { prefix + "a.txt" }, ct).ConfigureAwait(false);
                    }

                    Assert.Equal(0, capture.Find(N.S3SuppressedErrors).Count, "a genuine not-found must not be counted as a suppressed error");
                    Assert.Equal(1.0, capture.Sum(N.S3Requests, N.AttrDrive + "=I", N.AttrS3Operation + "=" + N.S3HeadObject, N.AttrOutcome + "=not_found"));
                    Assert.Equal(1.0, capture.Sum(N.S3Requests, N.AttrDrive + "=I", N.AttrS3Operation + "=" + N.S3PutObject, N.AttrOutcome + "=success"));
                    Assert.Equal(3.0, capture.Sum(N.S3Bytes, N.AttrDrive + "=I", N.AttrDirection + "=download"));
                    Assert.Equal(ActivityStatusCode.Ok, Single(capture.Spans(N.SpanS3Prefix + N.S3DeleteObjects, N.AttrDrive + "=I")).Status);
                }
            }));
        }

        private static void WithFs(string drive, int cacheSeconds, Action<S3DriveFileSystem, FakeS3Store> body, CancellationToken token = default)
        {
            Temp.WithDir(staging =>
            {
                FakeS3Store store = new FakeS3Store();
                InstrumentedS3Store instrumented = new InstrumentedS3Store(store, drive, S3ProviderEnum.S3Compatible, "bucket");
                S3DriveFileSystem fs = new S3DriveFileSystem(instrumented, new MetadataCache(cacheSeconds, drive), new ObjectLocks(drive), staging, "Telemetry", token, drive);
                body(fs, store);
            });
        }

        private static void WriteThenRead(S3DriveFileSystem fs, string path, string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);

            FakeDokanFileInfo writeInfo = new FakeDokanFileInfo();
            Assert.Equal(NtStatus.Success, Open(fs, path, FA.WriteData, FileMode.CreateNew, writeInfo));
            Assert.Equal(NtStatus.Success, fs.WriteFile(path, data, out int written, 0, writeInfo));
            Assert.Equal(data.Length, written);
            fs.Cleanup(path, writeInfo);
            fs.CloseFile(path, writeInfo);

            FakeDokanFileInfo readInfo = new FakeDokanFileInfo();
            Assert.Equal(NtStatus.Success, Open(fs, path, FA.ReadData, FileMode.Open, readInfo));
            byte[] buffer = new byte[data.Length];
            Assert.Equal(NtStatus.Success, fs.ReadFile(path, buffer, out int read, 0, readInfo));
            Assert.Equal(data.Length, read);
            Assert.Equal(NtStatus.Success, fs.ReadFile(path, new byte[4], out int eof, data.Length, readInfo));
            Assert.Equal(0, eof);
            fs.Cleanup(path, readInfo);
            fs.CloseFile(path, readInfo);
        }

        private static NtStatus Open(S3DriveFileSystem fs, string path, FA access, FileMode mode, FakeDokanFileInfo info)
        {
            return fs.CreateFile(path, access, FileShare.ReadWrite, mode, FileOptions.None, FileAttributes.Normal, info);
        }

        private static Activity Single(IReadOnlyList<Activity> spans)
        {
            Assert.Equal(1, spans.Count, "expected exactly one matching span");
            return spans[0];
        }

        private static bool HasExceptionEvent(Activity span)
        {
            foreach (ActivityEvent evt in span.Events)
            {
                if (evt.Name == "exception") return true;
            }

            return false;
        }

        private static bool ContainsLabel(MetricDescriptor descriptor, string key)
        {
            foreach (string label in descriptor.LabelKeys)
            {
                if (label == key) return true;
            }

            return false;
        }
    }
}
