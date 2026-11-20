namespace S3Drive.Publisher.Manifest
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Reads and validates publisher.json. Unknown properties are rejected so a typo fails loudly
    /// instead of silently dropping a setting. Thread-safe: holds no mutable state.
    /// </summary>
    public static class ManifestLoader
    {
        /// <summary>
        /// The manifest file name, at the repository root.
        /// </summary>
        public const string FileName = "publisher.json";

        private static readonly Regex _PackageNamePattern = new Regex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex _RuntimePattern = new Regex("^(win|osx|linux)-(x64|arm64)$", RegexOptions.CultureInvariant);
        private static readonly Regex _ColorPattern = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Loads and validates a manifest file.
        /// </summary>
        /// <param name="path">The manifest path.</param>
        /// <returns>The validated manifest.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
        /// <exception cref="PublisherException">Thrown when the file is missing, is not valid JSON, or fails validation.</exception>
        public static PublisherManifest Load(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Manifest path must be provided.", nameof(path));
            if (!File.Exists(path)) throw new PublisherException("Manifest not found: " + path + ". Run the Publisher from the repository root or pass --root.");

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                throw new PublisherException("Could not read " + path + ": " + ex.Message, ex);
            }

            return Parse(json);
        }

        /// <summary>
        /// Parses and validates manifest JSON.
        /// </summary>
        /// <param name="json">The manifest JSON.</param>
        /// <returns>The validated manifest.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is null.</exception>
        /// <exception cref="PublisherException">Thrown when the JSON is invalid or fails validation.</exception>
        public static PublisherManifest Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            PublisherManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<PublisherManifest>(json, CreateOptions());
            }
            catch (JsonException ex)
            {
                throw new PublisherException(FileName + " is not valid: " + ex.Message, ex);
            }

            if (manifest == null) throw new PublisherException(FileName + " is empty.");
            Validate(manifest);
            return manifest;
        }

        /// <summary>
        /// Checks that every file the manifest references exists under the repository root.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="root">The repository root.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="manifest"/> or <paramref name="root"/> is null.</exception>
        /// <exception cref="PublisherException">Thrown when a referenced file is missing.</exception>
        public static void ValidateFiles(PublisherManifest manifest, string root)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (root == null) throw new ArgumentNullException(nameof(root));

            List<string> missing = new List<string>();
            CheckFile(root, manifest.Project.LicenseFile, "project.licenseFile", missing);
            CheckFile(root, manifest.Project.Logo, "project.logo", missing);
            CheckFile(root, manifest.Version.File, "version.file", missing);
            foreach (ArtifactInfo artifact in manifest.Artifacts)
            {
                CheckFile(root, artifact.Project, "artifacts[" + artifact.Name + "].project", missing);
            }

            if (missing.Any()) throw new PublisherException(FileName + " references missing files: " + string.Join("; ", missing));
        }

        /// <summary>
        /// Resolves a manifest-relative path against the repository root.
        /// </summary>
        /// <param name="root">The repository root.</param>
        /// <param name="relativePath">A path from the manifest, with forward or back slashes.</param>
        /// <returns>The full path.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
        public static string Resolve(string root, string relativePath)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (relativePath == null) throw new ArgumentNullException(nameof(relativePath));

            string normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(root, normalized));
        }

        private static JsonSerializerOptions CreateOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private static void Validate(PublisherManifest manifest)
        {
            List<string> errors = new List<string>();

            ProjectInfo project = manifest.Project ?? new ProjectInfo();
            Require(project.Name, "project.name", errors);
            Require(project.PackageName, "project.packageName", errors);
            Require(project.Identifier, "project.identifier", errors);
            Require(project.Tagline, "project.tagline", errors);
            Require(project.License, "project.license", errors);
            Require(project.LicenseFile, "project.licenseFile", errors);
            Require(project.Logo, "project.logo", errors);
            Require(project.Publisher, "project.publisher", errors);
            if (!string.IsNullOrEmpty(project.PackageName) && !_PackageNamePattern.IsMatch(project.PackageName))
                errors.Add("project.packageName '" + project.PackageName + "' must be lowercase letters, digits, and hyphens");

            if (manifest.Version == null || string.IsNullOrWhiteSpace(manifest.Version.File)) errors.Add("version.file is required");

            BuildSettings build = manifest.Build ?? new BuildSettings();
            Require(build.Framework, "build.framework", errors);
            Require(build.Configuration, "build.configuration", errors);
            if (build.Runtimes == null || !build.Runtimes.Any()) errors.Add("build.runtimes needs at least one runtime identifier");
            else
            {
                foreach (string rid in build.Runtimes)
                {
                    if (rid == null || !_RuntimePattern.IsMatch(rid)) errors.Add("build.runtimes entry '" + rid + "' is not a supported runtime identifier");
                }

                if (build.Runtimes.Distinct(StringComparer.Ordinal).Count() != build.Runtimes.Count) errors.Add("build.runtimes has duplicates");
            }

            if (manifest.Artifacts == null || !manifest.Artifacts.Any()) errors.Add("artifacts needs at least one entry");
            else
            {
                HashSet<string> commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> executables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < manifest.Artifacts.Count; i++)
                {
                    ArtifactInfo artifact = manifest.Artifacts[i];
                    string label = "artifacts[" + i + "]";
                    if (artifact == null)
                    {
                        errors.Add(label + " is null");
                        continue;
                    }

                    Require(artifact.Name, label + ".name", errors);
                    Require(artifact.Project, label + ".project", errors);
                    Require(artifact.Executable, label + ".executable", errors);
                    if (!string.IsNullOrEmpty(artifact.Executable) && !executables.Add(artifact.Executable))
                        errors.Add(label + ".executable '" + artifact.Executable + "' is used by more than one artifact");
                    if (artifact.Command != null)
                    {
                        if (!_PackageNamePattern.IsMatch(artifact.Command)) errors.Add(label + ".command '" + artifact.Command + "' must be lowercase letters, digits, and hyphens");
                        else if (!commands.Add(artifact.Command)) errors.Add(label + ".command '" + artifact.Command + "' is used by more than one artifact");
                    }

                    if (artifact.Startup != null && artifact.Kind != ArtifactKindEnum.TrayAgent)
                        errors.Add(label + ".startup is only allowed for TrayAgent artifacts");
                }
            }

            if (manifest.Channels == null) errors.Add("channels is required");
            else
            {
                foreach (string channel in manifest.Channels)
                {
                    if (!ChannelNames.All.Contains(channel, StringComparer.Ordinal))
                        errors.Add("channels entry '" + channel + "' is unknown; known channels are " + string.Join(", ", ChannelNames.All));
                }
            }

            WindowsSettings windows = manifest.Windows ?? new WindowsSettings();
            if (windows.WizardBackground == null || !_ColorPattern.IsMatch(windows.WizardBackground))
                errors.Add("windows.wizardBackground must be a #RRGGBB color");
            if (windows.Prerequisites != null)
            {
                for (int i = 0; i < windows.Prerequisites.Count; i++)
                {
                    PrerequisiteInfo prerequisite = windows.Prerequisites[i];
                    string label = "windows.prerequisites[" + i + "]";
                    if (prerequisite == null)
                    {
                        errors.Add(label + " is null");
                        continue;
                    }

                    Require(prerequisite.Name, label + ".name", errors);
                    Require(prerequisite.DetectFile, label + ".detectFile", errors);
                    Require(prerequisite.Url, label + ".url", errors);
                    Require(prerequisite.Message, label + ".message", errors);
                }
            }

            SigningSettings signing = manifest.Signing ?? new SigningSettings();
            if (signing.Windows != null && signing.Windows.IsConfigured && string.IsNullOrWhiteSpace(signing.Windows.Command))
                errors.Add("signing.windows.command is required when signing.windows.isConfigured is true");

            if (errors.Any()) throw new PublisherException(FileName + " is invalid: " + string.Join("; ", errors) + ".");
        }

        private static void Require(string? value, string name, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add(name + " is required");
        }

        private static void CheckFile(string root, string relativePath, string name, List<string> missing)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return;
            string full = Resolve(root, relativePath);
            if (!File.Exists(full)) missing.Add(name + " (" + full + ")");
        }
    }
}
