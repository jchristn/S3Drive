namespace S3Drive.Core.Storage
{
    using System;

    /// <summary>
    /// A single entry in a listing: either a file backed by an S3 object or a folder represented
    /// by a key prefix.
    /// </summary>
    public class S3Entry
    {
        private string _Key = string.Empty;
        private string _Name = string.Empty;

        /// <summary>
        /// The object key (files) or prefix (folders). Never null.
        /// </summary>
        public string Key
        {
            get { return _Key; }
            set { _Key = value ?? string.Empty; }
        }

        /// <summary>
        /// The display name (final path segment). Never null.
        /// </summary>
        public string Name
        {
            get { return _Name; }
            set { _Name = value ?? string.Empty; }
        }

        /// <summary>
        /// Whether the entry is a file or a directory.
        /// </summary>
        public S3EntryTypeEnum EntryType { get; set; } = S3EntryTypeEnum.File;

        /// <summary>
        /// The object size in bytes for files; zero for directories.
        /// </summary>
        public long SizeBytes { get; set; }

        /// <summary>
        /// The last-modified time in UTC.
        /// </summary>
        public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The entity tag (ETag) for files, when known. May be null.
        /// </summary>
        public string? ETag { get; set; }
    }
}
