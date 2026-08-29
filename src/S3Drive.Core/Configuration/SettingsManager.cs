namespace S3Drive.Core.Configuration
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Serialization;

    /// <summary>
    /// Loads and saves <see cref="S3DriveSettings"/> from the configuration file. Writes are
    /// atomic (temp file plus move); reads tolerate a concurrent replace. Environment variables
    /// prefixed with S3DRIVE_ override selected values after load.
    /// </summary>
    public class SettingsManager
    {
        private readonly S3DrivePaths _Paths;
        private readonly Func<string, string?> _EnvironmentReader;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="paths">The path resolver. When null, a default <see cref="S3DrivePaths"/> is used.</param>
        /// <param name="environmentReader">
        /// A function that reads an environment variable by name. When null, the process
        /// environment is used. Injectable for testing.
        /// </param>
        public SettingsManager(S3DrivePaths? paths = null, Func<string, string?>? environmentReader = null)
        {
            _Paths = paths ?? new S3DrivePaths();
            _EnvironmentReader = environmentReader ?? Environment.GetEnvironmentVariable;
        }

        /// <summary>
        /// Loads settings, creating a default file if none exists, then applies environment overrides.
        /// </summary>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The loaded settings. Never null.</returns>
        public async Task<S3DriveSettings> LoadAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            _Paths.EnsureDirectories();

            S3DriveSettings settings;
            if (!File.Exists(_Paths.ConfigFile))
            {
                settings = new S3DriveSettings();
                await SaveAsync(settings, token).ConfigureAwait(false);
            }
            else
            {
                string json = await ReadAllTextSharedAsync(_Paths.ConfigFile, token).ConfigureAwait(false);
                settings = Parse(json);
            }

            ApplyEnvironmentOverrides(settings);
            return settings;
        }

        /// <summary>
        /// Saves settings atomically to the configuration file.
        /// </summary>
        /// <param name="settings">The settings to save. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        public async Task SaveAsync(S3DriveSettings settings, CancellationToken token = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            token.ThrowIfCancellationRequested();
            _Paths.EnsureDirectories();

            string json = JsonSerializer.Serialize(settings, S3DriveJson.Options);
            string finalPath = _Paths.ConfigFile;
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
        /// Parses configuration JSON into a settings object.
        /// </summary>
        /// <param name="json">The JSON text. Cannot be null.</param>
        /// <returns>The parsed settings, or a default instance when the JSON is null content.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is null.</exception>
        public S3DriveSettings Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            S3DriveSettings? parsed = JsonSerializer.Deserialize<S3DriveSettings>(json, S3DriveJson.Options);
            return parsed ?? new S3DriveSettings();
        }

        /// <summary>
        /// Applies S3DRIVE_ environment overrides to the given settings in place.
        /// </summary>
        /// <param name="settings">The settings to mutate. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        public void ApplyEnvironmentOverrides(S3DriveSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            if (TryGetBool("S3DRIVE_LOG_CONSOLE", out bool consoleLogging)) settings.Logging.ConsoleLogging = consoleLogging;
            if (TryGetBool("S3DRIVE_LOG_FILE", out bool fileLogging)) settings.Logging.FileLogging = fileLogging;
            if (TryGetInt("S3DRIVE_METADATA_CACHE_SECONDS", out int cacheSeconds)) settings.MetadataCacheSeconds = cacheSeconds;
            if (TryGetLong("S3DRIVE_MULTIPART_THRESHOLD_BYTES", out long threshold)) settings.MultipartThresholdBytes = threshold;
        }

        private bool TryGetBool(string name, out bool value)
        {
            value = false;
            string? raw = _EnvironmentReader(name);
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return bool.TryParse(raw.Trim(), out value);
        }

        private bool TryGetInt(string name, out int value)
        {
            value = 0;
            string? raw = _EnvironmentReader(name);
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return int.TryParse(raw.Trim(), out value);
        }

        private bool TryGetLong(string name, out long value)
        {
            value = 0;
            string? raw = _EnvironmentReader(name);
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return long.TryParse(raw.Trim(), out value);
        }

        private static async Task<string> ReadAllTextSharedAsync(string path, CancellationToken token)
        {
            int attempts = 0;
            while (true)
            {
                attempts++;
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return await reader.ReadToEndAsync(token).ConfigureAwait(false);
                    }
                }
                catch (IOException) when (attempts < 12)
                {
                    await Task.Delay(20, token).ConfigureAwait(false);
                }
            }
        }
    }
}
