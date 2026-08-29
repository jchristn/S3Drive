namespace S3Drive.Core.FileSystem
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.AccessControl;
    using System.Threading;
    using System.Threading.Tasks;
    using DokanNet;
    using S3Drive.Core.Concurrency;
    using S3Drive.Core.Diagnostics;
    using S3Drive.Core.Storage;

    /// <summary>
    /// A Dokan filesystem that exposes a single S3 bucket as a drive. One file maps to one
    /// object; folders map to key prefixes. Reads and writes are served from a locally staged
    /// copy because S3 objects are immutable and do not support partial writes. Access to each
    /// object is serialized with a coarse per-object lock during mutation to guarantee coherency
    /// of the backing data.
    /// </summary>
    public sealed class S3DriveFileSystem : IDokanOperations
    {
        private const long OneTebibyte = 1L << 40;
        private const long OnePebibyte = 1L << 50;

        private readonly IS3Store _Store;
        private readonly MetadataCache _Cache;
        private readonly ObjectLocks _Locks;
        private readonly string _StagingDirectory;
        private readonly string _VolumeLabel;
        private readonly CancellationToken _Token;

        /// <summary>
        /// Initializes a new filesystem over a store.
        /// </summary>
        /// <param name="store">The backing object store. Cannot be null.</param>
        /// <param name="cache">The metadata cache. Cannot be null.</param>
        /// <param name="locks">The per-object lock set. Cannot be null.</param>
        /// <param name="stagingDirectory">The directory for staged read/write files. Cannot be null or empty.</param>
        /// <param name="volumeLabel">The volume label shown for the drive. Cannot be null or empty.</param>
        /// <param name="token">A cancellation token that aborts in-flight storage operations on unmount.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/>, <paramref name="cache"/>, or <paramref name="locks"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="stagingDirectory"/> or <paramref name="volumeLabel"/> is null or empty.</exception>
        public S3DriveFileSystem(IS3Store store, MetadataCache cache, ObjectLocks locks, string stagingDirectory, string volumeLabel, CancellationToken token)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            if (locks == null) throw new ArgumentNullException(nameof(locks));
            if (string.IsNullOrEmpty(stagingDirectory)) throw new ArgumentException("Staging directory must be provided.", nameof(stagingDirectory));
            if (string.IsNullOrEmpty(volumeLabel)) throw new ArgumentException("Volume label must be provided.", nameof(volumeLabel));

            _Store = store;
            _Cache = cache;
            _Locks = locks;
            _StagingDirectory = stagingDirectory;
            _VolumeLabel = volumeLabel;
            _Token = token;

            Directory.CreateDirectory(_StagingDirectory);
        }

        /// <inheritdoc />
        public NtStatus CreateFile(string fileName, DokanNet.FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
        {
            if (IsRoot(fileName))
            {
                info.IsDirectory = true;
                info.Context = new FileContext { IsDirectory = true, Key = string.Empty };
                return NtStatus.Success;
            }

            string key = KeyMapper.ToObjectKey(fileName);

            try
            {
                S3Entry? head = RunHead(key);
                bool fileExists = head != null && head.EntryType == S3EntryTypeEnum.File;
                bool dirExists = !fileExists && DirectoryExists(key);

                if (info.IsDirectory || (dirExists && !fileExists))
                {
                    return OpenDirectory(key, mode, info, dirExists);
                }

                return OpenFile(key, access, mode, info, fileExists);
            }
            catch (OperationCanceledException)
            {
                return NtStatus.Unsuccessful;
            }
            catch (Exception)
            {
                return NtStatus.Error;
            }
        }

        /// <inheritdoc />
        public void Cleanup(string fileName, IDokanFileInfo info)
        {
            FileContext? context = info.Context as FileContext;
            if (context == null) return;

            try
            {
                if (info.DeletePending || context.DeleteOnCleanup)
                {
                    using (_Locks.Acquire(context.Key))
                    {
                        if (context.IsDirectory) RunDeleteDirectoryMarker(context.Key);
                        else RunDelete(context.Key);
                    }

                    InvalidateForKey(context.Key, context.IsDirectory);
                    S3DriveLog.Info((context.IsDirectory ? "rmdir " : "delete ") + KeyMapper.ToPath(context.Key));
                }
                else if (!context.IsDirectory && context.Dirty && context.StagingPath != null)
                {
                    using (_Locks.Acquire(context.Key))
                    {
                        RunPutFromFile(context.Key, context.StagingPath);
                    }

                    InvalidateForKey(context.Key, false);
                    S3DriveLog.Info("write " + KeyMapper.ToPath(context.Key));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
        }

        /// <inheritdoc />
        public void CloseFile(string fileName, IDokanFileInfo info)
        {
            FileContext? context = info.Context as FileContext;
            if (context != null && context.StagingPath != null)
            {
                try
                {
                    if (File.Exists(context.StagingPath)) File.Delete(context.StagingPath);
                }
                catch (Exception)
                {
                }
            }

            info.Context = null;
        }

        /// <inheritdoc />
        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
        {
            bytesRead = 0;
            FileContext? context = info.Context as FileContext;
            if (context == null || context.IsDirectory) return NtStatus.InvalidParameter;

            try
            {
                lock (context.Sync)
                {
                    string path = EnsureStaged(context);
                    using (FileStream stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (offset >= stream.Length) return NtStatus.Success;
                        stream.Seek(offset, SeekOrigin.Begin);
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                    }
                }

                return NtStatus.Success;
            }
            catch (OperationCanceledException)
            {
                return NtStatus.Unsuccessful;
            }
            catch (Exception)
            {
                return NtStatus.Error;
            }
        }

        /// <inheritdoc />
        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            bytesWritten = 0;
            FileContext? context = info.Context as FileContext;
            if (context == null || context.IsDirectory) return NtStatus.InvalidParameter;

            try
            {
                lock (context.Sync)
                {
                    string path = EnsureStaged(context);
                    using (FileStream stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.ReadWrite))
                    {
                        long writeOffset = info.WriteToEndOfFile ? stream.Length : offset;
                        stream.Seek(writeOffset, SeekOrigin.Begin);
                        stream.Write(buffer, 0, buffer.Length);
                        bytesWritten = buffer.Length;
                    }

                    context.Dirty = true;
                }

                return NtStatus.Success;
            }
            catch (OperationCanceledException)
            {
                return NtStatus.Unsuccessful;
            }
            catch (Exception)
            {
                return NtStatus.Error;
            }
        }

        /// <inheritdoc />
        public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        /// <inheritdoc />
        public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
        {
            if (IsRoot(fileName))
            {
                fileInfo = DirectoryInformation("\\");
                return NtStatus.Success;
            }

            string key = KeyMapper.ToObjectKey(fileName);
            FileContext? context = info.Context as FileContext;

            try
            {
                if ((context != null && context.IsDirectory) || DirectoryExists(key))
                {
                    fileInfo = DirectoryInformation(KeyMapper.GetName(fileName));
                    return NtStatus.Success;
                }

                long stagedLength = -1;
                if (context != null && context.StagingPath != null && File.Exists(context.StagingPath))
                {
                    stagedLength = new FileInfo(context.StagingPath).Length;
                }

                S3Entry? head = RunHead(key);
                if (head == null && stagedLength < 0)
                {
                    fileInfo = default;
                    return NtStatus.ObjectNameNotFound;
                }

                long length = stagedLength >= 0 ? stagedLength : (head?.SizeBytes ?? 0);
                DateTime modified = head?.LastModifiedUtc ?? DateTime.UtcNow;
                fileInfo = FileInfoFor(KeyMapper.GetName(fileName), length, modified);
                return NtStatus.Success;
            }
            catch (OperationCanceledException)
            {
                fileInfo = default;
                return NtStatus.Unsuccessful;
            }
            catch (Exception)
            {
                fileInfo = default;
                return NtStatus.Error;
            }
        }

        /// <inheritdoc />
        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
        {
            files = new List<FileInformation>();

            try
            {
                string prefix = KeyMapper.ToPrefix(fileName);
                IReadOnlyList<S3Entry> entries = RunList(prefix);
                foreach (S3Entry entry in entries)
                {
                    files.Add(EntryToInformation(entry));
                }

                S3DriveLog.Info("list " + KeyMapper.ToPath(prefix) + " (" + entries.Count + ")");
                return NtStatus.Success;
            }
            catch (OperationCanceledException)
            {
                return NtStatus.Unsuccessful;
            }
            catch (Exception)
            {
                return NtStatus.Error;
            }
        }

        /// <inheritdoc />
        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
        {
            // Not implemented so Dokan falls back to FindFiles and applies the pattern itself.
            files = new List<FileInformation>();
            return NtStatus.NotImplemented;
        }

        /// <inheritdoc />
        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info)
        {
            // S3 objects carry no Windows attributes; accept and ignore.
            return NtStatus.Success;
        }

        /// <inheritdoc />
        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info)
        {
            // Timestamps are derived from the object; accept and ignore.
            return NtStatus.Success;
        }

        /// <inheritdoc />
        public NtStatus DeleteFile(string fileName, IDokanFileInfo info)
        {
            string key = KeyMapper.ToObjectKey(fileName);
            FileContext? context = info.Context as FileContext;
            if (context != null && context.IsDirectory) return NtStatus.AccessDenied;

            try
            {
                bool exists = (context != null && context.StagingPath != null) || RunHead(key) != null;
                if (!exists) return NtStatus.ObjectNameNotFound;

                if (context != null) context.DeleteOnCleanup = true;
                return NtStatus.Success;
            }
            catch (OperationCanceledException)
            {
                return NtStatus.Unsuccessful;
            }
            catch (Exception)
            {
                return NtStatus.Error;
            }
        }

        /// <inheritdoc />
        public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
        {
            string key = KeyMapper.ToObjectKey(fileName);

            try
            {
                IReadOnlyList<S3Entry> children = RunList(key + "/");
                if (children.Count > 0) return NtStatus.DirectoryNotEmpty;

                FileContext? context = info.Context as FileContext;
                if (context != null) context.DeleteOnCleanup = true;
                return NtStatus.Success;
            }
            catch (OperationCanceledException)
            {
                return NtStatus.Unsuccessful;
            }
            catch (Exception)
            {
                return NtStatus.Error;
            }
        }

        /// <inheritdoc />
        public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
        {
            string oldKey = KeyMapper.ToObjectKey(oldName);
            string newKey = KeyMapper.ToObjectKey(newName);
            FileContext? context = info.Context as FileContext;

            try
            {
                bool isDirectory = (context != null && context.IsDirectory) || DirectoryExists(oldKey);
                if (isDirectory)
                {
                    MoveDirectory(oldKey, newKey);
                    S3DriveLog.Info("move dir " + KeyMapper.ToPath(oldKey) + " -> " + KeyMapper.ToPath(newKey));
                    return NtStatus.Success;
                }

                if (!replace && RunHead(newKey) != null) return NtStatus.ObjectNameCollision;

                using (_Locks.Acquire(oldKey))
                {
                    RunCopy(oldKey, newKey);
                    RunDelete(oldKey);
                }

                InvalidateForKey(oldKey, false);
                InvalidateForKey(newKey, false);
                S3DriveLog.Info("move " + KeyMapper.ToPath(oldKey) + " -> " + KeyMapper.ToPath(newKey));
                return NtStatus.Success;
            }
            catch (OperationCanceledException)
            {
                return NtStatus.Unsuccessful;
            }
            catch (Exception)
            {
                return NtStatus.Error;
            }
        }

        /// <inheritdoc />
        public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
        {
            return Truncate(info, length);
        }

        /// <inheritdoc />
        public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info)
        {
            return Truncate(info, length);
        }

        /// <inheritdoc />
        public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            // Byte-range locks are meaningless against a whole-object backing store; coherency is
            // enforced by the coarse per-object lock during mutation instead.
            return NtStatus.Success;
        }

        /// <inheritdoc />
        public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        /// <inheritdoc />
        public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
        {
            freeBytesAvailable = OneTebibyte;
            totalNumberOfBytes = OnePebibyte;
            totalNumberOfFreeBytes = OneTebibyte;
            return NtStatus.Success;
        }

        /// <inheritdoc />
        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
        {
            volumeLabel = _VolumeLabel;
            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.UnicodeOnDisk;
            fileSystemName = "S3Drive";
            maximumComponentLength = 255;
            return NtStatus.Success;
        }

        /// <inheritdoc />
        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity? security, AccessControlSections sections, IDokanFileInfo info)
        {
            security = null;
            return NtStatus.NotImplemented;
        }

        /// <inheritdoc />
        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
        {
            return NtStatus.NotImplemented;
        }

        /// <inheritdoc />
        public NtStatus Mounted(string mountPoint, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        /// <inheritdoc />
        public NtStatus Unmounted(IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        /// <inheritdoc />
        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
        {
            streams = new List<FileInformation>();
            return NtStatus.NotImplemented;
        }

        private NtStatus OpenDirectory(string key, FileMode mode, IDokanFileInfo info, bool dirExists)
        {
            switch (mode)
            {
                case FileMode.CreateNew:
                    if (dirExists) return NtStatus.ObjectNameCollision;
                    RunPutDirectoryMarker(key);
                    InvalidateForKey(key, true);
                    break;
                case FileMode.Create:
                case FileMode.OpenOrCreate:
                    if (!dirExists)
                    {
                        RunPutDirectoryMarker(key);
                        InvalidateForKey(key, true);
                    }
                    break;
                case FileMode.Open:
                case FileMode.Truncate:
                default:
                    if (!dirExists) return NtStatus.ObjectPathNotFound;
                    break;
            }

            info.IsDirectory = true;
            info.Context = new FileContext { IsDirectory = true, Key = key };
            return NtStatus.Success;
        }

        private NtStatus OpenFile(string key, DokanNet.FileAccess access, FileMode mode, IDokanFileInfo info, bool fileExists)
        {
            FileContext context = new FileContext
            {
                Key = key,
                IsDirectory = false,
                CanWrite = HasWriteAccess(access)
            };

            switch (mode)
            {
                case FileMode.CreateNew:
                    if (fileExists) return NtStatus.ObjectNameCollision;
                    CreateEmptyStaging(context);
                    break;
                case FileMode.Create:
                    CreateEmptyStaging(context);
                    break;
                case FileMode.Open:
                    if (!fileExists) return NtStatus.ObjectNameNotFound;
                    break;
                case FileMode.OpenOrCreate:
                    if (!fileExists) CreateEmptyStaging(context);
                    break;
                case FileMode.Truncate:
                    if (!fileExists) return NtStatus.ObjectNameNotFound;
                    CreateEmptyStaging(context);
                    break;
                default:
                    if (!fileExists) return NtStatus.ObjectNameNotFound;
                    break;
            }

            info.Context = context;
            return NtStatus.Success;
        }

        private NtStatus Truncate(IDokanFileInfo info, long length)
        {
            FileContext? context = info.Context as FileContext;
            if (context == null || context.IsDirectory) return NtStatus.InvalidParameter;

            try
            {
                lock (context.Sync)
                {
                    string path = EnsureStaged(context);
                    using (FileStream stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.ReadWrite))
                    {
                        stream.SetLength(length);
                    }

                    context.Dirty = true;
                }

                return NtStatus.Success;
            }
            catch (OperationCanceledException)
            {
                return NtStatus.Unsuccessful;
            }
            catch (Exception)
            {
                return NtStatus.Error;
            }
        }

        private void MoveDirectory(string oldKey, string newKey)
        {
            string oldPrefix = oldKey + "/";
            string newPrefix = newKey + "/";

            IReadOnlyList<string> keys = _Store.ListAllKeysAsync(oldPrefix, _Token).GetAwaiter().GetResult();
            foreach (string key in keys)
            {
                string destination = newPrefix + key.Substring(oldPrefix.Length);
                RunCopy(key, destination);
            }

            foreach (string key in keys)
            {
                RunDelete(key);
            }

            _Cache.InvalidatePrefix(oldPrefix);
            _Cache.InvalidatePrefix(newPrefix);
        }

        private string EnsureStaged(FileContext context)
        {
            if (context.StagingPath != null) return context.StagingPath;

            string path = NewStagingPath();
            using (_Locks.Acquire(context.Key))
            {
                RunGetToFile(context.Key, path);
            }

            context.StagingPath = path;
            S3DriveLog.Info("read " + KeyMapper.ToPath(context.Key));
            return path;
        }

        private void CreateEmptyStaging(FileContext context)
        {
            string path = NewStagingPath();
            using (FileStream stream = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write, FileShare.None))
            {
            }

            context.StagingPath = path;
            context.Dirty = true;
        }

        private string NewStagingPath()
        {
            return Path.Combine(_StagingDirectory, Guid.NewGuid().ToString("N"));
        }

        private bool DirectoryExists(string key)
        {
            if (string.IsNullOrEmpty(key)) return true;
            string prefix = key + "/";

            if (RunExists(prefix)) return true;
            return RunList(prefix).Count > 0;
        }

        private void InvalidateForKey(string key, bool isDirectory)
        {
            if (isDirectory) _Cache.InvalidatePrefix(key + "/");
            _Cache.InvalidateKey(key);
        }

        private S3Entry? RunHead(string key)
        {
            if (_Cache.TryGetHead(key, out S3Entry? cached)) return cached;
            S3Entry? head = _Store.HeadAsync(key, _Token).GetAwaiter().GetResult();
            _Cache.SetHead(key, head);
            return head;
        }

        private IReadOnlyList<S3Entry> RunList(string prefix)
        {
            if (_Cache.TryGetListing(prefix, out IReadOnlyList<S3Entry>? cached) && cached != null) return cached;
            IReadOnlyList<S3Entry> entries = _Store.ListAsync(prefix, _Token).GetAwaiter().GetResult();
            _Cache.SetListing(prefix, entries);
            return entries;
        }

        private bool RunExists(string key)
        {
            return _Store.ExistsAsync(key, _Token).GetAwaiter().GetResult();
        }

        private void RunGetToFile(string key, string path)
        {
            _Store.GetToFileAsync(key, path, _Token).GetAwaiter().GetResult();
        }

        private void RunPutFromFile(string key, string path)
        {
            _Store.PutFromFileAsync(key, path, _Token).GetAwaiter().GetResult();
        }

        private void RunPutDirectoryMarker(string key)
        {
            _Store.PutAsync(key + "/", Array.Empty<byte>(), _Token).GetAwaiter().GetResult();
            S3DriveLog.Info("mkdir " + KeyMapper.ToPath(key));
        }

        private void RunDeleteDirectoryMarker(string key)
        {
            _Store.DeleteAsync(key + "/", _Token).GetAwaiter().GetResult();
        }

        private void RunDelete(string key)
        {
            _Store.DeleteAsync(key, _Token).GetAwaiter().GetResult();
        }

        private void RunCopy(string sourceKey, string destinationKey)
        {
            _Store.CopyAsync(sourceKey, destinationKey, _Token).GetAwaiter().GetResult();
        }

        private static bool IsRoot(string fileName)
        {
            return string.IsNullOrEmpty(fileName) || fileName == "\\";
        }

        private static bool HasWriteAccess(DokanNet.FileAccess access)
        {
            DokanNet.FileAccess writeFlags = DokanNet.FileAccess.WriteData
                | DokanNet.FileAccess.AppendData
                | DokanNet.FileAccess.GenericWrite
                | DokanNet.FileAccess.GenericAll;
            return (access & writeFlags) != 0;
        }

        private static FileInformation EntryToInformation(S3Entry entry)
        {
            if (entry.EntryType == S3EntryTypeEnum.Directory)
            {
                return DirectoryInformation(entry.Name);
            }

            return FileInfoFor(entry.Name, entry.SizeBytes, entry.LastModifiedUtc);
        }

        private static FileInformation DirectoryInformation(string name)
        {
            DateTime now = DateTime.UtcNow;
            return new FileInformation
            {
                FileName = name,
                Attributes = FileAttributes.Directory,
                CreationTime = now,
                LastAccessTime = now,
                LastWriteTime = now,
                Length = 0
            };
        }

        private static FileInformation FileInfoFor(string name, long length, DateTime modifiedUtc)
        {
            return new FileInformation
            {
                FileName = name,
                Attributes = FileAttributes.Normal,
                CreationTime = modifiedUtc,
                LastAccessTime = modifiedUtc,
                LastWriteTime = modifiedUtc,
                Length = length
            };
        }
    }
}
