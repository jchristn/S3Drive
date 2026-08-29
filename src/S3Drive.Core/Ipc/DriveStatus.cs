namespace S3Drive.Core.Ipc
{
    /// <summary>
    /// The published runtime status of a single drive.
    /// </summary>
    public class DriveStatus
    {
        private string _DriveId = string.Empty;
        private string _Name = string.Empty;

        /// <summary>
        /// The drive identifier. Never null.
        /// </summary>
        public string DriveId
        {
            get { return _DriveId; }
            set { _DriveId = value ?? string.Empty; }
        }

        /// <summary>
        /// The drive name. Never null.
        /// </summary>
        public string Name
        {
            get { return _Name; }
            set { _Name = value ?? string.Empty; }
        }

        /// <summary>
        /// The mount state.
        /// </summary>
        public DriveMountStateEnum MountState { get; set; } = DriveMountStateEnum.Unmounted;

        /// <summary>
        /// The drive letter, when mounted. May be null.
        /// </summary>
        public string? DriveLetter { get; set; }

        /// <summary>
        /// The most recent error message for this drive when the last operation failed. May be null.
        /// </summary>
        public string? LastError { get; set; }
    }
}
