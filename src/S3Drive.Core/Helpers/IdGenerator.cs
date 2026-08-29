namespace S3Drive.Core.Helpers
{
    using System;

    /// <summary>
    /// Generates prefixed, K-sortable identifiers for S3Drive entities using PrettyId.
    /// </summary>
    public static class IdGenerator
    {
        private static readonly PrettyId.IdGenerator _Generator = new PrettyId.IdGenerator();
        private static int _IdLength = 24;

        /// <summary>
        /// The identifier body length excluding the prefix. Minimum 16, maximum 64, default 24.
        /// </summary>
        public static int IdLength
        {
            get { return _IdLength; }
            set { _IdLength = Math.Clamp(value, 16, 64); }
        }

        /// <summary>
        /// Generates a new drive-profile identifier prefixed with "drv_".
        /// </summary>
        /// <returns>A K-sortable identifier. Never null.</returns>
        public static string GenerateDriveId()
        {
            return _Generator.GenerateKSortable(Constants.DriveIdPrefix, _IdLength);
        }
    }
}
