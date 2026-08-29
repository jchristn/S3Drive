namespace S3Drive.Core.Ipc
{
    /// <summary>
    /// The kind of command the TUI issues to the tray agent through the command channel.
    /// </summary>
    public enum AgentCommandTypeEnum
    {
        /// <summary>
        /// Mount a specific drive.
        /// </summary>
        Mount,

        /// <summary>
        /// Unmount a specific drive.
        /// </summary>
        Unmount,

        /// <summary>
        /// Mount every configured drive.
        /// </summary>
        MountAll,

        /// <summary>
        /// Unmount every mounted drive.
        /// </summary>
        UnmountAll,

        /// <summary>
        /// Create the network share for a specific drive.
        /// </summary>
        Share,

        /// <summary>
        /// Remove the network share for a specific drive.
        /// </summary>
        Unshare,

        /// <summary>
        /// Reload configuration from disk.
        /// </summary>
        Reload
    }
}
