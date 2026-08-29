namespace S3Drive.Core.Configuration
{
    using S3Drive.Core.Sharing;

    /// <summary>
    /// A single S3 connection profile: the endpoint, credentials, the bucket it exposes, the
    /// drive letter it mounts to, and its optional network share.
    /// </summary>
    public class DriveProfile
    {
        private string _Id = string.Empty;
        private string _Name = string.Empty;
        private string _Bucket = string.Empty;
        private string _AccessKey = string.Empty;
        private string _SecretKeyEncrypted = string.Empty;
        private string _DriveLetter = string.Empty;
        private SmbShareSettings _Share = new SmbShareSettings();

        /// <summary>
        /// Stable identifier for this profile (a PrettyId with the "drv_" prefix). Never null.
        /// </summary>
        public string Id
        {
            get { return _Id; }
            set { _Id = value ?? string.Empty; }
        }

        /// <summary>
        /// Human-readable name. Never null.
        /// </summary>
        public string Name
        {
            get { return _Name; }
            set { _Name = value ?? string.Empty; }
        }

        /// <summary>
        /// The kind of endpoint. Defaults to <see cref="S3ProviderEnum.AwsS3"/>.
        /// </summary>
        public S3ProviderEnum Provider { get; set; } = S3ProviderEnum.AwsS3;

        /// <summary>
        /// The endpoint URL for an S3-compatible provider (for example http://127.0.0.1:9000).
        /// Null or empty for <see cref="S3ProviderEnum.AwsS3"/>.
        /// </summary>
        public string? ServiceUrl { get; set; } = null;

        /// <summary>
        /// Whether TLS is used for the endpoint. Defaults to true.
        /// </summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>
        /// The region, when applicable. May be null.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// The bucket exposed as the drive root. Never null.
        /// </summary>
        public string Bucket
        {
            get { return _Bucket; }
            set { _Bucket = value ?? string.Empty; }
        }

        /// <summary>
        /// The access key. Never null.
        /// </summary>
        public string AccessKey
        {
            get { return _AccessKey; }
            set { _AccessKey = value ?? string.Empty; }
        }

        /// <summary>
        /// The secret key, encrypted at rest. Never stored or logged in plaintext. Never null.
        /// </summary>
        public string SecretKeyEncrypted
        {
            get { return _SecretKeyEncrypted; }
            set { _SecretKeyEncrypted = value ?? string.Empty; }
        }

        /// <summary>
        /// Whether path-style addressing is used (true) instead of virtual-hosted (false).
        /// S3-compatible endpoints typically require path-style. Defaults to false.
        /// </summary>
        public bool UsePathStyle { get; set; } = false;

        /// <summary>
        /// The drive letter to mount to, for example "S:". Never null.
        /// </summary>
        public string DriveLetter
        {
            get { return _DriveLetter; }
            set { _DriveLetter = value ?? string.Empty; }
        }

        /// <summary>
        /// Whether this profile is mounted automatically when the agent starts. Defaults to false.
        /// </summary>
        public bool AutoMount { get; set; } = false;

        /// <summary>
        /// Network-sharing settings for this drive. Never null.
        /// </summary>
        public SmbShareSettings Share
        {
            get { return _Share; }
            set { _Share = value ?? new SmbShareSettings(); }
        }
    }
}
