namespace S3Drive.Core.Ipc
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Serialization;

    /// <summary>
    /// Reads and writes the agent-published status document. Writes are atomic; reads tolerate a
    /// concurrent replace and return null when no status has been published yet.
    /// </summary>
    public static class StatusStore
    {
        /// <summary>
        /// Writes the status document atomically.
        /// </summary>
        /// <param name="paths">The path resolver. Cannot be null.</param>
        /// <param name="status">The status to publish. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths"/> or <paramref name="status"/> is null.</exception>
        public static async Task WriteAsync(S3DrivePaths paths, AgentStatus status, CancellationToken token)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (status == null) throw new ArgumentNullException(nameof(status));

            paths.EnsureDirectories();

            string json = JsonSerializer.Serialize(status, S3DriveJson.Options);
            string finalPath = paths.StatusFile;
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
        /// Reads the status document, or null when none has been published.
        /// </summary>
        /// <param name="paths">The path resolver. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The published status, or null when absent or unreadable.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths"/> is null.</exception>
        public static async Task<AgentStatus?> ReadAsync(S3DrivePaths paths, CancellationToken token)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (!File.Exists(paths.StatusFile)) return null;

            try
            {
                using (FileStream stream = new FileStream(paths.StatusFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string json = await reader.ReadToEndAsync(token).ConfigureAwait(false);
                    return JsonSerializer.Deserialize<AgentStatus>(json, S3DriveJson.Options);
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
