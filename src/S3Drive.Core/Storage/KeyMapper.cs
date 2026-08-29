namespace S3Drive.Core.Storage
{
    /// <summary>
    /// Converts between Windows-style drive paths (as Dokan provides them) and S3 object keys or
    /// key prefixes. The drive root maps to the empty key/prefix, backslashes become forward
    /// slashes, and directory prefixes end with a single forward slash.
    /// </summary>
    public static class KeyMapper
    {
        /// <summary>
        /// Converts a drive path such as "\a\b\file.txt" to an object key such as "a/b/file.txt".
        /// The root path ("\" or empty) maps to the empty string.
        /// </summary>
        /// <param name="path">The drive path. Null is treated as the root.</param>
        /// <returns>The object key. Never null.</returns>
        public static string ToObjectKey(string? path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path.Replace('\\', '/').TrimStart('/');
        }

        /// <summary>
        /// Converts a drive path to a key prefix ending in "/". The root maps to the empty string,
        /// which lists the bucket root.
        /// </summary>
        /// <param name="path">The drive path. Null is treated as the root.</param>
        /// <returns>The prefix, either empty or ending with "/". Never null.</returns>
        public static string ToPrefix(string? path)
        {
            string key = ToObjectKey(path);
            if (key.Length == 0) return string.Empty;
            if (key.EndsWith('/')) return key;
            return key + "/";
        }

        /// <summary>
        /// Converts an object key such as "a/b/file.txt" to a drive path such as "\a\b\file.txt".
        /// </summary>
        /// <param name="key">The object key. Null is treated as the empty key.</param>
        /// <returns>The drive path. Never null; the empty key maps to "\".</returns>
        public static string ToPath(string? key)
        {
            if (string.IsNullOrEmpty(key)) return "\\";
            return "\\" + key.Trim('/').Replace('/', '\\');
        }

        /// <summary>
        /// Returns the final path segment (file or folder name) of a drive path.
        /// </summary>
        /// <param name="path">The drive path. Null or root returns the empty string.</param>
        /// <returns>The last segment, or empty for the root.</returns>
        public static string GetName(string? path)
        {
            string key = ToObjectKey(path).TrimEnd('/');
            if (key.Length == 0) return string.Empty;

            int index = key.LastIndexOf('/');
            if (index < 0) return key;
            return key.Substring(index + 1);
        }

        /// <summary>
        /// Returns the parent drive path of the given path, or "\" for a top-level entry.
        /// </summary>
        /// <param name="path">The drive path. Null is treated as the root.</param>
        /// <returns>The parent path. Never null.</returns>
        public static string GetParentPath(string? path)
        {
            string key = ToObjectKey(path).TrimEnd('/');
            int index = key.LastIndexOf('/');
            if (index < 0) return "\\";
            return ToPath(key.Substring(0, index));
        }
    }
}
