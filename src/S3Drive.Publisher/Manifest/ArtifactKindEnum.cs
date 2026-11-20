namespace S3Drive.Publisher.Manifest
{
    /// <summary>
    /// What kind of program an artifact is, which decides how installers present and register it.
    /// </summary>
    public enum ArtifactKindEnum
    {
        /// <summary>
        /// A command-line program. Installers put its command on PATH.
        /// </summary>
        Cli,

        /// <summary>
        /// A terminal user interface. Installers put its command on PATH and add a Start menu
        /// shortcut.
        /// </summary>
        Tui,

        /// <summary>
        /// A per-user system tray agent. Installers can register it to start at sign-in, start it
        /// after installing, and stop it before upgrading or uninstalling.
        /// </summary>
        TrayAgent
    }
}
