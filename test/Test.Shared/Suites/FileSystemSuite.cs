namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using DokanNet;
    using S3Drive.Core.Concurrency;
    using S3Drive.Core.FileSystem;
    using S3Drive.Core.Storage;
    using Test.Shared.Fakes;
    using Test.Shared.Helpers;
    using Touchstone.Core;
    using FA = DokanNet.FileAccess;

    /// <summary>
    /// Tests for <see cref="S3DriveFileSystem"/> driven directly against an in-memory store, with
    /// no mounted volume. Covers open/create dispositions, staged reads and writes, deletes,
    /// renames, directory semantics, cache coherency, and failure mapping to NTSTATUS codes.
    /// </summary>
    public static class FileSystemSuite
    {
        private const string SuiteId = "FileSystem";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            AddConstructionCases(cases);
            AddOpenCreateCases(cases);
            AddReadWriteCases(cases);
            AddInformationCases(cases);
            AddDeleteCases(cases);
            AddMoveCases(cases);
            AddCacheCases(cases);
            AddFailureCases(cases);
            AddVolumeCases(cases);
            return new TestSuiteDescriptor(SuiteId, "Filesystem (Dokan operations)", cases);
        }

        private static void AddConstructionCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(TestCases.Create(SuiteId, "ConstructorGuards", "FS constructor rejects null dependencies and empty strings", () =>
            {
                Temp.WithDir(staging =>
                {
                    FakeS3Store store = new FakeS3Store();
                    MetadataCache cache = new MetadataCache(0);
                    ObjectLocks locks = new ObjectLocks();
                    Assert.Throws<ArgumentNullException>(() => new S3DriveFileSystem(null!, cache, locks, staging, "L", CancellationToken.None));
                    Assert.Throws<ArgumentNullException>(() => new S3DriveFileSystem(store, null!, locks, staging, "L", CancellationToken.None));
                    Assert.Throws<ArgumentNullException>(() => new S3DriveFileSystem(store, cache, null!, staging, "L", CancellationToken.None));
                    Assert.Throws<ArgumentException>(() => new S3DriveFileSystem(store, cache, locks, string.Empty, "L", CancellationToken.None));
                    Assert.Throws<ArgumentException>(() => new S3DriveFileSystem(store, cache, locks, staging, string.Empty, CancellationToken.None));
                });
            }));

            cases.Add(TestCases.Create(SuiteId, "ConstructorCreatesStaging", "FS constructor creates the staging directory", () =>
            {
                Temp.WithDir(root =>
                {
                    string staging = Path.Combine(root, "nested", "staging");
                    S3DriveFileSystem fs = new S3DriveFileSystem(new FakeS3Store(), new MetadataCache(0), new ObjectLocks(), staging, "L", CancellationToken.None);
                    Assert.NotNull(fs);
                    Assert.True(Directory.Exists(staging));
                });
            }));
        }

        private static void AddOpenCreateCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(Case("RootIsDirectory", "FS root opens as a directory", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\", FA.ReadData, FileMode.Open, info));
                Assert.True(info.IsDirectory);

                FakeDokanFileInfo empty = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, string.Empty, FA.ReadData, FileMode.Open, empty));
                Assert.True(empty.IsDirectory);
            }));

            cases.Add(Case("CreateWriteCleanupPersists", "FS create, write, and cleanup persists the object", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\hello.txt", FA.WriteData, FileMode.CreateNew, info));
                byte[] data = Encoding.UTF8.GetBytes("hello world");
                Assert.Equal(NtStatus.Success, fs.WriteFile("\\hello.txt", data, out int written, 0, info));
                Assert.Equal(data.Length, written);
                Assert.False(store.Has("hello.txt"), "nothing is uploaded before cleanup");
                Close(fs, "\\hello.txt", info);

                Assert.Equal("hello world", Text(store, "hello.txt"));
                Assert.Equal(1, store.PutCallCount);
            }));

            cases.Add(Case("CreateEmptyFilePersists", "FS create then close without writing persists an empty object", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\empty.txt", FA.WriteData, FileMode.CreateNew, info));
                Close(fs, "\\empty.txt", info);
                Assert.True(store.Has("empty.txt"));
                Assert.Equal(0, store.Peek("empty.txt")!.Length);
            }));

            cases.Add(Case("CreateInNestedFolder", "FS create in a nested folder maps to a slash-separated key", (fs, store) =>
            {
                store.Seed("a/b/", Array.Empty<byte>());
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\a\\b\\c.txt", FA.WriteData, FileMode.CreateNew, info));
                fs.WriteFile("\\a\\b\\c.txt", Encoding.UTF8.GetBytes("nested"), out _, 0, info);
                Close(fs, "\\a\\b\\c.txt", info);
                Assert.Equal("nested", Text(store, "a/b/c.txt"));
            }));

            cases.Add(Case("CreateWithUnicodeName", "FS preserves unicode and spaces in file names", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\my résumé 履歴書.txt", FA.WriteData, FileMode.CreateNew, info));
                Close(fs, "\\my résumé 履歴書.txt", info);
                Assert.True(store.Has("my résumé 履歴書.txt"));
            }));

            cases.Add(Case("CreateNewCollides", "FS CreateNew on an existing file reports a name collision", (fs, store) =>
            {
                store.Seed("x.txt", new byte[1]);
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.ObjectNameCollision, Open(fs, "\\x.txt", FA.WriteData, FileMode.CreateNew, info));
                Assert.Null(info.Context);
                Assert.Equal(1, store.Peek("x.txt")!.Length);
            }));

            cases.Add(Case("CreateOverwritesExisting", "FS Create on an existing file replaces its content", (fs, store) =>
            {
                store.Seed("o.txt", Encoding.UTF8.GetBytes("old content"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\o.txt", FA.WriteData, FileMode.Create, info));
                fs.WriteFile("\\o.txt", Encoding.UTF8.GetBytes("new"), out _, 0, info);
                Close(fs, "\\o.txt", info);
                Assert.Equal("new", Text(store, "o.txt"));
            }));

            cases.Add(Case("OpenMissingNotFound", "FS Open on a missing file returns not found", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.ObjectNameNotFound, Open(fs, "\\nope.txt", FA.ReadData, FileMode.Open, info));
                Assert.Null(info.Context);
            }));

            cases.Add(Case("OpenOrCreateMissingCreates", "FS OpenOrCreate on a missing file creates it", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\new.txt", FA.WriteData, FileMode.OpenOrCreate, info));
                Close(fs, "\\new.txt", info);
                Assert.True(store.Has("new.txt"));
            }));

            cases.Add(Case("OpenOrCreateExistingPreserves", "FS OpenOrCreate on an existing file preserves its content", (fs, store) =>
            {
                store.Seed("keep.txt", Encoding.UTF8.GetBytes("keep me"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\keep.txt", FA.ReadData, FileMode.OpenOrCreate, info));
                Close(fs, "\\keep.txt", info);
                Assert.Equal("keep me", Text(store, "keep.txt"));
                Assert.Equal(0, store.PutCallCount);
            }));

            cases.Add(Case("TruncateExistingEmpties", "FS Truncate on an existing file empties it", (fs, store) =>
            {
                store.Seed("t.txt", Encoding.UTF8.GetBytes("abcdef"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\t.txt", FA.WriteData, FileMode.Truncate, info));
                Close(fs, "\\t.txt", info);
                Assert.Equal(0, store.Peek("t.txt")!.Length);
            }));

            cases.Add(Case("TruncateMissingNotFound", "FS Truncate on a missing file returns not found", (fs, store) =>
            {
                Assert.Equal(NtStatus.ObjectNameNotFound, Open(fs, "\\none.txt", FA.WriteData, FileMode.Truncate, new FakeDokanFileInfo()));
                Assert.Equal(0, store.Count);
            }));

            cases.Add(Case("AppendModeMissingNotFound", "FS Append disposition on a missing file returns not found", (fs, store) =>
            {
                Assert.Equal(NtStatus.ObjectNameNotFound, Open(fs, "\\none.txt", FA.AppendData, FileMode.Append, new FakeDokanFileInfo()));
            }));

            cases.Add(Case("CreateDirectory", "FS CreateNew directory writes a folder marker", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo { IsDirectory = true };
                Assert.Equal(NtStatus.Success, Open(fs, "\\newdir", FA.WriteData, FileMode.CreateNew, info, FileAttributes.Directory));
                Assert.True(store.Has("newdir/"));
                Assert.True(info.IsDirectory);
            }));

            cases.Add(Case("CreateDirectoryCollides", "FS CreateNew on an existing directory reports a name collision", (fs, store) =>
            {
                store.Seed("d/", Array.Empty<byte>());
                FakeDokanFileInfo info = new FakeDokanFileInfo { IsDirectory = true };
                Assert.Equal(NtStatus.ObjectNameCollision, Open(fs, "\\d", FA.WriteData, FileMode.CreateNew, info, FileAttributes.Directory));
            }));

            cases.Add(Case("OpenOrCreateDirectory", "FS OpenOrCreate directory creates the marker only when absent", (fs, store) =>
            {
                FakeDokanFileInfo first = new FakeDokanFileInfo { IsDirectory = true };
                Assert.Equal(NtStatus.Success, Open(fs, "\\oc", FA.ReadData, FileMode.OpenOrCreate, first, FileAttributes.Directory));
                Assert.True(store.Has("oc/"));
                int puts = store.PutCallCount;

                FakeDokanFileInfo second = new FakeDokanFileInfo { IsDirectory = true };
                Assert.Equal(NtStatus.Success, Open(fs, "\\oc", FA.ReadData, FileMode.OpenOrCreate, second, FileAttributes.Directory));
                Assert.Equal(puts, store.PutCallCount, "existing directory must not be re-created");
            }));

            cases.Add(Case("OpenMissingDirectoryPathNotFound", "FS Open on a missing directory returns path not found", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo { IsDirectory = true };
                Assert.Equal(NtStatus.ObjectPathNotFound, Open(fs, "\\nodir", FA.ReadData, FileMode.Open, info, FileAttributes.Directory));
            }));

            cases.Add(Case("ImplicitDirectoryOpens", "FS opens an implicit directory (children but no marker) as a directory", (fs, store) =>
            {
                store.Seed("implicit/child.txt", new byte[3]);
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\implicit", FA.ReadData, FileMode.Open, info));
                Assert.True(info.IsDirectory, "implicit directory should be detected without IsDirectory hint");
                Assert.False(store.Has("implicit/"), "opening must not materialize a marker");
            }));

            cases.Add(Case("ExplicitDirectoryOpensWithoutHint", "FS opens a marker-only directory as a directory without an IsDirectory hint", (fs, store) =>
            {
                store.Seed("marker/", Array.Empty<byte>());
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\marker", FA.ReadData, FileMode.Open, info));
                Assert.True(info.IsDirectory);
            }));
        }

        private static void AddReadWriteCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(Case("ReadAtOffset", "FS read returns object bytes at an offset", (fs, store) =>
            {
                store.Seed("a.txt", Encoding.UTF8.GetBytes("abcdef"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\a.txt", FA.ReadData, FileMode.Open, info));

                byte[] buffer = new byte[3];
                Assert.Equal(NtStatus.Success, fs.ReadFile("\\a.txt", buffer, out int read, 0, info));
                Assert.Equal(3, read);
                Assert.Equal("abc", Encoding.UTF8.GetString(buffer));

                byte[] buffer2 = new byte[10];
                Assert.Equal(NtStatus.Success, fs.ReadFile("\\a.txt", buffer2, out int read2, 3, info));
                Assert.Equal(3, read2);
                Assert.Equal("def", Encoding.UTF8.GetString(buffer2, 0, read2));

                fs.CloseFile("\\a.txt", info);
            }));

            cases.Add(Case("ReadPastEndReturnsZero", "FS read at or past end of file returns zero bytes", (fs, store) =>
            {
                store.Seed("a.txt", Encoding.UTF8.GetBytes("abc"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\a.txt", FA.ReadData, FileMode.Open, info);
                Assert.Equal(NtStatus.Success, fs.ReadFile("\\a.txt", new byte[4], out int atEnd, 3, info));
                Assert.Equal(0, atEnd);
                Assert.Equal(NtStatus.Success, fs.ReadFile("\\a.txt", new byte[4], out int pastEnd, 1000, info));
                Assert.Equal(0, pastEnd);
                fs.CloseFile("\\a.txt", info);
            }));

            cases.Add(Case("ReadLargeBinaryIntact", "FS reads a large binary object back byte-for-byte", (fs, store) =>
            {
                byte[] data = new byte[3 * 1024 * 1024 + 17];
                new Random(42).NextBytes(data);
                store.Seed("big.bin", data);

                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\big.bin", FA.ReadData, FileMode.Open, info);
                byte[] all = new byte[data.Length];
                long offset = 0;
                byte[] chunk = new byte[65536];
                while (true)
                {
                    Assert.Equal(NtStatus.Success, fs.ReadFile("\\big.bin", chunk, out int n, offset, info));
                    if (n == 0) break;
                    Buffer.BlockCopy(chunk, 0, all, (int)offset, n);
                    offset += n;
                }

                fs.CloseFile("\\big.bin", info);
                Assert.Equal((long)data.Length, offset);
                Assert.Equal(Convert.ToBase64String(data), Convert.ToBase64String(all));
            }));

            cases.Add(Case("WriteAtOffsetPreservesRest", "FS write at an offset into an existing object preserves surrounding bytes", (fs, store) =>
            {
                store.Seed("w.txt", Encoding.UTF8.GetBytes("0123456789"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\w.txt", FA.WriteData, FileMode.Open, info));
                Assert.Equal(NtStatus.Success, fs.WriteFile("\\w.txt", Encoding.UTF8.GetBytes("ab"), out int written, 4, info));
                Assert.Equal(2, written);
                Close(fs, "\\w.txt", info);
                Assert.Equal("0123ab6789", Text(store, "w.txt"));
            }));

            cases.Add(Case("WriteExtendsFile", "FS write beyond the end of a file extends it", (fs, store) =>
            {
                store.Seed("x.txt", Encoding.UTF8.GetBytes("abc"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\x.txt", FA.WriteData, FileMode.Open, info);
                fs.WriteFile("\\x.txt", Encoding.UTF8.GetBytes("def"), out _, 3, info);
                Close(fs, "\\x.txt", info);
                Assert.Equal("abcdef", Text(store, "x.txt"));
            }));

            cases.Add(Case("WriteToEndOfFileAppends", "FS write with WriteToEndOfFile appends regardless of offset", (fs, store) =>
            {
                store.Seed("log.txt", Encoding.UTF8.GetBytes("line1\n"));
                FakeDokanFileInfo info = new FakeDokanFileInfo { WriteToEndOfFile = true };
                Open(fs, "\\log.txt", FA.AppendData, FileMode.Open, info);
                fs.WriteFile("\\log.txt", Encoding.UTF8.GetBytes("line2\n"), out _, 0, info);
                Close(fs, "\\log.txt", info);
                Assert.Equal("line1\nline2\n", Text(store, "log.txt"));
            }));

            cases.Add(Case("MultipleWritesThenReadBack", "FS reads back staged writes on the same handle before cleanup", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\m.txt", FA.WriteData | FA.ReadData, FileMode.CreateNew, info);
                fs.WriteFile("\\m.txt", Encoding.UTF8.GetBytes("hello "), out _, 0, info);
                fs.WriteFile("\\m.txt", Encoding.UTF8.GetBytes("world"), out _, 6, info);
                byte[] buffer = new byte[32];
                Assert.Equal(NtStatus.Success, fs.ReadFile("\\m.txt", buffer, out int read, 0, info));
                Assert.Equal("hello world", Encoding.UTF8.GetString(buffer, 0, read));
                Close(fs, "\\m.txt", info);
                Assert.Equal("hello world", Text(store, "m.txt"));
                Assert.Equal(1, store.PutCallCount, "a handle uploads once on cleanup");
            }));

            cases.Add(Case("ReadOnlyOpenDoesNotUpload", "FS read-only open and close does not re-upload the object", (fs, store) =>
            {
                store.Seed("r.txt", Encoding.UTF8.GetBytes("data"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\r.txt", FA.ReadData, FileMode.Open, info);
                fs.ReadFile("\\r.txt", new byte[4], out _, 0, info);
                Close(fs, "\\r.txt", info);
                Assert.Equal(0, store.PutCallCount);
            }));

            cases.Add(Case("ReadWriteRequireFileContext", "FS read, write, and truncate reject a missing or directory context", (fs, store) =>
            {
                FakeDokanFileInfo none = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.InvalidParameter, fs.ReadFile("\\a", new byte[1], out int r, 0, none));
                Assert.Equal(0, r);
                Assert.Equal(NtStatus.InvalidParameter, fs.WriteFile("\\a", new byte[1], out int w, 0, none));
                Assert.Equal(0, w);
                Assert.Equal(NtStatus.InvalidParameter, fs.SetEndOfFile("\\a", 0, none));

                FakeDokanFileInfo dir = new FakeDokanFileInfo();
                Open(fs, "\\", FA.ReadData, FileMode.Open, dir);
                Assert.Equal(NtStatus.InvalidParameter, fs.ReadFile("\\", new byte[1], out _, 0, dir));
                Assert.Equal(NtStatus.InvalidParameter, fs.WriteFile("\\", new byte[1], out _, 0, dir));
                Assert.Equal(NtStatus.InvalidParameter, fs.SetAllocationSize("\\", 0, dir));
            }));

            cases.Add(Case("SetEndOfFileTruncates", "FS SetEndOfFile truncates and persists", (fs, store) =>
            {
                store.Seed("t.txt", Encoding.UTF8.GetBytes("abcdef"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\t.txt", FA.WriteData, FileMode.Open, info);
                Assert.Equal(NtStatus.Success, fs.SetEndOfFile("\\t.txt", 3, info));
                Close(fs, "\\t.txt", info);
                Assert.Equal("abc", Text(store, "t.txt"));
            }));

            cases.Add(Case("SetAllocationSizeExtends", "FS SetAllocationSize extends a file with zero bytes", (fs, store) =>
            {
                store.Seed("g.bin", new byte[] { 1, 2 });
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\g.bin", FA.WriteData, FileMode.Open, info);
                Assert.Equal(NtStatus.Success, fs.SetAllocationSize("\\g.bin", 5, info));
                Close(fs, "\\g.bin", info);
                byte[] stored = store.Peek("g.bin")!;
                Assert.Equal(5, stored.Length);
                Assert.Equal((byte)2, stored[1]);
                Assert.Equal((byte)0, stored[4]);
            }));

            cases.Add(Case("CloseRemovesStagingFile", "FS CloseFile removes the staged copy and clears the context", (fs, store, staging) =>
            {
                store.Seed("s.txt", Encoding.UTF8.GetBytes("abc"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\s.txt", FA.ReadData, FileMode.Open, info);
                fs.ReadFile("\\s.txt", new byte[3], out _, 0, info);
                Assert.Equal(1, Directory.GetFiles(staging).Length);
                fs.Cleanup("\\s.txt", info);
                fs.CloseFile("\\s.txt", info);
                Assert.Equal(0, Directory.GetFiles(staging).Length);
                Assert.Null(info.Context);
            }));

            cases.Add(Case("CleanupWithoutContextIsNoop", "FS Cleanup and CloseFile tolerate a handle with no context", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo { DeletePending = true };
                fs.Cleanup("\\x", info);
                fs.CloseFile("\\x", info);
                Assert.Equal(0, store.Count);
            }));

            cases.Add(Case("NoOpOperationsSucceed", "FS flush, attribute, time, lock, and mount callbacks succeed", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, fs.FlushFileBuffers("\\a", info));
                Assert.Equal(NtStatus.Success, fs.SetFileAttributes("\\a", FileAttributes.ReadOnly, info));
                Assert.Equal(NtStatus.Success, fs.SetFileTime("\\a", DateTime.UtcNow, null, null, info));
                Assert.Equal(NtStatus.Success, fs.LockFile("\\a", 0, 1, info));
                Assert.Equal(NtStatus.Success, fs.UnlockFile("\\a", 0, 1, info));
                Assert.Equal(NtStatus.Success, fs.Mounted("Z:\\", info));
                Assert.Equal(NtStatus.Success, fs.Unmounted(info));
            }));

            cases.Add(Case("UnsupportedOperationsReportNotImplemented", "FS pattern search, streams, and security report NotImplemented", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.NotImplemented, fs.FindFilesWithPattern("\\", "*", out IList<FileInformation> found, info));
                Assert.Equal(0, found.Count);
                Assert.Equal(NtStatus.NotImplemented, fs.FindStreams("\\", out IList<FileInformation> streams, info));
                Assert.Equal(0, streams.Count);
                Assert.Equal(NtStatus.NotImplemented, fs.GetFileSecurity("\\", out _, default(System.Security.AccessControl.AccessControlSections), info));
            }));
        }

        private static void AddInformationCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(Case("GetFileInformationFileAndMissing", "FS GetFileInformation reports file size and not-found", (fs, store) =>
            {
                store.Seed("f.bin", new byte[5]);
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\f.bin", FA.ReadData, FileMode.Open, info);
                Assert.Equal(NtStatus.Success, fs.GetFileInformation("\\f.bin", out FileInformation fileInfo, info));
                Assert.Equal(5L, fileInfo.Length);
                Assert.Equal("f.bin", fileInfo.FileName);
                Assert.False(fileInfo.Attributes.HasFlag(FileAttributes.Directory));

                FakeDokanFileInfo missing = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.ObjectNameNotFound, fs.GetFileInformation("\\missing", out _, missing));
            }));

            cases.Add(Case("GetFileInformationRootAndDirectory", "FS GetFileInformation reports the root and folders as directories", (fs, store) =>
            {
                store.Seed("dir/a.txt", new byte[1]);
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, fs.GetFileInformation("\\", out FileInformation root, info));
                Assert.True(root.Attributes.HasFlag(FileAttributes.Directory));
                Assert.Equal(NtStatus.Success, fs.GetFileInformation("\\dir", out FileInformation dir, info));
                Assert.True(dir.Attributes.HasFlag(FileAttributes.Directory));
                Assert.Equal("dir", dir.FileName);
            }));

            cases.Add(Case("GetFileInformationUsesStagedLength", "FS GetFileInformation reports the staged length of an unflushed new file", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\pending.txt", FA.WriteData, FileMode.CreateNew, info);
                fs.WriteFile("\\pending.txt", new byte[42], out _, 0, info);
                Assert.Equal(NtStatus.Success, fs.GetFileInformation("\\pending.txt", out FileInformation fileInfo, info));
                Assert.Equal(42L, fileInfo.Length);
                Close(fs, "\\pending.txt", info);
            }));

            cases.Add(Case("FindFilesListsFilesAndFolders", "FS FindFiles lists immediate files and folders", (fs, store) =>
            {
                store.Seed("dir/a.txt", new byte[1]);
                store.Seed("dir/b.txt", new byte[2]);
                store.Seed("dir/sub/c.txt", new byte[3]);
                FakeDokanFileInfo info = new FakeDokanFileInfo();

                Assert.Equal(NtStatus.Success, fs.FindFiles("\\dir", out IList<FileInformation> files, info));
                Assert.Equal(3, files.Count);

                int directories = 0;
                foreach (FileInformation entry in files)
                {
                    if (entry.Attributes.HasFlag(FileAttributes.Directory))
                    {
                        directories++;
                        Assert.Equal("sub", entry.FileName);
                    }
                    else if (entry.FileName == "b.txt")
                    {
                        Assert.Equal(2L, entry.Length);
                    }
                }

                Assert.Equal(1, directories);
            }));

            cases.Add(Case("FindFilesAtRoot", "FS FindFiles at the root lists top-level entries only", (fs, store) =>
            {
                store.Seed("top.txt", new byte[1]);
                store.Seed("folder/inner.txt", new byte[1]);
                store.Seed("marker/", Array.Empty<byte>());
                Assert.Equal(NtStatus.Success, fs.FindFiles("\\", out IList<FileInformation> files, new FakeDokanFileInfo()));
                Assert.Equal(3, files.Count);
            }));

            cases.Add(Case("FindFilesEmptyDirectory", "FS FindFiles on an empty folder (marker only) returns nothing", (fs, store) =>
            {
                store.Seed("empty/", Array.Empty<byte>());
                Assert.Equal(NtStatus.Success, fs.FindFiles("\\empty", out IList<FileInformation> files, new FakeDokanFileInfo()));
                Assert.Equal(0, files.Count);
            }));
        }

        private static void AddDeleteCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(Case("DeleteFileOnCleanup", "FS delete removes the object on cleanup", (fs, store) =>
            {
                store.Seed("d.txt", new byte[1]);
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\d.txt", FA.Delete, FileMode.Open, info);
                Assert.Equal(NtStatus.Success, fs.DeleteFile("\\d.txt", info));
                Assert.True(store.Has("d.txt"), "delete is deferred until cleanup");
                info.DeletePending = true;
                Close(fs, "\\d.txt", info);
                Assert.False(store.Has("d.txt"));
            }));

            cases.Add(Case("DeleteOnCleanupWithoutDeletePending", "FS DeleteFile alone marks the handle for deletion at cleanup", (fs, store) =>
            {
                store.Seed("d.txt", new byte[1]);
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\d.txt", FA.Delete, FileMode.Open, info);
                Assert.Equal(NtStatus.Success, fs.DeleteFile("\\d.txt", info));
                Close(fs, "\\d.txt", info);
                Assert.False(store.Has("d.txt"));
            }));

            cases.Add(Case("DeleteMissingNotFound", "FS delete of a missing file returns not found", (fs, store) =>
            {
                Assert.Equal(NtStatus.ObjectNameNotFound, fs.DeleteFile("\\missing.txt", new FakeDokanFileInfo()));
            }));

            cases.Add(Case("DeleteFileOnDirectoryDenied", "FS DeleteFile on a directory handle is access denied", (fs, store) =>
            {
                store.Seed("d/", Array.Empty<byte>());
                FakeDokanFileInfo info = new FakeDokanFileInfo { IsDirectory = true };
                Open(fs, "\\d", FA.Delete, FileMode.Open, info, FileAttributes.Directory);
                Assert.Equal(NtStatus.AccessDenied, fs.DeleteFile("\\d", info));
                Assert.True(store.Has("d/"));
            }));

            cases.Add(Case("CreateAndDeleteEmptyDirectory", "FS create and delete an empty directory", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo { IsDirectory = true };
                Assert.Equal(NtStatus.Success, Open(fs, "\\newdir", FA.WriteData, FileMode.CreateNew, info, FileAttributes.Directory));
                Assert.True(store.Has("newdir/"));

                FakeDokanFileInfo openInfo = new FakeDokanFileInfo { IsDirectory = true };
                Open(fs, "\\newdir", FA.ReadData, FileMode.Open, openInfo, FileAttributes.Directory);
                Assert.Equal(NtStatus.Success, fs.DeleteDirectory("\\newdir", openInfo));
                openInfo.DeletePending = true;
                Close(fs, "\\newdir", openInfo);
                Assert.False(store.Has("newdir/"));
            }));

            cases.Add(Case("DeleteNonEmptyDirectoryRefused", "FS delete of a non-empty directory is refused", (fs, store) =>
            {
                store.Seed("dir/a.txt", new byte[1]);
                FakeDokanFileInfo info = new FakeDokanFileInfo { IsDirectory = true };
                Assert.Equal(NtStatus.DirectoryNotEmpty, fs.DeleteDirectory("\\dir", info));
                Assert.True(store.Has("dir/a.txt"));
            }));
        }

        private static void AddMoveCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(Case("MoveFileRenames", "FS move file renames the object and keeps its content", (fs, store) =>
            {
                store.Seed("old.txt", Encoding.UTF8.GetBytes("data"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\old.txt", FA.ReadData, FileMode.Open, info);
                Assert.Equal(NtStatus.Success, fs.MoveFile("\\old.txt", "\\new.txt", false, info));
                Assert.False(store.Has("old.txt"));
                Assert.Equal("data", Text(store, "new.txt"));
            }));

            cases.Add(Case("MoveFileAcrossFolders", "FS move file between folders rewrites the key prefix", (fs, store) =>
            {
                store.Seed("a/f.txt", Encoding.UTF8.GetBytes("x"));
                store.Seed("b/", Array.Empty<byte>());
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\a\\f.txt", FA.ReadData, FileMode.Open, info);
                Assert.Equal(NtStatus.Success, fs.MoveFile("\\a\\f.txt", "\\b\\f.txt", false, info));
                Assert.False(store.Has("a/f.txt"));
                Assert.True(store.Has("b/f.txt"));
            }));

            cases.Add(Case("MoveFileNoReplaceCollides", "FS move onto an existing file without replace reports a collision", (fs, store) =>
            {
                store.Seed("src.txt", Encoding.UTF8.GetBytes("src"));
                store.Seed("dst.txt", Encoding.UTF8.GetBytes("dst"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\src.txt", FA.ReadData, FileMode.Open, info);
                Assert.Equal(NtStatus.ObjectNameCollision, fs.MoveFile("\\src.txt", "\\dst.txt", false, info));
                Assert.Equal("src", Text(store, "src.txt"));
                Assert.Equal("dst", Text(store, "dst.txt"));
            }));

            cases.Add(Case("MoveFileReplaceOverwrites", "FS move onto an existing file with replace overwrites it", (fs, store) =>
            {
                store.Seed("src.txt", Encoding.UTF8.GetBytes("src"));
                store.Seed("dst.txt", Encoding.UTF8.GetBytes("dst"));
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\src.txt", FA.ReadData, FileMode.Open, info);
                Assert.Equal(NtStatus.Success, fs.MoveFile("\\src.txt", "\\dst.txt", true, info));
                Assert.False(store.Has("src.txt"));
                Assert.Equal("src", Text(store, "dst.txt"));
            }));

            cases.Add(Case("MoveMissingFileFails", "FS move of a file that no longer exists fails and creates nothing", (fs, store) =>
            {
                NtStatus status = fs.MoveFile("\\ghost.txt", "\\new.txt", false, new FakeDokanFileInfo());
                Assert.False(status == NtStatus.Success, "moving a missing object must not report success");
                Assert.False(store.Has("new.txt"));
            }));

            cases.Add(Case("MoveDirectoryRenamesDescendants", "FS move directory renames all descendants with one batched delete", (fs, store) =>
            {
                store.Seed("d/", Array.Empty<byte>());
                store.Seed("d/a.txt", new byte[1]);
                store.Seed("d/sub/b.txt", new byte[2]);
                FakeDokanFileInfo info = new FakeDokanFileInfo { IsDirectory = true };
                Open(fs, "\\d", FA.ReadData, FileMode.Open, info, FileAttributes.Directory);

                Assert.Equal(NtStatus.Success, fs.MoveFile("\\d", "\\e", false, info));
                Assert.True(store.Has("e/"));
                Assert.True(store.Has("e/a.txt"));
                Assert.True(store.Has("e/sub/b.txt"));
                Assert.False(store.Has("d/"));
                Assert.False(store.Has("d/a.txt"));
                Assert.False(store.Has("d/sub/b.txt"));
                Assert.Equal(1, store.DeleteManyCallCount);
            }));

            cases.Add(Case("MoveImplicitDirectory", "FS move of an implicit directory (no marker) renames its children", (fs, store) =>
            {
                store.Seed("imp/x.txt", new byte[1]);
                Assert.Equal(NtStatus.Success, fs.MoveFile("\\imp", "\\moved", false, new FakeDokanFileInfo()));
                Assert.True(store.Has("moved/x.txt"));
                Assert.False(store.Has("imp/x.txt"));
            }));

            cases.Add(Case("MoveDirectoryDoesNotTouchSiblingPrefix", "FS move directory leaves keys that merely share a name prefix", (fs, store) =>
            {
                store.Seed("d/a.txt", new byte[1]);
                store.Seed("dx/keep.txt", new byte[1]);
                store.Seed("d.txt", new byte[1]);
                Assert.Equal(NtStatus.Success, fs.MoveFile("\\d", "\\e", false, new FakeDokanFileInfo { IsDirectory = true }));
                Assert.True(store.Has("dx/keep.txt"));
                Assert.True(store.Has("d.txt"));
                Assert.True(store.Has("e/a.txt"));
            }));
        }

        private static void AddCacheCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(CachedCase("CacheServesRepeatedLookups", "FS with caching serves repeated metadata lookups without new HEADs", (fs, store) =>
            {
                store.Seed("c.txt", new byte[3]);
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                fs.GetFileInformation("\\c.txt", out _, info);
                int heads = store.HeadCallCount;
                fs.GetFileInformation("\\c.txt", out _, info);
                fs.GetFileInformation("\\c.txt", out _, info);
                Assert.Equal(heads, store.HeadCallCount);
            }));

            cases.Add(CachedCase("CacheSeededFromListing", "FS seeds per-file metadata from a directory listing", (fs, store) =>
            {
                store.Seed("dir/a.txt", new byte[7]);
                fs.FindFiles("\\dir", out _, new FakeDokanFileInfo());
                Assert.Equal(NtStatus.Success, fs.GetFileInformation("\\dir\\a.txt", out FileInformation fileInfo, new FakeDokanFileInfo()));
                Assert.Equal(7L, fileInfo.Length);
                Assert.False(store.WasHeaded("dir/a.txt"), "listed child should not need its own HEAD");
            }));

            cases.Add(CachedCase("CacheInvalidatedOnWrite", "FS invalidates cached metadata when a file is written", (fs, store) =>
            {
                store.Seed("c.txt", new byte[3]);
                fs.FindFiles("\\", out _, new FakeDokanFileInfo());
                fs.GetFileInformation("\\c.txt", out FileInformation before, new FakeDokanFileInfo());
                Assert.Equal(3L, before.Length);

                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\c.txt", FA.WriteData, FileMode.Create, info);
                fs.WriteFile("\\c.txt", new byte[10], out _, 0, info);
                Close(fs, "\\c.txt", info);

                Assert.Equal(NtStatus.Success, fs.GetFileInformation("\\c.txt", out FileInformation after, new FakeDokanFileInfo()));
                Assert.Equal(10L, after.Length);
            }));

            cases.Add(CachedCase("CacheInvalidatedOnCreate", "FS shows a newly created file in a previously cached listing", (fs, store) =>
            {
                fs.FindFiles("\\", out IList<FileInformation> before, new FakeDokanFileInfo());
                Assert.Equal(0, before.Count);
                Assert.Equal(NtStatus.ObjectNameNotFound, Open(fs, "\\n.txt", FA.ReadData, FileMode.Open, new FakeDokanFileInfo()));

                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\n.txt", FA.WriteData, FileMode.CreateNew, info);
                Close(fs, "\\n.txt", info);

                fs.FindFiles("\\", out IList<FileInformation> after, new FakeDokanFileInfo());
                Assert.Equal(1, after.Count);
                Assert.Equal(NtStatus.Success, Open(fs, "\\n.txt", FA.ReadData, FileMode.Open, new FakeDokanFileInfo()));
            }));

            cases.Add(CachedCase("CacheInvalidatedOnDelete", "FS reports a deleted file as missing despite caching", (fs, store) =>
            {
                store.Seed("gone.txt", new byte[1]);
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\gone.txt", FA.Delete, FileMode.Open, info));
                fs.DeleteFile("\\gone.txt", info);
                Close(fs, "\\gone.txt", info);

                Assert.Equal(NtStatus.ObjectNameNotFound, Open(fs, "\\gone.txt", FA.ReadData, FileMode.Open, new FakeDokanFileInfo()));
                fs.FindFiles("\\", out IList<FileInformation> listing, new FakeDokanFileInfo());
                Assert.Equal(0, listing.Count);
            }));

            cases.Add(CachedCase("CacheInvalidatedOnMove", "FS reflects a rename in cached listings for both folders", (fs, store) =>
            {
                store.Seed("a/f.txt", new byte[1]);
                store.Seed("b/", Array.Empty<byte>());
                fs.FindFiles("\\a", out _, new FakeDokanFileInfo());
                fs.FindFiles("\\b", out _, new FakeDokanFileInfo());

                Assert.Equal(NtStatus.Success, fs.MoveFile("\\a\\f.txt", "\\b\\f.txt", false, new FakeDokanFileInfo()));

                fs.FindFiles("\\a", out IList<FileInformation> a, new FakeDokanFileInfo());
                fs.FindFiles("\\b", out IList<FileInformation> b, new FakeDokanFileInfo());
                Assert.Equal(0, a.Count);
                Assert.Equal(1, b.Count);
            }));

            cases.Add(CachedCase("CacheInvalidatedOnDirectoryMove", "FS reflects a directory rename in the cached parent listing", (fs, store) =>
            {
                store.Seed("p/old/x.txt", new byte[1]);
                fs.FindFiles("\\p", out IList<FileInformation> before, new FakeDokanFileInfo());
                Assert.Equal("old", before[0].FileName);

                Assert.Equal(NtStatus.Success, fs.MoveFile("\\p\\old", "\\p\\new", false, new FakeDokanFileInfo { IsDirectory = true }));

                fs.FindFiles("\\p", out IList<FileInformation> after, new FakeDokanFileInfo());
                Assert.Equal(1, after.Count);
                Assert.Equal("new", after[0].FileName);
            }));
        }

        private static void AddFailureCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(Case("StoreFailureMapsToError", "FS maps a failing store to NtStatus.Error on every operation", (fs, store) =>
            {
                store.Seed("f.txt", new byte[1]);
                store.FailWith = new IOException("endpoint unreachable");
                Assert.Equal(NtStatus.Error, Open(fs, "\\f.txt", FA.ReadData, FileMode.Open, new FakeDokanFileInfo()));
                Assert.Equal(NtStatus.Error, fs.GetFileInformation("\\f.txt", out _, new FakeDokanFileInfo()));
                Assert.Equal(NtStatus.Error, fs.FindFiles("\\", out IList<FileInformation> files, new FakeDokanFileInfo()));
                Assert.Equal(0, files.Count);
                Assert.Equal(NtStatus.Error, fs.DeleteFile("\\f.txt", new FakeDokanFileInfo()));
                Assert.Equal(NtStatus.Error, fs.DeleteDirectory("\\d", new FakeDokanFileInfo()));
                Assert.Equal(NtStatus.Error, fs.MoveFile("\\f.txt", "\\g.txt", false, new FakeDokanFileInfo()));
                store.FailWith = null;
                Assert.True(store.Has("f.txt"));
            }));

            cases.Add(Case("ReadFailureMapsToError", "FS maps a failed download during read to NtStatus.Error", (fs, store) =>
            {
                store.Seed("f.txt", new byte[4]);
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, Open(fs, "\\f.txt", FA.ReadData, FileMode.Open, info));
                store.FailWith = new IOException("connection reset");
                Assert.Equal(NtStatus.Error, fs.ReadFile("\\f.txt", new byte[4], out int read, 0, info));
                Assert.Equal(0, read);
                Assert.Equal(NtStatus.Error, fs.WriteFile("\\f.txt", new byte[4], out int written, 0, info));
                Assert.Equal(0, written);
                store.FailWith = null;
                fs.CloseFile("\\f.txt", info);
            }));

            cases.Add(Case("UploadFailureDoesNotThrow", "FS cleanup does not throw into Dokan when the upload fails", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Open(fs, "\\u.txt", FA.WriteData, FileMode.CreateNew, info);
                fs.WriteFile("\\u.txt", new byte[8], out _, 0, info);
                store.FailWith = new IOException("upload failed");
                fs.Cleanup("\\u.txt", info);
                fs.CloseFile("\\u.txt", info);
                store.FailWith = null;
                Assert.False(store.Has("u.txt"));
            }));

            cases.Add(TestCases.Create(SuiteId, "CanceledTokenMapsToUnsuccessful", "FS maps an unmount (canceled token) to NtStatus.Unsuccessful", () =>
            {
                Temp.WithDir(staging =>
                {
                    FakeS3Store store = new FakeS3Store();
                    store.Seed("f.txt", new byte[1]);
                    using (CancellationTokenSource cts = new CancellationTokenSource())
                    {
                        cts.Cancel();
                        S3DriveFileSystem fs = new S3DriveFileSystem(store, new MetadataCache(0), new ObjectLocks(), staging, "Test", cts.Token);
                        Assert.Equal(NtStatus.Unsuccessful, Open(fs, "\\f.txt", FA.ReadData, FileMode.Open, new FakeDokanFileInfo()));
                        Assert.Equal(NtStatus.Unsuccessful, fs.GetFileInformation("\\f.txt", out _, new FakeDokanFileInfo()));
                        Assert.Equal(NtStatus.Unsuccessful, fs.FindFiles("\\", out _, new FakeDokanFileInfo()));
                        Assert.Equal(NtStatus.Unsuccessful, fs.DeleteFile("\\f.txt", new FakeDokanFileInfo()));
                        Assert.Equal(NtStatus.Unsuccessful, fs.MoveFile("\\f.txt", "\\g.txt", false, new FakeDokanFileInfo()));
                    }
                });
            }));
        }

        private static void AddVolumeCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(Case("VolumeAndFreeSpace", "FS reports volume information and free space equal to total", (fs, store) =>
            {
                FakeDokanFileInfo info = new FakeDokanFileInfo();
                Assert.Equal(NtStatus.Success, fs.GetVolumeInformation(out string label, out FileSystemFeatures features, out string fsName, out uint maxLen, info));
                Assert.Equal("Test", label);
                Assert.Equal("S3Drive", fsName);
                Assert.Equal((uint)255, maxLen);
                Assert.True(features.HasFlag(FileSystemFeatures.CasePreservedNames));
                Assert.True(features.HasFlag(FileSystemFeatures.UnicodeOnDisk));

                Assert.Equal(NtStatus.Success, fs.GetDiskFreeSpace(out long free, out long total, out long totalFree, info));
                Assert.True(free > 0);
                Assert.Equal(total, free);
                Assert.Equal(total, totalFree);
            }));
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Action<S3DriveFileSystem, FakeS3Store> body)
        {
            return Case(caseId, displayName, (fs, store, staging) => body(fs, store));
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Action<S3DriveFileSystem, FakeS3Store, string> body)
        {
            return Build(caseId, displayName, 0, body);
        }

        private static TestCaseDescriptor CachedCase(string caseId, string displayName, Action<S3DriveFileSystem, FakeS3Store> body)
        {
            return Build(caseId, displayName, 60, (fs, store, staging) => body(fs, store));
        }

        private static TestCaseDescriptor Build(string caseId, string displayName, int cacheSeconds, Action<S3DriveFileSystem, FakeS3Store, string> body)
        {
            return TestCases.Create(SuiteId, caseId, displayName, () =>
            {
                Temp.WithDir(staging =>
                {
                    FakeS3Store store = new FakeS3Store();
                    S3DriveFileSystem fs = new S3DriveFileSystem(store, new MetadataCache(cacheSeconds), new ObjectLocks(), staging, "Test", CancellationToken.None);
                    body(fs, store, staging);
                });
            });
        }

        private static NtStatus Open(S3DriveFileSystem fs, string path, FA access, FileMode mode, FakeDokanFileInfo info, FileAttributes attributes = FileAttributes.Normal)
        {
            return fs.CreateFile(path, access, FileShare.ReadWrite, mode, FileOptions.None, attributes, info);
        }

        private static void Close(S3DriveFileSystem fs, string path, FakeDokanFileInfo info)
        {
            fs.Cleanup(path, info);
            fs.CloseFile(path, info);
        }

        private static string Text(FakeS3Store store, string key)
        {
            byte[]? data = store.Peek(key);
            if (data == null) throw new AssertException("Expected object '" + key + "' to exist.");
            return Encoding.UTF8.GetString(data);
        }
    }
}
