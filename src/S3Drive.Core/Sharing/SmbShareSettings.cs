namespace S3Drive.Core.Sharing
{
    using System.Collections.Generic;

    /// <summary>
    /// Per-drive CIFS/SMB network-sharing settings.
    /// </summary>
    public class SmbShareSettings
    {
        private List<string> _AllowedPrincipals = new List<string>();

        /// <summary>
        /// Whether the mounted drive is re-shared over SMB. Defaults to false.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// The SMB share name advertised on the network. May be null when sharing is disabled.
        /// </summary>
        public string? ShareName { get; set; } = null;

        /// <summary>
        /// The access level granted to clients. Defaults to <see cref="ShareAccessEnum.ReadOnly"/>.
        /// </summary>
        public ShareAccessEnum Access { get; set; } = ShareAccessEnum.ReadOnly;

        /// <summary>
        /// Windows accounts or groups permitted to connect. Never null; an empty list means a
        /// conservative default is applied and access is never granted to Everyone implicitly.
        /// </summary>
        public List<string> AllowedPrincipals
        {
            get { return _AllowedPrincipals; }
            set { _AllowedPrincipals = value ?? new List<string>(); }
        }

        /// <summary>
        /// Optional human-readable description of the share. May be null.
        /// </summary>
        public string? Description { get; set; } = null;
    }
}
