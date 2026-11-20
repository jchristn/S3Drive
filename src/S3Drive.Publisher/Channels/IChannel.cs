namespace S3Drive.Publisher.Channels
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// One packaging step the Publisher runs with --channel.
    /// </summary>
    public interface IChannel
    {
        /// <summary>
        /// The name used with --channel and in publisher.json.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A one-line description for --list-channels.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Whether this channel can run on the current operating system.
        /// </summary>
        bool IsSupportedOnCurrentOS { get; }

        /// <summary>
        /// Checks that every tool and input the channel needs is present, before doing any work.
        /// </summary>
        /// <param name="context">The run context.</param>
        /// <exception cref="PublisherException">Thrown with the fix (often an install command) when something is missing.</exception>
        void Preflight(ChannelContext context);

        /// <summary>
        /// Runs the channel for every runtime in the context.
        /// </summary>
        /// <param name="context">The run context.</param>
        /// <param name="token">Cancels the run.</param>
        /// <returns>A task that completes when the channel's outputs are written.</returns>
        /// <exception cref="PublisherException">Thrown when packaging fails.</exception>
        Task RunAsync(ChannelContext context, CancellationToken token);
    }
}
