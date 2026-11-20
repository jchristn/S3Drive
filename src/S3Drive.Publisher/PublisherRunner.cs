namespace S3Drive.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Publisher.Channels;
    using S3Drive.Publisher.Manifest;
    using S3Drive.Publisher.Tooling;

    /// <summary>
    /// Loads the manifest, resolves the version and runtimes, and runs one channel.
    /// </summary>
    public class PublisherRunner
    {
        private readonly TextWriter _Output;
        private readonly ChannelRegistry _Channels = new ChannelRegistry();

        /// <summary>
        /// Initializes the runner.
        /// </summary>
        /// <param name="output">Where progress is written.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="output"/> is null.</exception>
        public PublisherRunner(TextWriter output)
        {
            _Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <summary>
        /// Finds the repository root: the nearest directory at or above <paramref name="start"/>
        /// that holds publisher.json.
        /// </summary>
        /// <param name="start">Where to start searching.</param>
        /// <returns>The root directory.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="start"/> is null or empty.</exception>
        /// <exception cref="PublisherException">Thrown when no parent holds publisher.json.</exception>
        public static string FindRoot(string start)
        {
            if (string.IsNullOrEmpty(start)) throw new ArgumentException("Start directory must be provided.", nameof(start));

            DirectoryInfo? directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, ManifestLoader.FileName))) return directory.FullName;
                directory = directory.Parent;
            }

            throw new PublisherException("No " + ManifestLoader.FileName + " found in " + start + " or any parent directory. Pass --root <repository root>.");
        }

        /// <summary>
        /// Runs the action the options ask for.
        /// </summary>
        /// <param name="options">The parsed options.</param>
        /// <param name="token">Cancels the run.</param>
        /// <returns>The process exit code: 0 on success.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        /// <exception cref="PublisherException">Thrown when packaging cannot proceed.</exception>
        public async Task<int> RunAsync(PublisherOptions options, CancellationToken token)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            if (options.ShowHelp)
            {
                _Output.Write(PublisherOptions.Usage);
                return 0;
            }

            if (options.ListChannels)
            {
                foreach (IChannel channel in _Channels.All)
                {
                    _Output.WriteLine(channel.Name.PadRight(10) + (channel.IsSupportedOnCurrentOS ? "          " : "(not here)") + "  " + channel.Description);
                }

                return 0;
            }

            string root = options.Root != null ? Path.GetFullPath(options.Root) : FindRoot(Directory.GetCurrentDirectory());
            PublisherManifest manifest = ManifestLoader.Load(Path.Combine(root, ManifestLoader.FileName));
            string version = options.Version ?? VersionResolver.ReadFromFile(ManifestLoader.Resolve(root, manifest.Version.File));
            VersionResolver.Validate(version, options.Version != null ? "--version" : manifest.Version.File);

            if (options.PrintVersion)
            {
                _Output.WriteLine(version);
                return 0;
            }

            IChannel selected = _Channels.Find(options.Channel!);
            if (!manifest.Channels.Contains(selected.Name, StringComparer.Ordinal))
                throw new PublisherException("Channel '" + selected.Name + "' is not enabled in " + ManifestLoader.FileName + " (channels: " + string.Join(", ", manifest.Channels) + ").");
            if (!selected.IsSupportedOnCurrentOS)
                throw new PublisherException("Channel '" + selected.Name + "' cannot run on this operating system. " + selected.Description + ".");

            List<string> runtimes = options.Runtimes.Any() ? options.Runtimes : manifest.Build.Runtimes;
            List<string> unknown = runtimes.Where(r => !manifest.Build.Runtimes.Contains(r, StringComparer.Ordinal)).ToList();
            if (unknown.Any())
                throw new PublisherException("Runtime(s) " + string.Join(", ", unknown) + " are not in build.runtimes (" + string.Join(", ", manifest.Build.Runtimes) + ").");

            PublisherLayout layout = new PublisherLayout(root, manifest.Project.PackageName, version);
            ChannelContext context = new ChannelContext(manifest, version, runtimes, layout, _Output, new ToolLocator(), new ProcessRunner(_Output));

            _Output.WriteLine(manifest.Project.Name + " " + version + ": channel " + selected.Name + " for " + string.Join(", ", runtimes));
            selected.Preflight(context);
            await selected.RunAsync(context, token).ConfigureAwait(false);
            return 0;
        }
    }
}
