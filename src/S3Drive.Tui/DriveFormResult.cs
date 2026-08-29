namespace S3Drive.Tui
{
    using System.Collections.Generic;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Sharing;

    /// <summary>
    /// The raw values captured by the drive form, before encryption and validation.
    /// </summary>
    internal sealed class DriveFormResult
    {
        /// <summary>The drive name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The provider kind.</summary>
        public S3ProviderEnum Provider { get; set; }

        /// <summary>The endpoint URL for an S3-compatible provider.</summary>
        public string? ServiceUrl { get; set; }

        /// <summary>The region.</summary>
        public string? Region { get; set; }

        /// <summary>The bucket.</summary>
        public string Bucket { get; set; } = string.Empty;

        /// <summary>The access key.</summary>
        public string AccessKey { get; set; } = string.Empty;

        /// <summary>The plaintext secret key (empty to keep an existing value when editing).</summary>
        public string SecretPlain { get; set; } = string.Empty;

        /// <summary>Whether TLS is used.</summary>
        public bool UseSsl { get; set; }

        /// <summary>Whether path-style addressing is used.</summary>
        public bool UsePathStyle { get; set; }

        /// <summary>The drive letter.</summary>
        public string DriveLetter { get; set; } = string.Empty;

        /// <summary>Whether the drive auto-mounts.</summary>
        public bool AutoMount { get; set; }

        /// <summary>Whether the drive is shared over SMB.</summary>
        public bool ShareEnabled { get; set; }

        /// <summary>The share name.</summary>
        public string? ShareName { get; set; }

        /// <summary>The share access level.</summary>
        public ShareAccessEnum ShareAccess { get; set; }

        /// <summary>The allowed share principals.</summary>
        public List<string> AllowedPrincipals { get; set; } = new List<string>();
    }
}
