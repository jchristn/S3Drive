namespace S3Drive.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.AmazonS3;
    using Blobject.Core;
    using S3Drive.Core.Configuration;

    /// <summary>
    /// An <see cref="IS3Store"/> backed by Blobject's Amazon S3 client. Supports both real AWS
    /// and S3-compatible endpoints (custom endpoint URL, SSL toggle, and path-style versus
    /// virtual-hosted addressing). Because Blobject reads whole objects, ranged reads are served
    /// by the filesystem layer from a locally staged copy rather than here.
    /// </summary>
    public sealed class BlobS3Store : IS3Store, IDisposable
    {
        private const string ContentType = "application/octet-stream";
        private const string DefaultRegion = "us-east-1";

        private readonly AmazonS3BlobClient _Client;
        private bool _Disposed;

        /// <summary>
        /// Initializes a new instance from a drive profile and its decrypted secret key.
        /// </summary>
        /// <param name="profile">The drive profile. Cannot be null.</param>
        /// <param name="secretKey">The decrypted secret key. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> or <paramref name="secretKey"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the profile is missing required values.</exception>
        public BlobS3Store(DriveProfile profile, string secretKey)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (secretKey == null) throw new ArgumentNullException(nameof(secretKey));
            if (string.IsNullOrEmpty(profile.Bucket)) throw new ArgumentException("Bucket must be provided.", nameof(profile));
            if (string.IsNullOrEmpty(profile.AccessKey)) throw new ArgumentException("Access key must be provided.", nameof(profile));

            _Client = new AmazonS3BlobClient(BuildSettings(profile, secretKey));
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string key, CancellationToken token)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return await _Client.ExistsAsync(key, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<S3Entry?> HeadAsync(string key, CancellationToken token)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            try
            {
                BlobMetadata metadata = await _Client.GetMetadataAsync(key, token).ConfigureAwait(false);
                return ToEntry(metadata, key);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<S3Entry>> ListAsync(string prefix, CancellationToken token)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));

            Dictionary<string, S3Entry> folders = new Dictionary<string, S3Entry>(StringComparer.Ordinal);
            List<S3Entry> files = new List<S3Entry>();

            EnumerationFilter filter = new EnumerationFilter();
            filter.Prefix = prefix;

            await foreach (BlobMetadata metadata in _Client.EnumerateAsync(filter, token).ConfigureAwait(false))
            {
                string? key = metadata.Key;
                if (string.IsNullOrEmpty(key)) continue;
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
                    if (metadata.IsFolder) continue;
                    files.Add(ToEntry(metadata, key));
                }
            }

            List<S3Entry> result = new List<S3Entry>(folders.Count + files.Count);
            result.AddRange(folders.Values);
            result.AddRange(files);
            return result;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> ListAllKeysAsync(string prefix, CancellationToken token)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));

            List<string> keys = new List<string>();
            EnumerationFilter filter = new EnumerationFilter();
            filter.Prefix = prefix;

            await foreach (BlobMetadata metadata in _Client.EnumerateAsync(filter, token).ConfigureAwait(false))
            {
                string? key = metadata.Key;
                if (string.IsNullOrEmpty(key)) continue;
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                keys.Add(key);
            }

            return keys;
        }

        /// <inheritdoc />
        public async Task<byte[]> GetAsync(string key, CancellationToken token)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return await _Client.GetAsync(key, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task GetToFileAsync(string key, string destinationPath, CancellationToken token)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (string.IsNullOrEmpty(destinationPath)) throw new ArgumentException("Destination path must be provided.", nameof(destinationPath));

            byte[] data = await _Client.GetAsync(key, token).ConfigureAwait(false);

            string? directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(destinationPath, data, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PutAsync(string key, byte[] data, CancellationToken token)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (data == null) throw new ArgumentNullException(nameof(data));
            await _Client.WriteAsync(key, ContentType, data, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task PutFromFileAsync(string key, string sourcePath, CancellationToken token)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (string.IsNullOrEmpty(sourcePath)) throw new ArgumentException("Source path must be provided.", nameof(sourcePath));

            using (FileStream stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await _Client.WriteAsync(key, ContentType, stream.Length, stream, token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string key, CancellationToken token)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            bool exists = await _Client.ExistsAsync(key, token).ConfigureAwait(false);
            if (!exists) return;
            await _Client.DeleteAsync(key, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task CopyAsync(string sourceKey, string destinationKey, CancellationToken token)
        {
            if (sourceKey == null) throw new ArgumentNullException(nameof(sourceKey));
            if (destinationKey == null) throw new ArgumentNullException(nameof(destinationKey));

            byte[] data = await _Client.GetAsync(sourceKey, token).ConfigureAwait(false);
            await _Client.WriteAsync(destinationKey, ContentType, data, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> ValidateConnectivityAsync(CancellationToken token)
        {
            try
            {
                return await _Client.ValidateConnectivity(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Disposes the underlying storage client.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Client.Dispose();
        }

        private static S3Entry ToEntry(BlobMetadata metadata, string key)
        {
            bool isFolder = metadata.IsFolder || key.EndsWith('/');
            string name = key.TrimEnd('/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);

            return new S3Entry
            {
                Key = key,
                Name = name,
                EntryType = isFolder ? S3EntryTypeEnum.Directory : S3EntryTypeEnum.File,
                SizeBytes = metadata.ContentLength,
                LastModifiedUtc = metadata.LastUpdateUtc ?? DateTime.UtcNow,
                ETag = metadata.ETag
            };
        }

        private static AwsSettings BuildSettings(DriveProfile profile, string secretKey)
        {
            string region = string.IsNullOrEmpty(profile.Region) ? DefaultRegion : profile.Region;

            if (profile.Provider == S3ProviderEnum.AwsS3 || string.IsNullOrEmpty(profile.ServiceUrl))
            {
                return new AwsSettings(profile.AccessKey, secretKey, region, profile.Bucket);
            }

            string endpoint = EnsureTrailingSlash(profile.ServiceUrl!);
            string baseUrl = BuildBaseUrl(profile.ServiceUrl!, profile.UseSsl, profile.UsePathStyle);
            return new AwsSettings(endpoint, profile.UseSsl, profile.AccessKey, secretKey, region, profile.Bucket, baseUrl);
        }

        private static string BuildBaseUrl(string serviceUrl, bool useSsl, bool usePathStyle)
        {
            Uri uri = new Uri(serviceUrl, UriKind.Absolute);
            string scheme = useSsl ? "https" : "http";
            string authority = uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port;

            if (usePathStyle)
            {
                return scheme + "://" + authority + "/{bucket}/{key}";
            }

            return scheme + "://{bucket}." + authority + "/{key}";
        }

        private static string EnsureTrailingSlash(string value)
        {
            if (value.EndsWith('/')) return value;
            return value + "/";
        }
    }
}
