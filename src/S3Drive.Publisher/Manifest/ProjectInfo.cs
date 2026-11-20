namespace S3Drive.Publisher.Manifest
{
    /// <summary>
    /// The product identity every installer shows: name, tagline, license, logo, and publisher.
    /// </summary>
    public class ProjectInfo
    {
        /// <summary>
        /// The product name as users know it (for example "S3Drive"), shown in window titles, the
        /// welcome page, and the installed-apps list. Required.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The lowercase package name used in file names (for example "s3drive"). Required; letters,
        /// digits, and hyphens only.
        /// </summary>
        public string PackageName { get; set; } = string.Empty;

        /// <summary>
        /// The reverse-DNS identifier (for example "ai.latticetech.s3drive"). The Windows installer
        /// AppId is a GUID derived from it, so it must not change once a release has shipped.
        /// Required.
        /// </summary>
        public string Identifier { get; set; } = string.Empty;

        /// <summary>
        /// One sentence saying what the software is, shown on the installer's welcome page.
        /// Required.
        /// </summary>
        public string Tagline { get; set; } = string.Empty;

        /// <summary>
        /// A longer paragraph describing the product. Optional; defaults to empty.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The SPDX license identifier (for example "MIT"). Required.
        /// </summary>
        public string License { get; set; } = string.Empty;

        /// <summary>
        /// The license file, relative to the repository root. Shown on the installer's license
        /// page, which the user must accept. Required.
        /// </summary>
        public string LicenseFile { get; set; } = string.Empty;

        /// <summary>
        /// The source logo PNG, relative to the repository root. Every icon and wizard image is
        /// generated from it. Required.
        /// </summary>
        public string Logo { get; set; } = string.Empty;

        /// <summary>
        /// The publisher shown to users (for example "Lattice AI, LLC"). Required.
        /// </summary>
        public string Publisher { get; set; } = string.Empty;

        /// <summary>
        /// The publisher's web site. Optional; defaults to empty.
        /// </summary>
        public string PublisherUrl { get; set; } = string.Empty;

        /// <summary>
        /// The source repository URL, used for support and update links. Optional; defaults to
        /// empty.
        /// </summary>
        public string RepositoryUrl { get; set; } = string.Empty;

        /// <summary>
        /// The copyright line embedded in installers. Optional; defaults to empty.
        /// </summary>
        public string Copyright { get; set; } = string.Empty;
    }
}
