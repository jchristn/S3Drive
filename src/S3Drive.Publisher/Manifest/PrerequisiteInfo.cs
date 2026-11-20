namespace S3Drive.Publisher.Manifest
{
    /// <summary>
    /// A separately installed dependency the Windows installer checks for. A missing prerequisite
    /// does not block the install; the installer explains it and offers the download page.
    /// </summary>
    public class PrerequisiteInfo
    {
        /// <summary>
        /// The prerequisite's name (for example "Dokany"). Required.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// A file whose presence means the prerequisite is installed, as an Inno Setup path that may
        /// use constants such as {sys}. Required.
        /// </summary>
        public string DetectFile { get; set; } = string.Empty;

        /// <summary>
        /// The download page offered when the prerequisite is missing. Required.
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Why the product needs the prerequisite, shown when it is missing. Required.
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
