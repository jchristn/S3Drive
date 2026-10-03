namespace S3Drive.Core.Ipc
{
    using System;

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

        /// <summary>
        /// UTC time the command was written. Set by <see cref="CommandChannel.SendAsync"/> when
        /// null; used to measure how long the command waited before the agent picked it up. May be null.
        /// </summary>
        public DateTime? CreatedUtc { get; set; }

        /// <summary>
        /// The sender's W3C <c>traceparent</c>, stamped by <see cref="CommandChannel.SendAsync"/>
        /// when the sender has an active trace, so the agent's processing joins the same trace.
        /// May be null.
        /// </summary>
        public string? TraceParent { get; set; }
    }
}
