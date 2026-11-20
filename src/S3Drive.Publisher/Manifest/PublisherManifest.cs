namespace S3Drive.Publisher.Manifest
{
    using System.Collections.Generic;

    /// <summary>
    /// The deserialized publisher.json: the single source of truth for what the installers contain
    /// and how they present the product.
    /// </summary>
    public class PublisherManifest
    {
        /// <summary>
        /// The product identity.
        /// </summary>
        public ProjectInfo Project { get; set; } = new ProjectInfo();

        /// <summary>
        /// Where the release version is read from.
        /// </summary>
        public VersionSource Version { get; set; } = new VersionSource();

        /// <summary>
        /// The build matrix.
        /// </summary>
        public BuildSettings Build { get; set; } = new BuildSettings();

        /// <summary>
        /// The programs the installers ship. At least one is required.
        /// </summary>
        public List<ArtifactInfo> Artifacts { get; set; } = new List<ArtifactInfo>();

        /// <summary>
        /// The enabled channels, in the order they run. Defaults to empty, which enables nothing.
        /// </summary>
        public List<string> Channels { get; set; } = new List<string>();

        /// <summary>
        /// Windows installer settings.
        /// </summary>
        public WindowsSettings Windows { get; set; } = new WindowsSettings();

        /// <summary>
        /// Code-signing settings. Every target defaults to not configured.
        /// </summary>
        public SigningSettings Signing { get; set; } = new SigningSettings();
    }
}
