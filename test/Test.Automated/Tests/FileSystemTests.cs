namespace Test.Automated.Tests
{
    using System.IO;
    using System.Text;
    using System.Threading;
    using DokanNet;
    using S3Drive.Core.Concurrency;
    using S3Drive.Core.FileSystem;
    using S3Drive.Core.Storage;
    using Test.Automated.Fakes;
    using Test.Automated.Harness;
    using FA = DokanNet.FileAccess;

    /// <summary>
    /// Tests for <see cref="S3DriveFileSystem"/> driven directly against fakes.
    /// </summary>
    public static class FileSystemTests
    {
        /// <summary>
        /// Registers the tests.
        /// </summary>
        /// <param name="runner">The runner.</param>
        public static void Register(TestRunner runner)
        {
            runner.Add("FS root is a directory", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    FakeDokanFileInfo info = new FakeDokanFileInfo();
                    NtStatus status = fs.CreateFile("\\", FA.ReadData, FileShare.Read, FileMode.Open, FileOptions.None, FileAttributes.Normal, info);
                    Assert.Equal(NtStatus.Success, status);
                    Assert.True(info.IsDirectory);
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS create, write, cleanup persists object", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    FakeDokanFileInfo info = new FakeDokanFileInfo();
                    Assert.Equal(NtStatus.Success, fs.CreateFile("\\hello.txt", FA.WriteData, FileShare.None, FileMode.CreateNew, FileOptions.None, FileAttributes.Normal, info));
                    byte[] data = Encoding.UTF8.GetBytes("hello world");
                    Assert.Equal(NtStatus.Success, fs.WriteFile("\\hello.txt", data, out int written, 0, info));
                    Assert.Equal(data.Length, written);
                    fs.Cleanup("\\hello.txt", info);
                    fs.CloseFile("\\hello.txt", info);

                    Assert.True(store.Has("hello.txt"));
                    Assert.Equal("hello world", Encoding.UTF8.GetString(store.Peek("hello.txt")!));
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS read returns object bytes at offset", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    store.Seed("a.txt", Encoding.UTF8.GetBytes("abcdef"));
                    FakeDokanFileInfo info = new FakeDokanFileInfo();
                    Assert.Equal(NtStatus.Success, fs.CreateFile("\\a.txt", FA.ReadData, FileShare.Read, FileMode.Open, FileOptions.None, FileAttributes.Normal, info));

                    byte[] buffer = new byte[3];
                    Assert.Equal(NtStatus.Success, fs.ReadFile("\\a.txt", buffer, out int read, 0, info));
                    Assert.Equal(3, read);
                    Assert.Equal("abc", Encoding.UTF8.GetString(buffer));

                    byte[] buffer2 = new byte[10];
                    Assert.Equal(NtStatus.Success, fs.ReadFile("\\a.txt", buffer2, out int read2, 3, info));
                    Assert.Equal(3, read2);
                    Assert.Equal("def", Encoding.UTF8.GetString(buffer2, 0, read2));

                    fs.CloseFile("\\a.txt", info);
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS CreateNew on existing collides", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    store.Seed("x.txt", new byte[1]);
                    FakeDokanFileInfo info = new FakeDokanFileInfo();
                    Assert.Equal(NtStatus.ObjectNameCollision, fs.CreateFile("\\x.txt", FA.WriteData, FileShare.None, FileMode.CreateNew, FileOptions.None, FileAttributes.Normal, info));
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS Open on missing returns not found", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    FakeDokanFileInfo info = new FakeDokanFileInfo();
                    Assert.Equal(NtStatus.ObjectNameNotFound, fs.CreateFile("\\nope.txt", FA.ReadData, FileShare.Read, FileMode.Open, FileOptions.None, FileAttributes.Normal, info));
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS GetFileInformation for file and missing", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    store.Seed("f.bin", new byte[5]);
                    FakeDokanFileInfo info = new FakeDokanFileInfo();
                    fs.CreateFile("\\f.bin", FA.ReadData, FileShare.Read, FileMode.Open, FileOptions.None, FileAttributes.Normal, info);
                    Assert.Equal(NtStatus.Success, fs.GetFileInformation("\\f.bin", out FileInformation fileInfo, info));
                    Assert.Equal(5L, fileInfo.Length);
                    Assert.False(fileInfo.Attributes.HasFlag(FileAttributes.Directory));

                    FakeDokanFileInfo missing = new FakeDokanFileInfo();
                    Assert.Equal(NtStatus.ObjectNameNotFound, fs.GetFileInformation("\\missing", out _, missing));
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS FindFiles lists files and folders", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    store.Seed("dir/a.txt", new byte[1]);
                    store.Seed("dir/b.txt", new byte[2]);
                    store.Seed("dir/sub/c.txt", new byte[3]);
                    FakeDokanFileInfo info = new FakeDokanFileInfo();

                    Assert.Equal(NtStatus.Success, fs.FindFiles("\\dir", out System.Collections.Generic.IList<FileInformation> files, info));
                    Assert.Equal(3, files.Count);

                    int directories = 0;
                    foreach (FileInformation entry in files)
                    {
                        if (entry.Attributes.HasFlag(FileAttributes.Directory)) directories++;
                    }

                    Assert.Equal(1, directories);
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS delete file removes object on cleanup", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    store.Seed("d.txt", new byte[1]);
                    FakeDokanFileInfo info = new FakeDokanFileInfo();
                    fs.CreateFile("\\d.txt", FA.Delete, FileShare.None, FileMode.Open, FileOptions.None, FileAttributes.Normal, info);
                    Assert.Equal(NtStatus.Success, fs.DeleteFile("\\d.txt", info));
                    info.DeletePending = true;
                    fs.Cleanup("\\d.txt", info);
                    fs.CloseFile("\\d.txt", info);
                    Assert.False(store.Has("d.txt"));
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS delete missing returns not found", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    FakeDokanFileInfo info = new FakeDokanFileInfo();
                    Assert.Equal(NtStatus.ObjectNameNotFound, fs.DeleteFile("\\missing.txt", info));
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS create and delete empty directory", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    FakeDokanFileInfo info = new FakeDokanFileInfo { IsDirectory = true };
                    Assert.Equal(NtStatus.Success, fs.CreateFile("\\newdir", FA.WriteData, FileShare.None, FileMode.CreateNew, FileOptions.None, FileAttributes.Directory, info));
                    Assert.True(store.Has("newdir/"));

                    FakeDokanFileInfo openInfo = new FakeDokanFileInfo { IsDirectory = true };
                    fs.CreateFile("\\newdir", FA.ReadData, FileShare.Read, FileMode.Open, FileOptions.None, FileAttributes.Directory, openInfo);
                    Assert.Equal(NtStatus.Success, fs.DeleteDirectory("\\newdir", openInfo));
                    openInfo.DeletePending = true;
                    fs.Cleanup("\\newdir", openInfo);
                    fs.CloseFile("\\newdir", openInfo);
                    Assert.False(store.Has("newdir/"));
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS delete non-empty directory is refused", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    store.Seed("dir/a.txt", new byte[1]);
                    FakeDokanFileInfo info = new FakeDokanFileInfo { IsDirectory = true };
                    Assert.Equal(NtStatus.DirectoryNotEmpty, fs.DeleteDirectory("\\dir", info));
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS move file renames object", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    store.Seed("old.txt", Encoding.UTF8.GetBytes("data"));
                    FakeDokanFileInfo info = new FakeDokanFileInfo();
                    fs.CreateFile("\\old.txt", FA.ReadData, FileShare.Read, FileMode.Open, FileOptions.None, FileAttributes.Normal, info);
                    Assert.Equal(NtStatus.Success, fs.MoveFile("\\old.txt", "\\new.txt", false, info));
                    Assert.False(store.Has("old.txt"));
                    Assert.True(store.Has("new.txt"));
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS move directory renames all descendants", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    store.Seed("d/", System.Array.Empty<byte>());
                    store.Seed("d/a.txt", new byte[1]);
                    store.Seed("d/sub/b.txt", new byte[2]);
                    FakeDokanFileInfo info = new FakeDokanFileInfo { IsDirectory = true };
                    fs.CreateFile("\\d", FA.ReadData, FileShare.Read, FileMode.Open, FileOptions.None, FileAttributes.Directory, info);

                    Assert.Equal(NtStatus.Success, fs.MoveFile("\\d", "\\e", false, info));
                    Assert.True(store.Has("e/a.txt"));
                    Assert.True(store.Has("e/sub/b.txt"));
                    Assert.False(store.Has("d/a.txt"));
                    Assert.False(store.Has("d/sub/b.txt"));
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS SetEndOfFile truncates and persists", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    store.Seed("t.txt", Encoding.UTF8.GetBytes("abcdef"));
                    FakeDokanFileInfo info = new FakeDokanFileInfo();
                    fs.CreateFile("\\t.txt", FA.WriteData, FileShare.None, FileMode.Open, FileOptions.None, FileAttributes.Normal, info);
                    Assert.Equal(NtStatus.Success, fs.SetEndOfFile("\\t.txt", 3, info));
                    fs.Cleanup("\\t.txt", info);
                    fs.CloseFile("\\t.txt", info);
                    Assert.Equal(3, store.Peek("t.txt")!.Length);
                    Assert.Equal("abc", Encoding.UTF8.GetString(store.Peek("t.txt")!));
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });

            runner.Add("FS volume and free space report", () =>
            {
                string staging;
                FakeS3Store store;
                S3DriveFileSystem fs = Build(out store, out staging);
                try
                {
                    FakeDokanFileInfo info = new FakeDokanFileInfo();
                    Assert.Equal(NtStatus.Success, fs.GetVolumeInformation(out string label, out _, out string fsName, out uint maxLen, info));
                    Assert.Equal("Test", label);
                    Assert.Equal("S3Drive", fsName);
                    Assert.Equal((uint)255, maxLen);

                    Assert.Equal(NtStatus.Success, fs.GetDiskFreeSpace(out long free, out long total, out long totalFree, info));
                    Assert.True(free > 0);
                    Assert.True(total >= free);
                    Assert.True(totalFree > 0);

                    Assert.Equal(NtStatus.Success, fs.LockFile("\\a", 0, 1, info));
                    Assert.Equal(NtStatus.Success, fs.UnlockFile("\\a", 0, 1, info));
                }
                finally
                {
                    Temp.Delete(staging);
                }
            });
        }

        private static S3DriveFileSystem Build(out FakeS3Store store, out string staging)
        {
            store = new FakeS3Store();
            staging = Temp.NewDir();
            return new S3DriveFileSystem(store, new MetadataCache(0), new ObjectLocks(), staging, "Test", CancellationToken.None);
        }
    }
}
