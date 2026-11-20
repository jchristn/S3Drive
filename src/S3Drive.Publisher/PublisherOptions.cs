namespace S3Drive.Publisher
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Parsed command-line options.
    /// </summary>
    public class PublisherOptions
    {
        /// <summary>
        /// The usage text printed by --help.
        /// </summary>
        public const string Usage =
            "S3Drive.Publisher: builds installers from publisher.json, one channel at a time.\n" +
            "\n" +
            "Usage:\n" +
            "  S3Drive.Publisher --channel <name> [--rid <rid>[,<rid>...]] [--version <semver>] [--root <dir>]\n" +
            "  S3Drive.Publisher --list-channels\n" +
            "  S3Drive.Publisher --print-version\n" +
            "\n" +
            "Options:\n" +
            "  --channel <name>    publish, zip, inno, or checksums. Run them in that order.\n" +
            "  --rid <rid>         Limit to these runtime identifiers (repeatable or comma-separated).\n" +
            "                      Default: every runtime in publisher.json.\n" +
            "  --version <semver>  Override the version read from the manifest's version file.\n" +
            "  --root <dir>        The repository root. Default: the nearest parent with publisher.json.\n" +
            "  --list-channels     List channels and whether this OS can run them.\n" +
            "  --print-version     Print the release version and exit.\n" +
            "  --help              Show this help.\n" +
            "\n" +
            "Outputs land in installers/<version>/; staging lands in artifacts/publisher/<version>/.\n";

        /// <summary>
        /// The channel to run, or null.
        /// </summary>
        public string? Channel { get; set; } = null;

        /// <summary>
        /// The runtimes to build. Empty means every runtime in the manifest.
        /// </summary>
        public List<string> Runtimes { get; set; } = new List<string>();

        /// <summary>
        /// A version override, or null to read the manifest's version file.
        /// </summary>
        public string? Version { get; set; } = null;

        /// <summary>
        /// The repository root, or null to search upward from the current directory.
        /// </summary>
        public string? Root { get; set; } = null;

        /// <summary>
        /// Whether to list channels and exit.
        /// </summary>
        public bool ListChannels { get; set; } = false;

        /// <summary>
        /// Whether to print the release version and exit.
        /// </summary>
        public bool PrintVersion { get; set; } = false;

        /// <summary>
        /// Whether to print usage and exit.
        /// </summary>
        public bool ShowHelp { get; set; } = false;

        /// <summary>
        /// Parses command-line arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        /// <returns>The options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="args"/> is null.</exception>
        /// <exception cref="PublisherException">Thrown for an unknown option or a missing value.</exception>
        public static PublisherOptions Parse(string[] args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            PublisherOptions options = new PublisherOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--channel":
                        options.Channel = Value(args, ref i, arg);
                        break;
                    case "--rid":
                        options.Runtimes.AddRange(Value(args, ref i, arg).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        break;
                    case "--version":
                        options.Version = Value(args, ref i, arg);
                        break;
                    case "--root":
                        options.Root = Value(args, ref i, arg);
                        break;
                    case "--list-channels":
                        options.ListChannels = true;
                        break;
                    case "--print-version":
                        options.PrintVersion = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        options.ShowHelp = true;
                        break;
                    default:
                        throw new PublisherException("Unknown option '" + arg + "'. Run with --help for usage.");
                }
            }

            options.Runtimes = options.Runtimes.Distinct(StringComparer.Ordinal).ToList();
            if (!options.ShowHelp && !options.ListChannels && !options.PrintVersion && options.Channel == null)
                throw new PublisherException("Pass --channel <name>, --list-channels, or --print-version. Run with --help for usage.");
            return options;
        }

        private static string Value(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new PublisherException(option + " needs a value.");
            index++;
            return args[index];
        }
    }
}
