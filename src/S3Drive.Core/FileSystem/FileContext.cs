namespace S3Drive.Core.FileSystem
{
    /// <summary>
    /// Per-open handle state for a file or directory, stored on the Dokan file-info context.
    /// </summary>
    internal sealed class FileContext
    {
        /// <summary>
        /// The object key (for files) or the directory key without trailing slash (for directories).
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Whether this handle refers to a directory.
        /// </summary>
        public bool IsDirectory { get; set; }

        /// <summary>
        /// The local staging file path, created lazily for files. Null until staged.
        /// </summary>
        public string? StagingPath { get; set; }

        /// <summary>
        /// Whether the staged content differs from the backing object and must be written on cleanup.
        /// </summary>
        public bool Dirty { get; set; }

        /// <summary>
        /// Whether the object should be deleted on cleanup.
        /// </summary>
        public bool DeleteOnCleanup { get; set; }

        /// <summary>
        /// Whether the handle was opened with write access.
        /// </summary>
        public bool CanWrite { get; set; }

        /// <summary>
        /// Guards staging and dirty state for this handle.
        /// </summary>
        public object Sync { get; } = new object();
    }
}
