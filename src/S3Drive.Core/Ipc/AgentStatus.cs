namespace S3Drive.Core.Ipc
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The agent-wide status document published to the state directory.
    /// </summary>
    public class AgentStatus
    {
        private List<DriveStatus> _Drives = new List<DriveStatus>();

        /// <summary>
        /// UTC time the status was last updated.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The agent process identifier.
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// The per-drive statuses. Never null.
        /// </summary>
        public List<DriveStatus> Drives
        {
            get { return _Drives; }
            set { _Drives = value ?? new List<DriveStatus>(); }
        }
    }
}
