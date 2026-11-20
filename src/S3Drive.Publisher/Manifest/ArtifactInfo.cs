namespace S3Drive.Publisher.Manifest
{
    /// <summary>
    /// One program the installers ship. Every artifact is published self-contained and single-file
    /// into the same install directory.
    /// </summary>
    public class ArtifactInfo
    {
        /// <summary>
        /// The display name (for example "S3Drive Agent"). Required.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The project file, relative to the repository root. Required.
        /// </summary>
        public string Project { get; set; } = string.Empty;

        /// <summary>
        /// What kind of program this is. Defaults to <see cref="ArtifactKindEnum.Cli"/>.
        /// </summary>
        public ArtifactKindEnum Kind { get; set; } = ArtifactKindEnum.Cli;

        /// <summary>
        /// The executable file name the publish produces (for example "S3Drive.Agent.exe").
        /// Packaging fails if the publish output does not contain it. Required.
        /// </summary>
        public string Executable { get; set; } = string.Empty;

        /// <summary>
        /// The command users type to run this artifact (for example "s3drive"). When set, a shim
        /// with this name is installed beside the executable and the install directory is added to
        /// PATH. Optional; null means no command.
        /// </summary>
        public string? Command { get; set; } = null;

        /// <summary>
        /// The Start menu shortcut name. Optional; null means no shortcut.
        /// </summary>
        public string? Shortcut { get; set; } = null;

        /// <summary>
        /// One phrase saying what this artifact does, used in the installer welcome text (for
        /// example "the system tray agent that owns your mounted drives"). Optional; null leaves
        /// the artifact out of the welcome text.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// The name of the sign-in startup entry (for example "S3Drive Agent"). Only meaningful for
        /// <see cref="ArtifactKindEnum.TrayAgent"/>. Optional; null means the artifact is not
        /// registered to start at sign-in.
        /// </summary>
        public string? Startup { get; set; } = null;
    }
}
