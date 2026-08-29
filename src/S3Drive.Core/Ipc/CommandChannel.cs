namespace S3Drive.Core.Ipc
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Serialization;

    /// <summary>
    /// A file-based command channel: the TUI drops a command file into the agent's command
    /// directory, and the agent reads, executes, and deletes it. No network surface is involved.
    /// </summary>
    public static class CommandChannel
    {
        /// <summary>
        /// Writes a command to the command directory as a uniquely named JSON file.
        /// </summary>
        /// <param name="paths">The path resolver. Cannot be null.</param>
        /// <param name="command">The command to send. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths"/> or <paramref name="command"/> is null.</exception>
        public static async Task SendAsync(S3DrivePaths paths, AgentCommand command, CancellationToken token)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (command == null) throw new ArgumentNullException(nameof(command));

            paths.EnsureDirectories();

            string json = JsonSerializer.Serialize(command, S3DriveJson.Options);
            string finalName = "cmd-" + Guid.NewGuid().ToString("N") + ".json";
            string finalPath = Path.Combine(paths.CommandDirectory, finalName);
            string tempPath = finalPath + ".tmp";

            using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteAsync(json.AsMemory(), token).ConfigureAwait(false);
                await writer.FlushAsync(token).ConfigureAwait(false);
            }

            File.Move(tempPath, finalPath, true);
        }

        /// <summary>
        /// Returns the paths of pending command files (excluding partially written temp files).
        /// </summary>
        /// <param name="paths">The path resolver. Cannot be null.</param>
        /// <returns>The pending command file paths, oldest first. Never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths"/> is null.</exception>
        public static IReadOnlyList<string> ListPending(S3DrivePaths paths)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (!Directory.Exists(paths.CommandDirectory)) return Array.Empty<string>();

            List<string> files = new List<string>(Directory.GetFiles(paths.CommandDirectory, "cmd-*.json"));
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        /// <summary>
        /// Attempts to read and parse a command file.
        /// </summary>
        /// <param name="filePath">The command file path. Cannot be null or empty.</param>
        /// <param name="command">The parsed command on success; otherwise null.</param>
        /// <returns>True when the file was read and parsed; otherwise false.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is null or empty.</exception>
        public static bool TryRead(string filePath, out AgentCommand? command)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("File path must be provided.", nameof(filePath));

            command = null;
            try
            {
                string json = File.ReadAllText(filePath);
                command = JsonSerializer.Deserialize<AgentCommand>(json, S3DriveJson.Options);
                return command != null;
            }
            catch (IOException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
