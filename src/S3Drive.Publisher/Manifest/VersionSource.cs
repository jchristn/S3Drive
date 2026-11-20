namespace S3Drive.Publisher.Manifest
{
    /// <summary>
    /// Where the release version is read from. A release is the manifest replayed against the
    /// version in this file, so bumping a version never edits the manifest.
    /// </summary>
    public class VersionSource
    {
        /// <summary>
        /// The MSBuild file holding the version in a Version element, relative to the repository
        /// root. Defaults to "Directory.Build.props".
        /// </summary>
        public string File { get; set; } = "Directory.Build.props";
    }
}
