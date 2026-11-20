namespace S3Drive.Publisher.Manifest
{
    /// <summary>
    /// Code-signing settings for one operating system. Releases are unsigned by policy, so this is
    /// wired but switched off; turning it on is a secrets change, not a rewrite.
    /// </summary>
    public class SigningTarget
    {
        /// <summary>
        /// Whether signing runs. Defaults to false.
        /// </summary>
        public bool IsConfigured { get; set; } = false;

        /// <summary>
        /// The signing command line, with $f standing for the file to sign and %NAME% environment
        /// references expanded at run time so secrets stay out of the manifest (for example
        /// signtool sign /f "%WINDOWS_SIGNING_PFX%" /p "%WINDOWS_SIGNING_PASSWORD%" /fd sha256 $f).
        /// Required when <see cref="IsConfigured"/> is true.
        /// </summary>
        public string Command { get; set; } = string.Empty;
    }
}
