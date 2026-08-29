namespace Test.Automated.Harness
{
    using System;
    using System.IO;

    /// <summary>
    /// Temporary-directory helpers for tests.
    /// </summary>
    public static class Temp
    {
        /// <summary>
        /// Creates and returns a new unique temporary directory.
        /// </summary>
        /// <returns>The directory path.</returns>
        public static string NewDir()
        {
            string path = Path.Combine(Path.GetTempPath(), "s3drive-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Deletes a directory tree, ignoring errors.
        /// </summary>
        /// <param name="path">The directory to delete.</param>
        public static void Delete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception)
            {
            }
        }
    }
}
