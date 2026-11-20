namespace S3Drive.Publisher
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Xml;
    using System.Xml.Linq;

    /// <summary>
    /// Reads and checks the release version. Thread-safe: holds no mutable state.
    /// </summary>
    public static class VersionResolver
    {
        private static readonly Regex _SemVerPattern = new Regex(
            "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?(\\+[0-9A-Za-z.-]+)?$",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Reads the Version element from an MSBuild file.
        /// </summary>
        /// <param name="path">The MSBuild file (for example Directory.Build.props).</param>
        /// <returns>The version string.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
        /// <exception cref="PublisherException">Thrown when the file is missing, unreadable, or has no valid Version element.</exception>
        public static string ReadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Version file path must be provided.", nameof(path));
            if (!File.Exists(path)) throw new PublisherException("Version file not found: " + path + ".");

            try
            {
                return ReadFromXml(File.ReadAllText(path), path);
            }
            catch (IOException ex)
            {
                throw new PublisherException("Could not read " + path + ": " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Reads the first Version element from MSBuild XML.
        /// </summary>
        /// <param name="xml">The MSBuild XML.</param>
        /// <param name="sourceName">A name for the XML used in error messages.</param>
        /// <returns>The version string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="xml"/> is null.</exception>
        /// <exception cref="PublisherException">Thrown when the XML is invalid or has no valid Version element.</exception>
        public static string ReadFromXml(string xml, string sourceName)
        {
            if (xml == null) throw new ArgumentNullException(nameof(xml));

            XDocument document;
            try
            {
                document = XDocument.Parse(xml);
            }
            catch (XmlException ex)
            {
                throw new PublisherException(sourceName + " is not valid XML: " + ex.Message, ex);
            }

            XElement? element = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Version");
            if (element == null) throw new PublisherException(sourceName + " has no <Version> element.");

            string version = element.Value.Trim();
            Validate(version, sourceName);
            return version;
        }

        /// <summary>
        /// Checks that a version is a Semantic Versioning 2.0.0 string.
        /// </summary>
        /// <param name="version">The version.</param>
        /// <param name="sourceName">Where the version came from, used in the error message.</param>
        /// <exception cref="PublisherException">Thrown when the version is not valid SemVer.</exception>
        public static void Validate(string? version, string sourceName)
        {
            if (string.IsNullOrEmpty(version) || !_SemVerPattern.IsMatch(version))
                throw new PublisherException("Version '" + version + "' from " + sourceName + " is not a MAJOR.MINOR.PATCH Semantic Version.");
        }

        /// <summary>
        /// Converts a SemVer string into the four-part numeric form Windows version resources need,
        /// dropping any pre-release or build suffix (for example "0.1.1-alpha" becomes "0.1.1.0").
        /// </summary>
        /// <param name="version">A valid SemVer string.</param>
        /// <returns>The numeric version.</returns>
        /// <exception cref="PublisherException">Thrown when the version is not valid SemVer.</exception>
        public static string ToNumericVersion(string version)
        {
            Validate(version, "the version argument");
            string core = version.Split('-', '+')[0];
            return core + ".0";
        }

        /// <summary>
        /// Whether a version is a pre-release: it has a pre-release suffix, or it is a 0.x.y version,
        /// which the project labels alpha.
        /// </summary>
        /// <param name="version">A valid SemVer string.</param>
        /// <returns>True for a pre-release.</returns>
        /// <exception cref="PublisherException">Thrown when the version is not valid SemVer.</exception>
        public static bool IsPrerelease(string version)
        {
            Validate(version, "the version argument");
            string core = version.Split('+')[0];
            return core.Contains('-') || core.StartsWith("0.", StringComparison.Ordinal);
        }
    }
}
