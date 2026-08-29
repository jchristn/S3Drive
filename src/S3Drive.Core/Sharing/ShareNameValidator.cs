namespace S3Drive.Core.Sharing
{
    /// <summary>
    /// Validates Windows SMB share names.
    /// </summary>
    public static class ShareNameValidator
    {
        /// <summary>
        /// The maximum share-name length.
        /// </summary>
        public const int MaxLength = 80;

        private static readonly char[] _InvalidCharacters = new char[]
        {
            '\\', '/', '[', ']', ':', '|', '<', '>', '+', '=', ';', ',', '*', '?', '"'
        };

        /// <summary>
        /// Determines whether a share name is valid: non-empty, no more than <see cref="MaxLength"/>
        /// characters, containing no control or reserved characters.
        /// </summary>
        /// <param name="name">The candidate share name. May be null.</param>
        /// <returns>True when the name is valid; otherwise false.</returns>
        public static bool IsValid(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.Length > MaxLength) return false;

            foreach (char c in name)
            {
                if (char.IsControl(c)) return false;
                foreach (char invalid in _InvalidCharacters)
                {
                    if (c == invalid) return false;
                }
            }

            return true;
        }
    }
}
