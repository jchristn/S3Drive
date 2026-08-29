namespace S3Drive.Core.Ipc
{
    /// <summary>
    /// Runtime mount state of a drive, as published by the agent.
    /// </summary>
    public enum DriveMountStateEnum
    {
        /// <summary>
        /// Not mounted.
        /// </summary>
        Unmounted,

        /// <summary>
        /// Mount in progress.
        /// </summary>
        Mounting,

        /// <summary>
        /// Mounted and available.
        /// </summary>
        Mounted,

        /// <summary>
        /// Unmount in progress.
        /// </summary>
        Unmounting,

        /// <summary>
        /// The last mount attempt failed.
        /// </summary>
        Failed
    }
}
