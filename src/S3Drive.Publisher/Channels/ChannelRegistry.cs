namespace S3Drive.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The channels the Publisher can run, in run order. Thread-safe: immutable after construction.
    /// </summary>
    public class ChannelRegistry
    {
        private readonly List<IChannel> _Channels = new List<IChannel>
        {
            new PublishChannel(),
            new ZipChannel(),
            new InnoChannel(),
            new ChecksumsChannel()
        };

        /// <summary>
        /// Every channel, in run order.
        /// </summary>
        public IReadOnlyList<IChannel> All
        {
            get { return _Channels; }
        }

        /// <summary>
        /// Finds a channel by name.
        /// </summary>
        /// <param name="name">The channel name.</param>
        /// <returns>The channel.</returns>
        /// <exception cref="PublisherException">Thrown when no channel has that name.</exception>
        public IChannel Find(string name)
        {
            IChannel? channel = _Channels.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
            if (channel == null) throw new PublisherException("Unknown channel '" + name + "'. Known channels: " + string.Join(", ", _Channels.Select(c => c.Name)) + ".");
            return channel;
        }
    }
}
