namespace S3Drive.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Publisher.Imaging;
    using S3Drive.Publisher.Manifest;

    /// <summary>
    /// Publishes every artifact self-contained and single-file for each runtime into its staging
    /// directory, then adds the command shims, the license, and the generated icon. Runs on any OS,
    /// since .NET cross-compiles for every runtime identifier.
    /// </summary>
    public class PublishChannel : IChannel
    {
        /// <inheritdoc />
        public string Name
        {
            get { return ChannelNames.Publish; }
        }

        /// <inheritdoc />
        public string Description
        {
            get { return "Self-contained single-file dotnet publish of every artifact into the staging directory"; }
        }

        /// <inheritdoc />
        public bool IsSupportedOnCurrentOS
        {
            get { return true; }
        }

        /// <summary>
        /// The dotnet publish arguments for one artifact and runtime.
        /// </summary>
        /// <param name="projectPath">The project file.</param>
        /// <param name="build">The build settings.</param>
        /// <param name="runtime">The runtime identifier.</param>
        /// <param name="version">The version stamped into the binaries.</param>
        /// <param name="outputDirectory">The staging directory.</param>
        /// <returns>The arguments.</returns>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public static List<string> PublishArguments(string projectPath, BuildSettings build, string runtime, string version, string outputDirectory)
        {
            if (projectPath == null) throw new ArgumentNullException(nameof(projectPath));
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (version == null) throw new ArgumentNullException(nameof(version));
            if (outputDirectory == null) throw new ArgumentNullException(nameof(outputDirectory));

            return new List<string>
            {
                "publish", projectPath,
                "-c", build.Configuration,
                "-f", build.Framework,
                "-r", runtime,
                "--self-contained", "true",
                "-p:PublishSingleFile=true",
                "-p:IncludeNativeLibrariesForSelfExtract=true",
                "-p:DebugType=none",
                "-p:Version=" + version,
                "-o", outputDirectory,
                "-nologo"
            };
        }

        /// <inheritdoc />
        public void Preflight(ChannelContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.Tools.FindDotnet();
            ManifestLoader.ValidateFiles(context.Manifest, context.Layout.Root);
        }

        /// <inheritdoc />
        public async Task RunAsync(ChannelContext context, CancellationToken token)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            string dotnet = context.Tools.FindDotnet();
            PublisherManifest manifest = context.Manifest;
            BrandingGenerator branding = new BrandingGenerator(context.Resolve(manifest.Project.Logo));
            Directory.CreateDirectory(context.Layout.BrandingDirectory);
            string icon = Path.Combine(context.Layout.BrandingDirectory, context.Layout.IconFileName);
            branding.WriteIcon(icon);
            string license = File.ReadAllText(context.Resolve(manifest.Project.LicenseFile));

            foreach (string runtime in context.Runtimes)
            {
                token.ThrowIfCancellationRequested();
                string stage = context.Layout.StageDirectory(runtime);
                context.Output.WriteLine("== publish " + runtime + " -> " + stage);
                StageFiles.Reset(stage);

                foreach (ArtifactInfo artifact in manifest.Artifacts)
                {
                    List<string> arguments = PublishArguments(context.Resolve(artifact.Project), manifest.Build, runtime, context.Version, stage);
                    int exitCode = await context.Processes.RunAsync(dotnet, arguments, context.Layout.Root, null, token).ConfigureAwait(false);
                    if (exitCode != 0)
                        throw new PublisherException("dotnet publish of " + artifact.Project + " for " + runtime + " failed with exit code " + exitCode + ".");
                }

                List<string> removed = StageFiles.RemoveExcluded(stage, manifest.Build.ExcludeFromStage);
                if (removed.Any()) context.Output.WriteLine("Removed from the stage (build.excludeFromStage): " + string.Join(", ", removed));
                StageFiles.RequireArtifacts(stage, manifest, runtime);

                bool windows = runtime.StartsWith("win-", StringComparison.Ordinal);
                foreach (ArtifactInfo artifact in manifest.Artifacts.Where(a => a.Command != null))
                {
                    if (windows) File.WriteAllText(Path.Combine(stage, artifact.Command + ".cmd"), StageFiles.CommandShim(artifact.Executable));
                }

                File.WriteAllText(Path.Combine(stage, StageFiles.LicenseFileName), windows ? StageFiles.ToCrlf(license) : license);
                File.Copy(icon, Path.Combine(stage, context.Layout.IconFileName), true);

                foreach (string file in Directory.GetFiles(stage).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    context.Output.WriteLine("  " + Path.GetFileName(file) + "  " + new FileInfo(file).Length.ToString("N0") + " bytes");
                }
            }
        }
    }
}
