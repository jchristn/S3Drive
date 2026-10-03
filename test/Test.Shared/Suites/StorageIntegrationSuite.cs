namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using DokanNet;
    using S3Drive.Core.Concurrency;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.FileSystem;
    using S3Drive.Core.Storage;
    using Test.Shared.Fakes;
    using Test.Shared.Helpers;
    using Touchstone.Core;
    using FA = DokanNet.FileAccess;

    /// <summary>
    /// Integration tests for <see cref="BlobS3Store"/> and <see cref="S3DriveFileSystem"/> against
    /// a real S3 or S3-compatible endpoint (for example Less3, MinIO, or Ceph). Skipped unless an
    /// endpoint is configured via CLI arguments or S3DRIVE_TEST_* environment variables. Every
    /// case works under a unique key prefix and removes what it creates.
    /// </summary>
    public static class StorageIntegrationSuite
    {
        private const string SuiteId = "StorageIntegration";
        private const string RootPrefix = "s3drive-test/";

        /// <summary>
        /// Builds the suite, or a suite of skipped cases when no endpoint is configured.
        /// </summary>
        /// <param name="config">The storage configuration.</param>
        /// <returns>The suite descriptor.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
        public static TestSuiteDescriptor Build(StorageTestConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case(config, "Connectivity", "Storage connectivity succeeds with valid credentials", async (store, prefix, ct) =>
                {
                    Assert.True(await store.ValidateConnectivityAsync(ct).ConfigureAwait(false));
                }),

                Case(config, "PutHeadGetListCopyDelete", "Storage put, head, get, list, copy, and delete", async (store, prefix, ct) =>
                {
                    string key = prefix + "hello.txt";
                    string copyKey = prefix + "copy.txt";
                    byte[] data = Encoding.UTF8.GetBytes("hello s3drive");

                    await store.PutAsync(key, data, ct).ConfigureAwait(false);
                    Assert.True(await store.ExistsAsync(key, ct).ConfigureAwait(false));

                    S3Entry? head = await store.HeadAsync(key, ct).ConfigureAwait(false);
                    Assert.NotNull(head);
                    Assert.Equal((long)data.Length, head!.SizeBytes);
                    Assert.Equal("hello.txt", head.Name);
                    Assert.Equal(S3EntryTypeEnum.File, head.EntryType);

                    byte[] got = await store.GetAsync(key, ct).ConfigureAwait(false);
                    Assert.Equal("hello s3drive", Encoding.UTF8.GetString(got));

                    IReadOnlyList<S3Entry> listing = await store.ListAsync(prefix, ct).ConfigureAwait(false);
                    bool found = false;
                    foreach (S3Entry entry in listing)
                    {
                        if (entry.Name == "hello.txt") found = true;
                    }

                    Assert.True(found, "listing should contain hello.txt");

                    await store.CopyAsync(key, copyKey, ct).ConfigureAwait(false);
                    Assert.Equal("hello s3drive", Encoding.UTF8.GetString(await store.GetAsync(copyKey, ct).ConfigureAwait(false)));

                    IReadOnlyList<string> all = await store.ListAllKeysAsync(prefix, ct).ConfigureAwait(false);
                    Assert.Equal(2, all.Count, "expected exactly two keys under the prefix");

                    await store.DeleteAsync(key, ct).ConfigureAwait(false);
                    Assert.False(await store.ExistsAsync(key, ct).ConfigureAwait(false));

                    // Deleting an already-absent key must be tolerated (no HEAD guard; some
                    // endpoints return NoSuchKey, which is treated as success).
                    await store.DeleteAsync(key, ct).ConfigureAwait(false);
                }),

                Case(config, "OverwriteReplacesContent", "Storage put over an existing key replaces its content and size", async (store, prefix, ct) =>
                {
                    string key = prefix + "over.txt";
                    await store.PutAsync(key, Encoding.UTF8.GetBytes("first version"), ct).ConfigureAwait(false);
                    await store.PutAsync(key, Encoding.UTF8.GetBytes("v2"), ct).ConfigureAwait(false);
                    Assert.Equal("v2", Encoding.UTF8.GetString(await store.GetAsync(key, ct).ConfigureAwait(false)));
                    Assert.Equal(2L, (await store.HeadAsync(key, ct).ConfigureAwait(false))!.SizeBytes);
                }),

                Case(config, "EmptyObjectRoundTrip", "Storage round-trips an empty object", async (store, prefix, ct) =>
                {
                    string key = prefix + "empty.bin";
                    await store.PutAsync(key, Array.Empty<byte>(), ct).ConfigureAwait(false);
                    Assert.True(await store.ExistsAsync(key, ct).ConfigureAwait(false));
                    Assert.Equal(0, (await store.GetAsync(key, ct).ConfigureAwait(false)).Length);
                }),

                Case(config, "ListGroupsFolders", "Storage listing returns immediate files and folders only", async (store, prefix, ct) =>
                {
                    await store.PutAsync(prefix + "top.txt", new byte[1], ct).ConfigureAwait(false);
                    await store.PutAsync(prefix + "dir/a.txt", new byte[2], ct).ConfigureAwait(false);
                    await store.PutAsync(prefix + "dir/b.txt", new byte[3], ct).ConfigureAwait(false);
                    await store.PutAsync(prefix + "dir/deep/c.txt", new byte[4], ct).ConfigureAwait(false);

                    IReadOnlyList<S3Entry> listing = await store.ListAsync(prefix, ct).ConfigureAwait(false);
                    Assert.Equal(2, listing.Count, "one folder and one file");

                    int folders = 0;
                    foreach (S3Entry entry in listing)
                    {
                        if (entry.EntryType == S3EntryTypeEnum.Directory)
                        {
                            folders++;
                            Assert.Equal("dir", entry.Name);
                            Assert.Equal(prefix + "dir/", entry.Key);
                        }
                        else
                        {
                            Assert.Equal("top.txt", entry.Name);
                            Assert.Equal(1L, entry.SizeBytes);
                        }
                    }

                    Assert.Equal(1, folders);

                    IReadOnlyList<S3Entry> inner = await store.ListAsync(prefix + "dir/", ct).ConfigureAwait(false);
                    Assert.Equal(3, inner.Count, "a.txt, b.txt, and deep/");

                    IReadOnlyList<string> recursive = await store.ListAllKeysAsync(prefix, ct).ConfigureAwait(false);
                    Assert.Equal(4, recursive.Count);
                }),

                Case(config, "ListEmptyPrefix", "Storage listing of an unused prefix is empty", async (store, prefix, ct) =>
                {
                    Assert.Equal(0, (await store.ListAsync(prefix + "nothing-here/", ct).ConfigureAwait(false)).Count);
                    Assert.Equal(0, (await store.ListAllKeysAsync(prefix + "nothing-here/", ct).ConfigureAwait(false)).Count);
                }),

                Case(config, "MissingObjectBehavior", "Storage reports missing objects as absent and fails reads of them", async (store, prefix, ct) =>
                {
                    string missing = prefix + "missing.txt";
                    Assert.False(await store.ExistsAsync(missing, ct).ConfigureAwait(false));
                    Assert.Null(await store.HeadAsync(missing, ct).ConfigureAwait(false));
                    await Assert.ThrowsAsync<Exception>(() => store.GetAsync(missing, ct)).ConfigureAwait(false);
                    await Assert.ThrowsAsync<Exception>(() => store.CopyAsync(missing, prefix + "copy.txt", ct)).ConfigureAwait(false);
                    Assert.False(await store.ExistsAsync(prefix + "copy.txt", ct).ConfigureAwait(false));
                }),

                Case(config, "MultiObjectDelete", "Storage multi-object delete removes every key and tolerates absent ones", async (store, prefix, ct) =>
                {
                    List<string> keys = new List<string>();
                    for (int i = 0; i < 5; i++)
                    {
                        string key = prefix + "bulk/" + i + ".txt";
                        keys.Add(key);
                        await store.PutAsync(key, Encoding.UTF8.GetBytes("bulk " + i), ct).ConfigureAwait(false);
                    }

                    keys.Add(prefix + "bulk/missing.txt");
                    await store.DeleteManyAsync(keys, ct).ConfigureAwait(false);

                    foreach (string key in keys)
                    {
                        Assert.False(await store.ExistsAsync(key, ct).ConfigureAwait(false), key + " should be deleted");
                    }
                }),

                Case(config, "FileUploadDownload", "Storage file upload and download round-trip byte-for-byte", async (store, prefix, ct) =>
                {
                    string key = prefix + "file.bin";
                    byte[] data = RandomNumberGenerator.GetBytes(256 * 1024 + 3);
                    await Temp.WithDirAsync(async dir =>
                    {
                        string source = Path.Combine(dir, "source.bin");
                        await File.WriteAllBytesAsync(source, data, ct).ConfigureAwait(false);
                        await store.PutFromFileAsync(key, source, ct).ConfigureAwait(false);

                        string destination = Path.Combine(dir, "nested", "dest.bin");
                        await store.GetToFileAsync(key, destination, ct).ConfigureAwait(false);
                        byte[] roundTrip = await File.ReadAllBytesAsync(destination, ct).ConfigureAwait(false);
                        Assert.Equal(Convert.ToBase64String(data), Convert.ToBase64String(roundTrip));
                    }).ConfigureAwait(false);
                }),

                Case(config, "SpecialCharacterKeys", "Storage handles keys with spaces, unicode, and punctuation", async (store, prefix, ct) =>
                {
                    string key = prefix + "my folder/résumé (v2) & notes #1.txt";
                    await store.PutAsync(key, Encoding.UTF8.GetBytes("special"), ct).ConfigureAwait(false);
                    Assert.True(await store.ExistsAsync(key, ct).ConfigureAwait(false));
                    Assert.Equal("special", Encoding.UTF8.GetString(await store.GetAsync(key, ct).ConfigureAwait(false)));

                    IReadOnlyList<S3Entry> listing = await store.ListAsync(prefix + "my folder/", ct).ConfigureAwait(false);
                    Assert.Equal(1, listing.Count);
                    Assert.Equal("résumé (v2) & notes #1.txt", listing[0].Name);
                }),

                Case(config, "FileSystemEndToEnd", "Filesystem create, write, list, read, rename, and delete against the live endpoint", (store, prefix, ct) =>
                {
                    Temp.WithDir(staging =>
                    {
                        S3DriveFileSystem fs = new S3DriveFileSystem(store, new MetadataCache(5), new ObjectLocks(), staging, "Integration", ct);
                        string root = KeyMapper.ToPath(prefix);
                        string folder = root + "\\docs";
                        string file = folder + "\\note.txt";
                        string renamed = folder + "\\renamed.txt";

                        FakeDokanFileInfo dirInfo = new FakeDokanFileInfo { IsDirectory = true };
                        Assert.Equal(NtStatus.Success, fs.CreateFile(folder, FA.WriteData, FileShare.ReadWrite, FileMode.CreateNew, FileOptions.None, FileAttributes.Directory, dirInfo));
                        fs.Cleanup(folder, dirInfo);
                        fs.CloseFile(folder, dirInfo);

                        FakeDokanFileInfo write = new FakeDokanFileInfo();
                        Assert.Equal(NtStatus.Success, fs.CreateFile(file, FA.WriteData, FileShare.None, FileMode.CreateNew, FileOptions.None, FileAttributes.Normal, write));
                        Assert.Equal(NtStatus.Success, fs.WriteFile(file, Encoding.UTF8.GetBytes("written through the filesystem"), out _, 0, write));
                        fs.Cleanup(file, write);
                        fs.CloseFile(file, write);

                        Assert.Equal(NtStatus.Success, fs.FindFiles(folder, out IList<FileInformation> listing, new FakeDokanFileInfo()));
                        Assert.Equal(1, listing.Count);
                        Assert.Equal("note.txt", listing[0].FileName);

                        FakeDokanFileInfo read = new FakeDokanFileInfo();
                        Assert.Equal(NtStatus.Success, fs.CreateFile(file, FA.ReadData, FileShare.Read, FileMode.Open, FileOptions.None, FileAttributes.Normal, read));
                        byte[] buffer = new byte[64];
                        Assert.Equal(NtStatus.Success, fs.ReadFile(file, buffer, out int count, 0, read));
                        Assert.Equal("written through the filesystem", Encoding.UTF8.GetString(buffer, 0, count));
                        fs.Cleanup(file, read);
                        fs.CloseFile(file, read);

                        Assert.Equal(NtStatus.Success, fs.MoveFile(file, renamed, false, new FakeDokanFileInfo()));
                        Assert.Equal(NtStatus.ObjectNameNotFound, fs.GetFileInformation(file, out _, new FakeDokanFileInfo()));

                        FakeDokanFileInfo delete = new FakeDokanFileInfo();
                        Assert.Equal(NtStatus.Success, fs.CreateFile(renamed, FA.Delete, FileShare.None, FileMode.Open, FileOptions.None, FileAttributes.Normal, delete));
                        Assert.Equal(NtStatus.Success, fs.DeleteFile(renamed, delete));
                        fs.Cleanup(renamed, delete);
                        fs.CloseFile(renamed, delete);

                        Assert.Equal(NtStatus.ObjectNameNotFound, fs.GetFileInformation(renamed, out _, new FakeDokanFileInfo()));
                    });

                    return Task.CompletedTask;
                }),

                TestCases.Create(SuiteId, "UnknownAccessKeyRejected", "Storage rejects writes made with an unknown access key", async ct =>
                {
                    DriveProfile profile = Clone(config.Profile);
                    profile.AccessKey = "s3drive-unknown-" + Guid.NewGuid().ToString("N").Substring(0, 12);
                    string key = RootPrefix + Guid.NewGuid().ToString("N") + "/denied.txt";
                    using (BlobS3Store store = new BlobS3Store(profile, config.Secret + "-wrong"))
                    {
                        await Assert.ThrowsAsync<Exception>(() => store.PutAsync(key, new byte[1], ct)).ConfigureAwait(false);
                    }

                    using (BlobS3Store valid = new BlobS3Store(config.Profile, config.Secret))
                    {
                        Assert.False(await valid.ExistsAsync(key, ct).ConfigureAwait(false), "a rejected write must not create the object");
                    }
                }),

                TestCases.Create(SuiteId, "MissingBucketOperationsFail", "Storage operations against a bucket that does not exist fail", async ct =>
                {
                    DriveProfile profile = Clone(config.Profile);
                    profile.Bucket = "s3drive-missing-" + Guid.NewGuid().ToString("N").Substring(0, 12);
                    using (BlobS3Store store = new BlobS3Store(profile, config.Secret))
                    {
                        await Assert.ThrowsAsync<Exception>(() => store.PutAsync("probe.txt", new byte[1], ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<Exception>(() => store.ListAsync(string.Empty, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<Exception>(() => store.GetAsync("probe.txt", ct)).ConfigureAwait(false);
                    }
                })
            };

            if (!config.Enabled)
            {
                const string reason = "no S3 endpoint configured (pass --endpoint/--access-key/--secret-key/--bucket or set S3DRIVE_TEST_*)";
                List<TestCaseDescriptor> skipped = new List<TestCaseDescriptor>();
                foreach (TestCaseDescriptor testCase in cases)
                {
                    skipped.Add(TestCases.Skipped(SuiteId, testCase.CaseId, testCase.DisplayName, reason));
                }

                cases = skipped;
            }

            return new TestSuiteDescriptor(SuiteId, "Storage integration (live endpoint)", cases);
        }

        private static TestCaseDescriptor Case(StorageTestConfig config, string caseId, string displayName, Func<BlobS3Store, string, CancellationToken, Task> body)
        {
            return TestCases.Create(SuiteId, caseId, displayName, async ct =>
            {
                string prefix = RootPrefix + Guid.NewGuid().ToString("N") + "/";
                using (BlobS3Store store = new BlobS3Store(config.Profile, config.Secret))
                {
                    try
                    {
                        await body(store, prefix, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        IReadOnlyList<string> leftovers = await store.ListAllKeysAsync(prefix, CancellationToken.None).ConfigureAwait(false);
                        if (leftovers.Count > 0) await store.DeleteManyAsync(new List<string>(leftovers), CancellationToken.None).ConfigureAwait(false);
                    }
                }
            });
        }

        private static DriveProfile Clone(DriveProfile source)
        {
            return new DriveProfile
            {
                Id = source.Id,
                Name = source.Name,
                Provider = source.Provider,
                ServiceUrl = source.ServiceUrl,
                UseSsl = source.UseSsl,
                Region = source.Region,
                Bucket = source.Bucket,
                AccessKey = source.AccessKey,
                UsePathStyle = source.UsePathStyle,
                DriveLetter = source.DriveLetter
            };
        }
    }
}
