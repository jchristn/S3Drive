namespace S3Drive.Core.Sharing
{
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;

    /// <summary>
    /// Creates and removes CIFS/SMB network shares that re-expose a mounted drive on the network.
    /// </summary>
    public interface ISmbShareManager
    {
        /// <summary>
        /// Whether this manager can create shares on the current host.
        /// </summary>
        bool IsSupported { get; }

        /// <summary>
        /// Creates an SMB share for the given drive at the given local mount path.
        /// </summary>
        /// <param name="profile">The drive profile whose share settings are applied. Cannot be null.</param>
        /// <param name="mountPath">The local path to share, for example "S:\\". Cannot be null or empty.</param>
        /// <param name="token">A cancellation token.</param>
        Task CreateShareAsync(DriveProfile profile, string mountPath, CancellationToken token);

        /// <summary>
        /// Removes an SMB share by name. Removing a non-existent share is not an error.
        /// </summary>
        /// <param name="shareName">The share name. Cannot be null or empty.</param>
        /// <param name="token">A cancellation token.</param>
        Task RemoveShareAsync(string shareName, CancellationToken token);

        /// <summary>
        /// Determines whether a share with the given name currently exists.
        /// </summary>
        /// <param name="shareName">The share name. Cannot be null or empty.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>True when the share exists; otherwise false.</returns>
        Task<bool> ShareExistsAsync(string shareName, CancellationToken token);
    }
}
