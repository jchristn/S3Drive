namespace S3Drive.Core.Sharing
{
    /// <summary>
    /// Access level granted to clients of a CIFS/SMB network share.
    /// </summary>
    public enum ShareAccessEnum
    {
        /// <summary>
        /// Read-only access.
        /// </summary>
        ReadOnly,

        /// <summary>
        /// Read and write access.
        /// </summary>
        ReadWrite
    }
}
