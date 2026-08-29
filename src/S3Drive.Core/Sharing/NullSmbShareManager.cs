namespace S3Drive.Core.Sharing
{
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;

    /// <summary>
    /// A no-op share manager used on platforms without SMB support and in tests. It reports that
    /// sharing is not supported and performs no action.
    /// </summary>
    public sealed class NullSmbShareManager : ISmbShareManager
    {
        /// <inheritdoc />
        public bool IsSupported
        {
            get { return false; }
        }

        /// <inheritdoc />
        public Task CreateShareAsync(DriveProfile profile, string mountPath, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RemoveShareAsync(string shareName, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> ShareExistsAsync(string shareName, CancellationToken token)
        {
            return Task.FromResult(false);
        }
    }
}
