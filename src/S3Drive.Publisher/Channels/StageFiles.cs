namespace S3Drive.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using S3Drive.Publisher.Manifest;

    /// <summary>
    /// Helpers for the per-runtime staging directory that holds exactly the files an install puts
    /// on disk. Thread-safe: holds no state.
    /// </summary>
    public static class StageFiles
    {
        /// <summary>
        /// The installed license file name.
        /// </summary>
        public const string LicenseFileName = "LICENSE.txt";

        /// <summary>
        /// Deletes and recreates a directory.
        /// </summary>
        /// <param name="directory">The directory.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="directory"/> is null or empty.</exception>
        /// <exception cref="PublisherException">Thrown when the directory cannot be cleared, usually because a file in it is open.</exception>
        public static void Reset(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("Directory must be provided.", nameof(directory));
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                Directory.CreateDirectory(directory);
            }
            catch (IOException ex)
            {
                throw new PublisherException("Could not clear " + directory + " (is a file in it open?): " + ex.Message, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new PublisherException("Could not clear " + directory + ": " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Deletes top-level files matching any of the patterns.
        /// </summary>
        /// <param name="directory">The staging directory.</param>
        /// <param name="patterns">Patterns with * and ? wildcards.</param>
        /// <returns>The names of the deleted files.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static List<string> RemoveExcluded(string directory, IEnumerable<string> patterns)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            if (patterns == null) throw new ArgumentNullException(nameof(patterns));

            List<string> removed = new List<string>();
            foreach (string pattern in patterns.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                foreach (string file in Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                {
                    File.Delete(file);
                    removed.Add(Path.GetFileName(file));
                }
            }

            removed.Sort(StringComparer.OrdinalIgnoreCase);
            return removed;
        }

        /// <summary>
        /// Checks that a staging directory holds every artifact's executable.
        /// </summary>
        /// <param name="directory">The staging directory.</param>
        /// <param name="manifest">The manifest.</param>
        /// <param name="runtime">The runtime, used in the error message.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="PublisherException">Thrown when the stage is missing or incomplete.</exception>
        public static void RequireArtifacts(string directory, PublisherManifest manifest, string runtime)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            if (!Directory.Exists(directory))
                throw new PublisherException("Nothing is staged for " + runtime + " at " + directory + ". Run --channel " + ChannelNames.Publish + " first.");

            List<string> missing = manifest.Artifacts
                .Select(a => a.Executable)
                .Where(e => !File.Exists(Path.Combine(directory, e)))
                .ToList();
            if (missing.Any())
                throw new PublisherException("The " + runtime + " stage at " + directory + " is missing " + string.Join(", ", missing)
                    + ". Check each artifact's executable in publisher.json, then run --channel " + ChannelNames.Publish + " again.");
        }

        /// <summary>
        /// The Windows command shim that makes a command name run an executable in the same folder,
        /// passing arguments and the exit code through.
        /// </summary>
        /// <param name="executable">The executable file name.</param>
        /// <returns>The .cmd file contents with CRLF line endings.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="executable"/> is null or empty.</exception>
        public static string CommandShim(string executable)
        {
            if (string.IsNullOrEmpty(executable)) throw new ArgumentException("Executable must be provided.", nameof(executable));
            return "@echo off\r\n\"%~dp0" + executable + "\" %*\r\nexit /b %ERRORLEVEL%\r\n";
        }

        /// <summary>
        /// Normalizes text to CRLF line endings, so Notepad and the installer's license page show it
        /// correctly whatever the checkout's line endings were.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The text with CRLF line endings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
        public static string ToCrlf(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        }
    }
}
