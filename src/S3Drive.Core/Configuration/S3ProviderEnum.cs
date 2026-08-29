namespace S3Drive.Core.Configuration
{
    /// <summary>
    /// Identifies the kind of S3 endpoint a <see cref="DriveProfile"/> connects to.
    /// </summary>
    public enum S3ProviderEnum
    {
        /// <summary>
        /// Amazon Web Services S3.
        /// </summary>
        AwsS3,

        /// <summary>
        /// An S3-compatible endpoint such as Less3, Ceph, or MinIO.
        /// </summary>
        S3Compatible
    }
}
