namespace S3Drive.Core.Ipc
{
    /// <summary>
    /// A command written by the TUI into the agent's command channel and executed by the agent.
    /// </summary>
    public class AgentCommand
    {
        /// <summary>
        /// The command type.
        /// </summary>
        public AgentCommandTypeEnum CommandType { get; set; }

        /// <summary>
        /// The target drive identifier for drive-scoped commands (Mount, Unmount, Share,
        /// Unshare). Null for global commands (MountAll, UnmountAll, Reload).
        /// </summary>
        public string? DriveId { get; set; }
    }
}
