namespace S3Drive.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Writes SHA256SUMS over every file in the version's installers directory, in the format
    /// sha256sum -c and shasum -a 256 -c read. Runs on any OS.
    /// </summary>
    public class ChecksumsChannel : IChannel
    {
        /// <inheritdoc />
        public string Name
        {
            get { return ChannelNames.Checksums; }
        }

        /// <inheritdoc />
        public string Description
        {
            get { return "SHA256SUMS over everything in installers/<version>"; }
        }

        /// <inheritdoc />
        public bool IsSupportedOnCurrentOS
        {
            get { return true; }
        }

        /// <summary>
        /// Formats checksum lines: lowercase hex, two spaces, file name, LF, sorted by name.
        /// </summary>
        /// <param name="hashes">File name to lowercase hex SHA-256.</param>
        /// <returns>The SHA256SUMS contents.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="hashes"/> is null.</exception>
        public static string Format(IDictionary<string, string> hashes)
        {
            if (hashes == null) throw new ArgumentNullException(nameof(hashes));

            StringBuilder text = new StringBuilder();
            foreach (KeyValuePair<string, string> entry in hashes.OrderBy(h => h.Key, StringComparer.Ordinal))
            {
                text.Append(entry.Value).Append("  ").Append(entry.Key).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>
        /// Computes a file's SHA-256 as lowercase hex.
        /// </summary>
        /// <param name="path">The file.</param>
        /// <param name="token">Cancels the read.</param>
        /// <returns>The hash.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
        /// <exception cref="IOException">Thrown when the file cannot be read.</exception>
        public static async Task<string> HashFileAsync(string path, CancellationToken token)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path must be provided.", nameof(path));

            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true))
            {
                byte[] hash = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
        }

        /// <inheritdoc />
        public void Preflight(ChannelContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!Directory.Exists(context.Layout.InstallersDirectory))
                throw new PublisherException("Nothing to checksum: " + context.Layout.InstallersDirectory + " does not exist. Run the other channels first.");
        }

        /// <inheritdoc />
        public async Task RunAsync(ChannelContext context, CancellationToken token)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            string checksums = context.Layout.ChecksumsFile;
            Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(context.Layout.InstallersDirectory))
            {
                if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(checksums), StringComparison.OrdinalIgnoreCase)) continue;
                hashes[Path.GetFileName(file)] = await HashFileAsync(file, token).ConfigureAwait(false);
            }

            if (!hashes.Any()) throw new PublisherException("Nothing to checksum in " + context.Layout.InstallersDirectory + ".");

            string text = Format(hashes);
            await File.WriteAllTextAsync(checksums, text, new UTF8Encoding(false), token).ConfigureAwait(false);
            context.Output.WriteLine("== checksums -> " + checksums);
            context.Output.Write(text);
        }
    }
}
