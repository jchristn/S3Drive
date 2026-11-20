namespace S3Drive.Publisher
{
    using System.Collections.Generic;

    /// <summary>
    /// The channel names the Publisher understands, in the order a full build runs them.
    /// </summary>
    public static class ChannelNames
    {
        /// <summary>
        /// Self-contained single-file publish of every artifact into the per-RID staging directory.
        /// </summary>
        public const string Publish = "publish";

        /// <summary>
        /// The portable .zip of the staging directory (Windows RIDs).
        /// </summary>
        public const string Zip = "zip";

        /// <summary>
        /// The Inno Setup .exe installer (Windows only).
        /// </summary>
        public const string Inno = "inno";

        /// <summary>
        /// The SHA256SUMS file over everything in the version's installers directory.
        /// </summary>
        public const string Checksums = "checksums";

        /// <summary>
        /// Every channel, in run order.
        /// </summary>
        public static IReadOnlyList<string> All
        {
            get { return new List<string> { Publish, Zip, Inno, Checksums }; }
        }
    }
}
