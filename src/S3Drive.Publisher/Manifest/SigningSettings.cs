namespace S3Drive.Publisher.Manifest
{
    /// <summary>
    /// Code-signing settings per operating system.
    /// </summary>
    public class SigningSettings
    {
        /// <summary>
        /// Windows signing (Authenticode, through Inno Setup's SignTool). Defaults to not
        /// configured.
        /// </summary>
        public SigningTarget Windows { get; set; } = new SigningTarget();
    }
}
