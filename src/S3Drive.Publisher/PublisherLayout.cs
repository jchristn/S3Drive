namespace S3Drive.Publisher
{
    using System;
    using System.IO;

    /// <summary>
    /// Where the Publisher reads and writes. Final outputs go to installers/&lt;version&gt;/ (git
    /// ignored); intermediate staging goes to artifacts/publisher/&lt;version&gt;/. Thread-safe.
    /// </summary>
    public class PublisherLayout
    {
        private readonly string _Root;
        private readonly string _PackageName;
        private readonly string _Version;

        /// <summary>
        /// Initializes the layout.
        /// </summary>
        /// <param name="root">The repository root.</param>
        /// <param name="packageName">The lowercase package name used in file names.</param>
        /// <param name="version">The release version.</param>
        /// <exception cref="ArgumentException">Thrown when an argument is null or empty.</exception>
        public PublisherLayout(string root, string packageName, string version)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("Root must be provided.", nameof(root));
            if (string.IsNullOrEmpty(packageName)) throw new ArgumentException("Package name must be provided.", nameof(packageName));
            if (string.IsNullOrEmpty(version)) throw new ArgumentException("Version must be provided.", nameof(version));
            _Root = Path.GetFullPath(root);
            _PackageName = packageName;
            _Version = version;
        }

        /// <summary>
        /// The repository root.
        /// </summary>
        public string Root
        {
            get { return _Root; }
        }

        /// <summary>
        /// The final output directory for this version: installers/&lt;version&gt;.
        /// </summary>
        public string InstallersDirectory
        {
            get { return Path.Combine(_Root, "installers", _Version); }
        }

        /// <summary>
        /// The intermediate directory for this version: artifacts/publisher/&lt;version&gt;.
        /// </summary>
        public string WorkDirectory
        {
            get { return Path.Combine(_Root, "artifacts", "publisher", _Version); }
        }

        /// <summary>
        /// Where generated icons and wizard images go.
        /// </summary>
        public string BrandingDirectory
        {
            get { return Path.Combine(WorkDirectory, "branding"); }
        }

        /// <summary>
        /// The SHA256SUMS file in the installers directory.
        /// </summary>
        public string ChecksumsFile
        {
            get { return Path.Combine(InstallersDirectory, "SHA256SUMS"); }
        }

        /// <summary>
        /// The generated icon file name (for example "s3drive.ico").
        /// </summary>
        public string IconFileName
        {
            get { return _PackageName + ".ico"; }
        }

        /// <summary>
        /// The staging directory holding one runtime's installed file set.
        /// </summary>
        /// <param name="runtime">The runtime identifier.</param>
        /// <returns>The directory path.</returns>
        public string StageDirectory(string runtime)
        {
            return Path.Combine(WorkDirectory, runtime, "stage");
        }

        /// <summary>
        /// The generated Inno Setup script for one runtime.
        /// </summary>
        /// <param name="runtime">The runtime identifier.</param>
        /// <returns>The script path.</returns>
        public string InnoScriptFile(string runtime)
        {
            return Path.Combine(WorkDirectory, runtime, _PackageName + ".iss");
        }

        /// <summary>
        /// The portable archive name: &lt;app&gt;-&lt;version&gt;-&lt;rid&gt;.zip.
        /// </summary>
        /// <param name="runtime">The runtime identifier.</param>
        /// <returns>The file name.</returns>
        public string ZipFileName(string runtime)
        {
            return _PackageName + "-" + _Version + "-" + runtime + ".zip";
        }

        /// <summary>
        /// The installer base name without extension: &lt;app&gt;-&lt;version&gt;-&lt;rid&gt;-setup.
        /// </summary>
        /// <param name="runtime">The runtime identifier.</param>
        /// <returns>The base file name.</returns>
        public string SetupBaseName(string runtime)
        {
            return _PackageName + "-" + _Version + "-" + runtime + "-setup";
        }
    }
}
