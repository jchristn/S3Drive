namespace S3Drive.Core.Storage
{
    /// <summary>
    /// Distinguishes files (objects) from folders (key prefixes) in a listing.
    /// </summary>
    public enum S3EntryTypeEnum
    {
        /// <summary>
        /// A file backed by a single S3 object.
        /// </summary>
        File,

        /// <summary>
        /// A folder represented by a key prefix.
        /// </summary>
        Directory
    }
}
