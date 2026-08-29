namespace Test.Automated.Tests
{
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Storage;
    using Test.Automated.Harness;

    /// <summary>
    /// Integration tests for <see cref="BlobS3Store"/> against a real S3 or S3-compatible
    /// endpoint (for example Less3, MinIO, or Ceph). These are skipped unless an endpoint is
    /// configured via CLI arguments or S3DRIVE_TEST_* environment variables.
    /// </summary>
    public static class StorageIntegrationTests
    {
        private const string Prefix = "s3drive-test/";

        /// <summary>
        /// Registers the tests, or skips them when no endpoint is configured.
        /// </summary>
        /// <param name="runner">The runner.</param>
        /// <param name="config">The storage configuration.</param>
        public static void Register(TestRunner runner, StorageTestConfig? config)
        {
            if (config == null || !config.Enabled)
            {
                string reason = "no S3 endpoint configured (pass --endpoint/--access-key/--secret-key/--bucket or set S3DRIVE_TEST_*)";
                runner.Skip("Storage connectivity", reason);
                runner.Skip("Storage put/head/get/list/copy/delete", reason);
                runner.Skip("Storage file upload and download", reason);
                runner.Skip("Storage multi-object delete", reason);
                return;
            }

            runner.Add("Storage connectivity", async () =>
            {
                using (BlobS3Store store = new BlobS3Store(config.Profile, config.Secret))
                {
                    Assert.True(await store.ValidateConnectivityAsync(CancellationToken.None));
                }
            });

            runner.Add("Storage put/head/get/list/copy/delete", async () =>
            {
                using (BlobS3Store store = new BlobS3Store(config.Profile, config.Secret))
                {
                    CancellationToken token = CancellationToken.None;
                    string key = Prefix + "hello.txt";
                    string copyKey = Prefix + "copy.txt";
                    byte[] data = Encoding.UTF8.GetBytes("hello s3drive");

                    await store.PutAsync(key, data, token);
                    Assert.True(await store.ExistsAsync(key, token));

                    S3Entry? head = await store.HeadAsync(key, token);
                    Assert.NotNull(head);
                    Assert.Equal(data.Length, (int)head!.SizeBytes);

                    byte[] got = await store.GetAsync(key, token);
                    Assert.Equal("hello s3drive", Encoding.UTF8.GetString(got));

                    System.Collections.Generic.IReadOnlyList<S3Entry> listing = await store.ListAsync(Prefix, token);
                    bool found = false;
                    foreach (S3Entry entry in listing)
                    {
                        if (entry.Name == "hello.txt") found = true;
                    }

                    Assert.True(found, "listing should contain hello.txt");

                    await store.CopyAsync(key, copyKey, token);
                    Assert.True(await store.ExistsAsync(copyKey, token));

                    System.Collections.Generic.IReadOnlyList<string> all = await store.ListAllKeysAsync(Prefix, token);
                    Assert.True(all.Count >= 2, "expected at least two keys under the prefix");

                    await store.DeleteAsync(key, token);
                    Assert.False(await store.ExistsAsync(key, token));
                    await store.DeleteAsync(copyKey, token);
                }
            });

            runner.Add("Storage multi-object delete", async () =>
            {
                using (BlobS3Store store = new BlobS3Store(config.Profile, config.Secret))
                {
                    CancellationToken token = CancellationToken.None;
                    System.Collections.Generic.List<string> keys = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < 5; i++)
                    {
                        string key = Prefix + "bulk/" + i + ".txt";
                        keys.Add(key);
                        await store.PutAsync(key, Encoding.UTF8.GetBytes("bulk " + i), token);
                    }

                    // Deleting a key that no longer exists must be tolerated by the batch delete.
                    keys.Add(Prefix + "bulk/missing.txt");

                    await store.DeleteManyAsync(keys, token);

                    foreach (string key in keys)
                    {
                        Assert.False(await store.ExistsAsync(key, token), key + " should be deleted");
                    }
                }
            });

            runner.Add("Storage file upload and download", async () =>
            {
                using (BlobS3Store store = new BlobS3Store(config.Profile, config.Secret))
                {
                    CancellationToken token = CancellationToken.None;
                    string key = Prefix + "file.bin";
                    byte[] data = RandomNumberGenerator.GetBytes(2048);
                    string dir = Temp.NewDir();
                    try
                    {
                        string source = Path.Combine(dir, "source.bin");
                        await File.WriteAllBytesAsync(source, data, token);
                        await store.PutFromFileAsync(key, source, token);

                        string destination = Path.Combine(dir, "dest.bin");
                        await store.GetToFileAsync(key, destination, token);
                        byte[] roundTrip = await File.ReadAllBytesAsync(destination, token);
                        Assert.Equal(data.Length, roundTrip.Length);

                        await store.DeleteAsync(key, token);
                    }
                    finally
                    {
                        Temp.Delete(dir);
                    }
                }
            });
        }
    }
}
