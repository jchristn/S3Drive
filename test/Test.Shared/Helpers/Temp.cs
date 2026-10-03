namespace Test.Shared.Helpers
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

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
    
        /// <summary>
        /// Runs an asynchronous body against a fresh temporary directory, deleting it afterward.
        /// </summary>
        /// <param name="body">The body, given the directory path.</param>
        /// <returns>A task that completes when the body has run and the directory is removed.</returns>
        public static async Task WithDirAsync(Func<string, Task> body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            string path = NewDir();
            try
            {
                await body(path).ConfigureAwait(false);
            }
            finally
            {
                Delete(path);
            }
        }

        /// <summary>
        /// Runs a synchronous body against a fresh temporary directory, deleting it afterward.
        /// </summary>
        /// <param name="body">The body, given the directory path.</param>
        public static void WithDir(Action<string> body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            string path = NewDir();
            try
            {
                body(path);
            }
            finally
            {
                Delete(path);
            }
        }
    }
}
