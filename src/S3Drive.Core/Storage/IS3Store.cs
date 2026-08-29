namespace S3Drive.Core.Storage
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Object-storage operations S3Drive needs to expose a single bucket as a drive. The
    /// interface lets the filesystem layer be exercised against a fake store in tests, while the
    /// production implementation wraps Blobject.
    /// </summary>
    public interface IS3Store
    {
        /// <summary>
        /// Determines whether an object exists.
        /// </summary>
        /// <param name="key">The object key. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>True if the object exists; otherwise false.</returns>
        Task<bool> ExistsAsync(string key, CancellationToken token);

        /// <summary>
        /// Reads object metadata without downloading the body.
        /// </summary>
        /// <param name="key">The object key. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The entry, or null when the object does not exist.</returns>
        Task<S3Entry?> HeadAsync(string key, CancellationToken token);

        /// <summary>
        /// Lists the immediate children (files and subfolders) under a prefix.
        /// </summary>
        /// <param name="prefix">The key prefix. Empty lists the bucket root. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The immediate child entries. Never null.</returns>
        Task<IReadOnlyList<S3Entry>> ListAsync(string prefix, CancellationToken token);

        /// <summary>
        /// Lists every object key recursively under a prefix (all descendants, not just
        /// immediate children). Used for directory rename and recursive delete.
        /// </summary>
        /// <param name="prefix">The key prefix. Empty lists the whole bucket. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>All descendant object keys. Never null.</returns>
        Task<IReadOnlyList<string>> ListAllKeysAsync(string prefix, CancellationToken token);

        /// <summary>
        /// Downloads an entire object as a byte array.
        /// </summary>
        /// <param name="key">The object key. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The object bytes. Never null.</returns>
        Task<byte[]> GetAsync(string key, CancellationToken token);

        /// <summary>
        /// Downloads an entire object to a local file.
        /// </summary>
        /// <param name="key">The object key. Cannot be null.</param>
        /// <param name="destinationPath">The destination file path. Cannot be null or empty.</param>
        /// <param name="token">A cancellation token.</param>
        Task GetToFileAsync(string key, string destinationPath, CancellationToken token);

        /// <summary>
        /// Writes an object from a byte array, replacing any existing object at the key.
        /// </summary>
        /// <param name="key">The object key. Cannot be null.</param>
        /// <param name="data">The object bytes. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        Task PutAsync(string key, byte[] data, CancellationToken token);

        /// <summary>
        /// Writes an object from a local file, replacing any existing object at the key.
        /// </summary>
        /// <param name="key">The object key. Cannot be null.</param>
        /// <param name="sourcePath">The source file path. Cannot be null or empty.</param>
        /// <param name="token">A cancellation token.</param>
        Task PutFromFileAsync(string key, string sourcePath, CancellationToken token);

        /// <summary>
        /// Deletes an object. Deleting a non-existent object is not an error.
        /// </summary>
        /// <param name="key">The object key. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        Task DeleteAsync(string key, CancellationToken token);

        /// <summary>
        /// Copies an object to a new key by reading and rewriting it.
        /// </summary>
        /// <param name="sourceKey">The source key. Cannot be null.</param>
        /// <param name="destinationKey">The destination key. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        Task CopyAsync(string sourceKey, string destinationKey, CancellationToken token);

        /// <summary>
        /// Validates connectivity to the endpoint and bucket.
        /// </summary>
        /// <param name="token">A cancellation token.</param>
        /// <returns>True when the endpoint is reachable and usable; otherwise false.</returns>
        Task<bool> ValidateConnectivityAsync(CancellationToken token);
    }
}
