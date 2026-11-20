namespace S3Drive.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using S3Drive.Publisher.Manifest;
    using S3Drive.Publisher.Tooling;

    /// <summary>
    /// What a channel run needs: the manifest, version, runtimes, layout, and tools.
    /// </summary>
    public class ChannelContext
    {
        /// <summary>
        /// Initializes the context.
        /// </summary>
        /// <param name="manifest">The validated manifest.</param>
        /// <param name="version">The release version.</param>
        /// <param name="runtimes">The runtime identifiers to build; a subset of the manifest's.</param>
        /// <param name="layout">The directory layout.</param>
        /// <param name="output">Where progress is written.</param>
        /// <param name="tools">The tool locator.</param>
        /// <param name="processes">The process runner.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="version"/> is empty.</exception>
        public ChannelContext(PublisherManifest manifest, string version, IReadOnlyList<string> runtimes, PublisherLayout layout, TextWriter output, ToolLocator tools, ProcessRunner processes)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrEmpty(version)) throw new ArgumentException("Version must be provided.", nameof(version));
            Version = version;
            Runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Output = output ?? throw new ArgumentNullException(nameof(output));
            Tools = tools ?? throw new ArgumentNullException(nameof(tools));
            Processes = processes ?? throw new ArgumentNullException(nameof(processes));
        }

        /// <summary>
        /// The validated manifest.
        /// </summary>
        public PublisherManifest Manifest { get; }

        /// <summary>
        /// The release version.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// The runtime identifiers this run builds.
        /// </summary>
        public IReadOnlyList<string> Runtimes { get; }

        /// <summary>
        /// The directory layout.
        /// </summary>
        public PublisherLayout Layout { get; }

        /// <summary>
        /// Where progress is written.
        /// </summary>
        public TextWriter Output { get; }

        /// <summary>
        /// The tool locator.
        /// </summary>
        public ToolLocator Tools { get; }

        /// <summary>
        /// The process runner.
        /// </summary>
        public ProcessRunner Processes { get; }

        /// <summary>
        /// Resolves a manifest-relative path against the repository root.
        /// </summary>
        /// <param name="relativePath">The manifest path.</param>
        /// <returns>The full path.</returns>
        public string Resolve(string relativePath)
        {
            return ManifestLoader.Resolve(Layout.Root, relativePath);
        }
    }
}
