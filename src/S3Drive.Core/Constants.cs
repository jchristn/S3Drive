namespace S3Drive.Core
{
    /// <summary>
    /// Product-wide constants for S3Drive: identity, versioning labels, and branding.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Human-readable product name.
        /// </summary>
        public static string ProductName { get; } = "S3Drive";

        /// <summary>
        /// Short product tagline.
        /// </summary>
        public static string Tagline { get; } = "Your S3 bucket as a local drive.";

        /// <summary>
        /// Release channel label shown alongside the version. Remains "Alpha" through 0.1.x.
        /// </summary>
        public static string ReleaseLabel { get; } = "Alpha";

        /// <summary>
        /// GitHub repository URL.
        /// </summary>
        public static string RepositoryUrl { get; } = "https://github.com/jchristn/S3Drive";

        /// <summary>
        /// Copyright notice.
        /// </summary>
        public static string Copyright { get; } = "(c) 2026 Joel Christner";

        /// <summary>
        /// Entity identifier prefix for drive connection profiles (used with PrettyId).
        /// </summary>
        public static string DriveIdPrefix { get; } = "drv_";
    }
}
