namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Storage;

    /// <summary>
    /// An in-memory <see cref="IS3Store"/> used to exercise the filesystem layer without a real
    /// endpoint. Listing derives immediate children from the flat key set exactly as the Blobject
    /// implementation does.
    /// </summary>
    public sealed class FakeS3Store : IS3Store
    {
        private readonly object _Sync = new object();
        private readonly Dictionary<string, byte[]> _Objects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly HashSet<string> _HeadedKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _Modified = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        /// <summary>
        /// Whether connectivity validation reports success.
        /// </summary>
        public bool ConnectivityResult { get; set; } = true;

        /// <summary>
        /// When set, every store operation throws this exception (simulates an unreachable or
        /// failing endpoint).
        /// </summary>
        public Exception? FailWith { get; set; }

        /// <summary>
        /// The number of successful single-object writes (<see cref="PutAsync"/> and
        /// <see cref="PutFromFileAsync"/>), for assertions.
        /// </summary>
        public int PutCallCount { get; private set; }

        /// <summary>
        /// The number of successful HEAD requests, for assertions about metadata caching.
        /// </summary>
        public int HeadCallCount { get; private set; }

        /// <summary>
        /// Determines whether a HEAD request has been issued for a key (for assertions about
        /// metadata caching).
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>True when the key has been HEADed at least once.</returns>
        public bool WasHeaded(string key)
        {
            lock (_Sync) { return _HeadedKeys.Contains(key); }
        }

        /// <summary>
        /// The number of successful listing requests, for assertions about metadata caching.
        /// </summary>
        public int ListCallCount { get; private set; }

        /// <summary>
        /// The number of objects currently stored.
        /// </summary>
        public int Count
        {
            get { lock (_Sync) { return _Objects.Count; } }
        }

        /// <summary>
        /// Determines whether an object exists in the fake store (for assertions).
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>True when present.</returns>
        public bool Has(string key)
        {
            lock (_Sync) { return _Objects.ContainsKey(key); }
        }

        /// <summary>
        /// Returns a copy of an object's bytes, or null when absent (for assertions).
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The bytes, or null.</returns>
        public byte[]? Peek(string key)
        {
            lock (_Sync)
            {
                return _Objects.TryGetValue(key, out byte[]? data) ? (byte[])data.Clone() : null;
            }
        }

        /// <summary>
        /// Seeds an object directly (for arranging tests).
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="data">The bytes.</param>
        public void Seed(string key, byte[] data)
        {
            lock (_Sync)
            {
                _Objects[key] = (byte[])data.Clone();
                _Modified[key] = DateTime.UtcNow;
            }
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(string key, CancellationToken token)
        {
            Guard(token);
            lock (_Sync) { return Task.FromResult(_Objects.ContainsKey(key)); }
        }

        /// <inheritdoc />
        public Task<S3Entry?> HeadAsync(string key, CancellationToken token)
        {
            Guard(token);
            lock (_Sync)
            {
                HeadCallCount++;
                _HeadedKeys.Add(key);
                if (!_Objects.TryGetValue(key, out byte[]? data)) return Task.FromResult<S3Entry?>(null);

                S3Entry entry = new S3Entry
                {
                    Key = key,
                    Name = NameOf(key),
                    EntryType = key.EndsWith('/') ? S3EntryTypeEnum.Directory : S3EntryTypeEnum.File,
                    SizeBytes = data.Length,
                    LastModifiedUtc = _Modified.TryGetValue(key, out DateTime m) ? m : DateTime.UtcNow
                };
                return Task.FromResult<S3Entry?>(entry);
            }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<S3Entry>> ListAsync(string prefix, CancellationToken token)
        {
            Guard(token);
            Dictionary<string, S3Entry> folders = new Dictionary<string, S3Entry>(StringComparer.Ordinal);
            List<S3Entry> files = new List<S3Entry>();

            lock (_Sync)
            {
                ListCallCount++;
                foreach (KeyValuePair<string, byte[]> pair in _Objects)
                {
                    string key = pair.Key;
                    if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;

                    string remainder = key.Substring(prefix.Length);
                    if (remainder.Length == 0) continue;

                    int slash = remainder.IndexOf('/');
                    if (slash >= 0)
                    {
                        string folderName = remainder.Substring(0, slash);
                        if (folderName.Length == 0) continue;
                        if (!folders.ContainsKey(folderName))
                        {
                            folders[folderName] = new S3Entry
                            {
                                Key = prefix + folderName + "/",
                                Name = folderName,
                                EntryType = S3EntryTypeEnum.Directory
                            };
                        }
                    }
                    else
                    {
                        if (key.EndsWith('/')) continue;
                        files.Add(new S3Entry
                        {
                            Key = key,
                            Name = remainder,
                            EntryType = S3EntryTypeEnum.File,
                            SizeBytes = pair.Value.Length,
                            LastModifiedUtc = _Modified.TryGetValue(key, out DateTime m) ? m : DateTime.UtcNow
                        });
                    }
                }
            }

            List<S3Entry> result = new List<S3Entry>(folders.Count + files.Count);
            result.AddRange(folders.Values);
            result.AddRange(files);
            return Task.FromResult<IReadOnlyList<S3Entry>>(result);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListAllKeysAsync(string prefix, CancellationToken token)
        {
            Guard(token);
            List<string> keys = new List<string>();
            lock (_Sync)
            {
                foreach (string key in _Objects.Keys)
                {
                    if (key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(key);
                }
            }

            return Task.FromResult<IReadOnlyList<string>>(keys);
        }

        /// <inheritdoc />
        public Task<byte[]> GetAsync(string key, CancellationToken token)
        {
            Guard(token);
            lock (_Sync)
            {
                if (!_Objects.TryGetValue(key, out byte[]? data)) throw new FileNotFoundException("No such object: " + key);
                return Task.FromResult((byte[])data.Clone());
            }
        }

        /// <inheritdoc />
        public async Task GetToFileAsync(string key, string destinationPath, CancellationToken token)
        {
            byte[] data = await GetAsync(key, token).ConfigureAwait(false);
            string? directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(destinationPath, data, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task PutAsync(string key, byte[] data, CancellationToken token)
        {
            Guard(token);
            lock (_Sync)
            {
                _Objects[key] = (byte[])data.Clone();
                _Modified[key] = DateTime.UtcNow;
                PutCallCount++;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task PutFromFileAsync(string key, string sourcePath, CancellationToken token)
        {
            byte[] data = await File.ReadAllBytesAsync(sourcePath, token).ConfigureAwait(false);
            await PutAsync(key, data, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task DeleteAsync(string key, CancellationToken token)
        {
            Guard(token);
            lock (_Sync)
            {
                _Objects.Remove(key);
                _Modified.Remove(key);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// The number of times <see cref="DeleteManyAsync"/> has been invoked (for assertions).
        /// </summary>
        public int DeleteManyCallCount { get; private set; }

        /// <inheritdoc />
        public Task DeleteManyAsync(IReadOnlyCollection<string> keys, CancellationToken token)
        {
            Guard(token);
            if (keys == null) throw new ArgumentNullException(nameof(keys));

            lock (_Sync)
            {
                DeleteManyCallCount++;
                foreach (string key in keys)
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    _Objects.Remove(key);
                    _Modified.Remove(key);
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken token)
        {
            Guard(token);
            lock (_Sync)
            {
                if (!_Objects.TryGetValue(sourceKey, out byte[]? data)) throw new FileNotFoundException("No such object: " + sourceKey);
                _Objects[destinationKey] = (byte[])data.Clone();
                _Modified[destinationKey] = DateTime.UtcNow;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> ValidateConnectivityAsync(CancellationToken token)
        {
            Guard(token);
            return Task.FromResult(ConnectivityResult);
        }

        private void Guard(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Exception? failure = FailWith;
            if (failure != null) throw failure;
        }

        private static string NameOf(string key)
        {
            string trimmed = key.TrimEnd('/');
            int slash = trimmed.LastIndexOf('/');
            return slash < 0 ? trimmed : trimmed.Substring(slash + 1);
        }
    }
}
