namespace S3Drive.Publisher.Inno
{
    using S3Drive.Publisher.Imaging;
    using S3Drive.Publisher.Manifest;

    /// <summary>
    /// Everything <see cref="InnoScriptBuilder"/> needs to generate one runtime's setup script.
    /// All paths are absolute.
    /// </summary>
    public class InnoScriptInput
    {
        /// <summary>
        /// The manifest.
        /// </summary>
        public PublisherManifest Manifest { get; set; } = new PublisherManifest();

        /// <summary>
        /// The release version.
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// The runtime identifier (win-x64 or win-arm64).
        /// </summary>
        public string Runtime { get; set; } = string.Empty;

        /// <summary>
        /// The staging directory whose contents are installed.
        /// </summary>
        public string StageDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Where the compiled setup .exe is written.
        /// </summary>
        public string OutputDirectory { get; set; } = string.Empty;

        /// <summary>
        /// The setup file name without extension.
        /// </summary>
        public string OutputBaseFilename { get; set; } = string.Empty;

        /// <summary>
        /// The plain-text license shown on the license page.
        /// </summary>
        public string LicenseFile { get; set; } = string.Empty;

        /// <summary>
        /// The .ico used for the setup executable.
        /// </summary>
        public string SetupIconFile { get; set; } = string.Empty;

        /// <summary>
        /// The icon file name inside the install directory, used for shortcuts and the
        /// installed-apps list.
        /// </summary>
        public string InstalledIconFileName { get; set; } = string.Empty;

        /// <summary>
        /// The wizard bitmaps.
        /// </summary>
        public WizardImageSet Images { get; set; } = new WizardImageSet();

        /// <summary>
        /// The Inno Setup sign tool name passed to ISCC with /S, or null when signing is off (the
        /// default).
        /// </summary>
        public string? SignToolName { get; set; } = null;
    }
}
