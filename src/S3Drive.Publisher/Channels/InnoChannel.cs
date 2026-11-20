namespace S3Drive.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Publisher.Imaging;
    using S3Drive.Publisher.Inno;
    using S3Drive.Publisher.Manifest;

    /// <summary>
    /// Builds the Inno Setup installer (&lt;app&gt;-&lt;version&gt;-&lt;rid&gt;-setup.exe) for each
    /// Windows runtime from its staging directory. Windows only, since ISCC runs only there.
    /// </summary>
    public class InnoChannel : IChannel
    {
        /// <summary>
        /// The Inno Setup sign tool name used when Windows signing is configured.
        /// </summary>
        public const string SignToolName = "publisher";

        private static readonly Regex _EnvironmentReference = new Regex("%([A-Za-z_][A-Za-z0-9_]*)%", RegexOptions.CultureInvariant);

        /// <inheritdoc />
        public string Name
        {
            get { return ChannelNames.Inno; }
        }

        /// <inheritdoc />
        public string Description
        {
            get { return "Inno Setup installer <app>-<version>-<rid>-setup.exe (Windows only)"; }
        }

        /// <inheritdoc />
        public bool IsSupportedOnCurrentOS
        {
            get { return OperatingSystem.IsWindows(); }
        }

        /// <inheritdoc />
        public void Preflight(ChannelContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.Tools.FindIscc();
            foreach (string runtime in context.Runtimes)
            {
                InnoScriptBuilder.ArchitectureFor(runtime);
                StageFiles.RequireArtifacts(context.Layout.StageDirectory(runtime), context.Manifest, runtime);
            }
        }

        /// <inheritdoc />
        public async Task RunAsync(ChannelContext context, CancellationToken token)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            string iscc = context.Tools.FindIscc();
            PublisherManifest manifest = context.Manifest;
            BrandingGenerator branding = new BrandingGenerator(context.Resolve(manifest.Project.Logo));
            WizardImageSet images = branding.WriteWizardImages(context.Layout.BrandingDirectory, manifest.Windows.WizardBackground);
            Directory.CreateDirectory(context.Layout.InstallersDirectory);

            SigningTarget signing = manifest.Signing.Windows;
            List<string> secrets = new List<string>();
            string? signCommand = null;
            if (signing.IsConfigured)
            {
                foreach (Match match in _EnvironmentReference.Matches(signing.Command))
                {
                    string name = match.Groups[1].Value;
                    string? value = Environment.GetEnvironmentVariable(name);
                    if (string.IsNullOrEmpty(value))
                        throw new PublisherException("signing.windows.command uses %" + name + "%, but that environment variable is not set.");
                    secrets.Add(value);
                }

                signCommand = Environment.ExpandEnvironmentVariables(signing.Command);
            }

            foreach (string runtime in context.Runtimes)
            {
                token.ThrowIfCancellationRequested();
                string stage = context.Layout.StageDirectory(runtime);
                InnoScriptInput input = new InnoScriptInput
                {
                    Manifest = manifest,
                    Version = context.Version,
                    Runtime = runtime,
                    StageDirectory = stage,
                    OutputDirectory = context.Layout.InstallersDirectory,
                    OutputBaseFilename = context.Layout.SetupBaseName(runtime),
                    LicenseFile = Path.Combine(stage, StageFiles.LicenseFileName),
                    SetupIconFile = Path.Combine(stage, context.Layout.IconFileName),
                    InstalledIconFileName = context.Layout.IconFileName,
                    Images = images,
                    SignToolName = signCommand != null ? SignToolName : null
                };

                string script = context.Layout.InnoScriptFile(runtime);
                Directory.CreateDirectory(Path.GetDirectoryName(script)!);
                await File.WriteAllTextAsync(script, InnoScriptBuilder.Build(input), new UTF8Encoding(true), token).ConfigureAwait(false);
                context.Output.WriteLine("== inno " + runtime + " -> " + script);

                List<string> arguments = new List<string> { "/Qp" };
                if (signCommand != null) arguments.Add("/S" + SignToolName + "=" + signCommand);
                arguments.Add(script);

                int exitCode = await context.Processes.RunAsync(iscc, arguments, context.Layout.Root, secrets, token).ConfigureAwait(false);
                if (exitCode != 0) throw new PublisherException("Inno Setup failed for " + runtime + " with exit code " + exitCode + "; the generated script is " + script + ".");

                string setup = Path.Combine(context.Layout.InstallersDirectory, context.Layout.SetupBaseName(runtime) + ".exe");
                if (!File.Exists(setup)) throw new PublisherException("Inno Setup reported success but " + setup + " was not written.");
                context.Output.WriteLine("  " + setup + "  " + new FileInfo(setup).Length.ToString("N0") + " bytes");
            }
        }
    }
}
