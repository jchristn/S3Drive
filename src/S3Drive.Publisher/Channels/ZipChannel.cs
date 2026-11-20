namespace S3Drive.Publisher.Channels
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Archives each Windows runtime's staging directory as the portable .zip, for scripts, CI jobs,
    /// and anyone who will not run an installer. Runs on any OS.
    /// </summary>
    public class ZipChannel : IChannel
    {
        /// <inheritdoc />
        public string Name
        {
            get { return ChannelNames.Zip; }
        }

        /// <inheritdoc />
        public string Description
        {
            get { return "Portable <app>-<version>-<rid>.zip of each Windows staging directory"; }
        }

        /// <inheritdoc />
        public bool IsSupportedOnCurrentOS
        {
            get { return true; }
        }

        /// <inheritdoc />
        public void Preflight(ChannelContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            foreach (string runtime in context.Runtimes)
            {
                StageFiles.RequireArtifacts(context.Layout.StageDirectory(runtime), context.Manifest, runtime);
            }
        }

        /// <inheritdoc />
        public Task RunAsync(ChannelContext context, CancellationToken token)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            Directory.CreateDirectory(context.Layout.InstallersDirectory);
            foreach (string runtime in context.Runtimes)
            {
                token.ThrowIfCancellationRequested();
                if (!runtime.StartsWith("win-", StringComparison.Ordinal))
                {
                    context.Output.WriteLine("== zip " + runtime + ": skipped (the .zip is the Windows portable format)");
                    continue;
                }

                string zip = Path.Combine(context.Layout.InstallersDirectory, context.Layout.ZipFileName(runtime));
                context.Output.WriteLine("== zip " + runtime + " -> " + zip);
                if (File.Exists(zip)) File.Delete(zip);
                ZipFile.CreateFromDirectory(context.Layout.StageDirectory(runtime), zip, CompressionLevel.Optimal, false);
                context.Output.WriteLine("  " + new FileInfo(zip).Length.ToString("N0") + " bytes");
            }

            return Task.CompletedTask;
        }
    }
}
