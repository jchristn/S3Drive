namespace S3Drive.Publisher.Manifest
{
    using System.Collections.Generic;

    /// <summary>
    /// Windows installer settings that are not part of the product identity.
    /// </summary>
    public class WindowsSettings
    {
        /// <summary>
        /// Separately installed dependencies to check for. Defaults to empty.
        /// </summary>
        public List<PrerequisiteInfo> Prerequisites { get; set; } = new List<PrerequisiteInfo>();

        /// <summary>
        /// Startup Run-key value names registered by earlier tooling for the current user. The
        /// installer deletes them so upgraded machines do not start the agent twice. Defaults to
        /// empty.
        /// </summary>
        public List<string> LegacyStartupValues { get; set; } = new List<string>();

        /// <summary>
        /// The wizard image background as #RRGGBB. Defaults to "#FFFFFF", which matches the modern
        /// wizard style.
        /// </summary>
        public string WizardBackground { get; set; } = "#FFFFFF";
    }
}
