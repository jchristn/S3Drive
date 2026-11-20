namespace S3Drive.Publisher.Manifest
{
    using System.Collections.Generic;

    /// <summary>
    /// How artifacts are compiled: the framework, configuration, runtime identifier matrix, and
    /// which publish outputs to leave out of the packages.
    /// </summary>
    public class BuildSettings
    {
        /// <summary>
        /// The target framework to publish (for example "net8.0"). Required.
        /// </summary>
        public string Framework { get; set; } = string.Empty;

        /// <summary>
        /// The build configuration. Defaults to "Release".
        /// </summary>
        public string Configuration { get; set; } = "Release";

        /// <summary>
        /// The runtime identifiers to build (for example "win-x64" and "win-arm64"). At least one
        /// is required.
        /// </summary>
        public List<string> Runtimes { get; set; } = new List<string>();

        /// <summary>
        /// File-name patterns (with * and ? wildcards) deleted from the publish output before
        /// packaging, such as native debug symbols that NuGet packages drop beside the executable.
        /// Defaults to empty.
        /// </summary>
        public List<string> ExcludeFromStage { get; set; } = new List<string>();
    }
}
