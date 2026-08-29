namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using DokanNet;
    using DokanNet.Logging;
    using S3Drive.Core.Concurrency;
    using S3Drive.Core.FileSystem;
    using S3Drive.Core.Storage;
    using Test.Automated.Harness;

    /// <summary>
    /// A real end-to-end mount test: mounts a bucket to a drive letter via the installed Dokan
    /// driver, performs operating-system-level file operations against it, and unmounts. Requires
    /// Windows, the Dokany driver, and a configured endpoint.
    /// </summary>
    internal static class MountHarness
    {
        /// <summary>
        /// Runs the mount test.
        /// </summary>
        /// <param name="config">The storage configuration.</param>
        /// <param name="driveLetterArg">An optional drive letter override.</param>
        /// <returns>Zero on success; one on failure.</returns>
        public static async Task<int> RunAsync(StorageTestConfig config, string? driveLetterArg)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.WriteLine("Mount test requires Windows.");
                return 1;
            }

            if (!config.Enabled)
            {
                Console.WriteLine("Mount test requires an endpoint (pass --endpoint/--access-key/--secret-key/--bucket).");
                return 1;
            }

            string letter = ResolveDriveLetter(driveLetterArg);
            string mountPoint = letter + ":\\";
            string staging = Temp.NewDir();
            int failures = 0;

            Console.WriteLine("Mounting bucket '" + config.Profile.Bucket + "' at " + mountPoint + " via Dokan...");

            BlobS3Store store = new BlobS3Store(config.Profile, config.Secret);
            CancellationTokenSource cts = new CancellationTokenSource();
            S3DriveFileSystem fileSystem = new S3DriveFileSystem(store, new MetadataCache(2), new ObjectLocks(), staging, "S3DriveTest", cts.Token);

            Dokan dokan = new Dokan(new NullLogger());
            DokanInstanceBuilder builder = new DokanInstanceBuilder(dokan)
                .ConfigureOptions(options =>
                {
                    options.MountPoint = mountPoint;
                    options.Options = DokanOptions.EnableNetworkUnmount;
                });
            DokanInstance instance = builder.Build(fileSystem);

            try
            {
                bool ready = false;
                for (int i = 0; i < 60; i++)
                {
                    if (Directory.Exists(mountPoint))
                    {
                        ready = true;
                        break;
                    }

                    await Task.Delay(250).ConfigureAwait(false);
                }

                failures += Check("drive appears at " + mountPoint, ready);
                if (!ready)
                {
                    return 1;
                }

                string directory = Path.Combine(mountPoint, "s3drive-mount-test");
                string file = Path.Combine(directory, "hello.txt");
                string content = "hello from dokan " + Guid.NewGuid().ToString("N");

                Directory.CreateDirectory(directory);
                failures += Check("directory create", Directory.Exists(directory));

                await File.WriteAllTextAsync(file, content).ConfigureAwait(false);
                failures += Check("file write", File.Exists(file));

                string readBack = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                failures += Check("file read round-trip", readBack == content);

                string[] entries = Directory.GetFiles(directory);
                failures += Check("directory listing shows the file", entries.Length == 1);

                long length = new FileInfo(file).Length;
                failures += Check("file length matches", length == content.Length);

                string renamed = Path.Combine(directory, "renamed.txt");
                File.Move(file, renamed);
                failures += Check("file rename", File.Exists(renamed) && !File.Exists(file));

                File.Delete(renamed);
                failures += Check("file delete", !File.Exists(renamed));

                Directory.Delete(directory);
                failures += Check("directory delete", !Directory.Exists(directory));
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL  exception during file operations: " + ex.Message);
                failures++;
            }
            finally
            {
                Console.WriteLine("Unmounting...");
                cts.Cancel();
                try
                {
                    instance.Dispose();
                    dokan.Dispose();
                }
                catch (Exception)
                {
                }

                store.Dispose();
                cts.Dispose();
                Temp.Delete(staging);
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "Mount test: ALL PASSED" : "Mount test: " + failures + " check(s) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static int Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
            return ok ? 0 : 1;
        }

        private static string ResolveDriveLetter(string? arg)
        {
            if (!string.IsNullOrEmpty(arg))
            {
                string cleaned = arg.TrimEnd(':', '\\', ' ');
                if (cleaned.Length > 0) return char.ToUpperInvariant(cleaned[0]).ToString();
            }

            HashSet<char> used = new HashSet<char>();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.Name.Length > 0) used.Add(char.ToUpperInvariant(drive.Name[0]));
            }

            for (char c = 'Z'; c >= 'F'; c--)
            {
                if (!used.Contains(c)) return c.ToString();
            }

            return "X";
        }
    }
}
