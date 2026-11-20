namespace S3Drive.Publisher.Tooling
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Finds the packaging tools and, when one is missing, fails with the exact command that
    /// installs it. Thread-safe: holds no mutable state.
    /// </summary>
    public class ToolLocator
    {
        /// <summary>
        /// The command that installs Inno Setup on a Windows machine or runner.
        /// </summary>
        public const string InnoInstallHint = "choco install innosetup --no-progress -y   (or: winget install --id JRSoftware.InnoSetup -e)";

        /// <summary>
        /// Finds the dotnet host: the one running the Publisher (DOTNET_HOST_PATH), then PATH.
        /// </summary>
        /// <returns>The dotnet executable.</returns>
        /// <exception cref="PublisherException">Thrown when dotnet cannot be found.</exception>
        public string FindDotnet()
        {
            string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrEmpty(host) && File.Exists(host)) return host;

            string? onPath = FindOnPath("dotnet");
            if (onPath != null) return onPath;

            throw new PublisherException("The .NET SDK was not found on PATH. Install the .NET 10 SDK (which also builds net8.0) from https://dot.net/download.");
        }

        /// <summary>
        /// Finds the Inno Setup command-line compiler (6.3 or later): PATH, then every "Inno Setup *"
        /// folder under Program Files and the per-user Programs folder, newest major version first.
        /// </summary>
        /// <returns>The ISCC.exe path.</returns>
        /// <exception cref="PublisherException">Thrown when Inno Setup is not installed.</exception>
        public string FindIscc()
        {
            string? onPath = FindOnPath("iscc");
            if (onPath != null) return onPath;

            List<string> candidates = new List<string>();
            AddCandidates(candidates, Environment.GetEnvironmentVariable("ProgramFiles(x86)"));
            AddCandidates(candidates, Environment.GetEnvironmentVariable("ProgramFiles"));
            string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData)) AddCandidates(candidates, Path.Combine(localAppData, "Programs"));

            string? found = candidates.FirstOrDefault(File.Exists);
            if (found != null) return found;

            throw new PublisherException("Inno Setup (ISCC.exe) was not found on PATH or in Program Files. Install it with: " + InnoInstallHint);
        }

        /// <summary>
        /// Searches PATH for an executable, trying PATHEXT extensions on Windows.
        /// </summary>
        /// <param name="name">The executable name without extension.</param>
        /// <returns>The full path, or null when not found.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or empty.</exception>
        public static string? FindOnPath(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Executable name must be provided.", nameof(name));

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            List<string> extensions = new List<string> { string.Empty };
            if (OperatingSystem.IsWindows())
            {
                string pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
                extensions = pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string extension in extensions)
                {
                    string candidate;
                    try
                    {
                        candidate = Path.Combine(directory.Trim('"'), name + extension);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    if (File.Exists(candidate)) return candidate;
                }
            }

            return null;
        }

        private static void AddCandidates(List<string> candidates, string? baseDirectory)
        {
            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory)) return;

            IEnumerable<string> installs;
            try
            {
                installs = Directory.GetDirectories(baseDirectory, "Inno Setup *")
                    .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }

            candidates.AddRange(installs.Select(d => Path.Combine(d, "ISCC.exe")));
        }
    }
}
