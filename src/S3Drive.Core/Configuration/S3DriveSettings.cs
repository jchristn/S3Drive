namespace S3Drive.Core.Configuration
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Root configuration for S3Drive: global options plus the list of connection profiles.
    /// </summary>
    public class S3DriveSettings
    {
        private int _MetadataCacheSeconds = 5;
        private long _MultipartThresholdBytes = 16L * 1024 * 1024;
        private LoggingSettings _Logging = new LoggingSettings();
        private List<DriveProfile> _Drives = new List<DriveProfile>();

        /// <summary>
        /// UTC timestamp when the configuration was first created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Logging configuration. Never null.
        /// </summary>
        public LoggingSettings Logging
        {
            get { return _Logging; }
            set { _Logging = value ?? new LoggingSettings(); }
        }

        /// <summary>
        /// How long directory listings and object attributes are cached, in seconds. Minimum 0
        /// (caching disabled), maximum 3600. Defaults to 5.
        /// </summary>
        public int MetadataCacheSeconds
        {
            get { return _MetadataCacheSeconds; }
            set { _MetadataCacheSeconds = Math.Clamp(value, 0, 3600); }
        }

        /// <summary>
        /// Object size at or above which uploads use multipart, in bytes. Minimum 5 MiB (the S3
        /// multipart part-size floor), maximum 5 GiB. Defaults to 16 MiB.
        /// </summary>
        public long MultipartThresholdBytes
        {
            get { return _MultipartThresholdBytes; }
            set { _MultipartThresholdBytes = Math.Clamp(value, 5L * 1024 * 1024, 5L * 1024 * 1024 * 1024); }
        }

        /// <summary>
        /// The configured connection profiles. Never null.
        /// </summary>
        public List<DriveProfile> Drives
        {
            get { return _Drives; }
            set { _Drives = value ?? new List<DriveProfile>(); }
        }
    }
}
