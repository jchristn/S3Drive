namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Storage;
    using Test.Shared.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Offline tests for <see cref="BlobS3Store"/>: construction for each addressing mode and
    /// argument validation. None of these cases contact an endpoint; live behavior is covered by
    /// <see cref="StorageIntegrationSuite"/>.
    /// </summary>
    public static class BlobS3StoreSuite
    {
        private const string SuiteId = "BlobS3Store";

        /// <summary>
        /// An endpoint that refuses connections, used where a request must fail fast.
        /// </summary>
        private const string UnreachableEndpoint = "http://127.0.0.1:1";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TestCases.Create(SuiteId, "ConstructorGuards", "BlobS3Store rejects a null profile or secret and a missing bucket or access key", () =>
                {
                    Assert.Throws<ArgumentNullException>(() => new BlobS3Store(null!, "s"));
                    Assert.Throws<ArgumentNullException>(() => new BlobS3Store(CompatibleProfile(true), null!));

                    DriveProfile noBucket = CompatibleProfile(true);
                    noBucket.Bucket = string.Empty;
                    Assert.Throws<ArgumentException>(() => new BlobS3Store(noBucket, "s"));

                    DriveProfile noAccessKey = CompatibleProfile(true);
                    noAccessKey.AccessKey = string.Empty;
                    Assert.Throws<ArgumentException>(() => new BlobS3Store(noAccessKey, "s"));
                }),

                TestCases.Create(SuiteId, "ConstructsForEveryAddressingMode", "BlobS3Store constructs for AWS, path-style, virtual-hosted, SSL, and default-region profiles", () =>
                {
                    DriveProfile aws = new DriveProfile { Provider = S3ProviderEnum.AwsS3, Bucket = "b", AccessKey = "a", Region = "eu-west-1" };
                    DriveProfile awsNoRegion = new DriveProfile { Provider = S3ProviderEnum.AwsS3, Bucket = "b", AccessKey = "a" };
                    DriveProfile pathStyle = CompatibleProfile(true);
                    DriveProfile virtualHosted = CompatibleProfile(false);
                    DriveProfile ssl = CompatibleProfile(true);
                    ssl.ServiceUrl = "https://s3.example.com";
                    ssl.UseSsl = true;
                    DriveProfile compatibleWithoutUrl = new DriveProfile { Provider = S3ProviderEnum.S3Compatible, Bucket = "b", AccessKey = "a" };

                    foreach (DriveProfile profile in new[] { aws, awsNoRegion, pathStyle, virtualHosted, ssl, compatibleWithoutUrl })
                    {
                        using (BlobS3Store store = new BlobS3Store(profile, "secret"))
                        {
                            Assert.NotNull(store);
                        }
                    }
                }),

                TestCases.Create(SuiteId, "ConstructRejectsInvalidServiceUrl", "BlobS3Store rejects a malformed service URL for an S3-compatible profile", () =>
                {
                    DriveProfile profile = CompatibleProfile(true);
                    profile.ServiceUrl = "not a url";
                    Assert.Throws<UriFormatException>(() => new BlobS3Store(profile, "secret"));
                }),

                TestCases.Create(SuiteId, "DisposeIsIdempotent", "BlobS3Store tolerates repeated disposal", () =>
                {
                    BlobS3Store store = new BlobS3Store(CompatibleProfile(true), "secret");
                    store.Dispose();
                    store.Dispose();
                }),

                TestCases.Create(SuiteId, "MethodArgumentGuards", "BlobS3Store operations reject null keys, data, and empty paths before any request", async ct =>
                {
                    using (BlobS3Store store = new BlobS3Store(CompatibleProfile(true), "secret"))
                    {
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.ExistsAsync(null!, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.HeadAsync(null!, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.ListAsync(null!, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.ListAllKeysAsync(null!, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.GetAsync(null!, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.GetToFileAsync(null!, "x", ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentException>(() => store.GetToFileAsync("k", string.Empty, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.PutAsync(null!, Array.Empty<byte>(), ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.PutAsync("k", null!, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.PutFromFileAsync(null!, "x", ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentException>(() => store.PutFromFileAsync("k", string.Empty, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.DeleteAsync(null!, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.DeleteManyAsync(null!, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.CopyAsync(null!, "d", ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => store.CopyAsync("s", null!, ct)).ConfigureAwait(false);
                    }
                }),

                TestCases.Create(SuiteId, "DeleteManyEmptyIsNoop", "BlobS3Store.DeleteManyAsync with no keys issues no request", async ct =>
                {
                    using (BlobS3Store store = new BlobS3Store(CompatibleProfile(true), "secret"))
                    {
                        await store.DeleteManyAsync(Array.Empty<string>(), ct).ConfigureAwait(false);
                    }
                }),

                TestCases.Create(SuiteId, "ConnectivityUnreachableIsFalse", "BlobS3Store.ValidateConnectivityAsync reports false for an unreachable endpoint", async ct =>
                {
                    using (BlobS3Store store = new BlobS3Store(CompatibleProfile(true), "secret"))
                    {
                        Assert.False(await store.ValidateConnectivityAsync(ct).ConfigureAwait(false));
                    }
                }),

                TestCases.Create(SuiteId, "UnreachableOperationsFail", "BlobS3Store surfaces a failure, not an empty result, when the endpoint refuses connections", async ct =>
                {
                    using (BlobS3Store store = new BlobS3Store(CompatibleProfile(true), "secret"))
                    {
                        bool putFailed = false;
                        bool listFailed = false;
                        try
                        {
                            await store.PutAsync("k", new byte[] { 1, 2, 3 }, ct).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            putFailed = true;
                        }

                        try
                        {
                            await store.ListAsync(string.Empty, ct).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            listFailed = true;
                        }

                        Assert.True(putFailed, "put against an unreachable endpoint should fail");
                        Assert.True(listFailed, "list against an unreachable endpoint should fail");
                    }
                }),

                TestCases.Create(SuiteId, "PutFromMissingFileThrows", "BlobS3Store.PutFromFileAsync surfaces a missing source file", async ct =>
                {
                    using (BlobS3Store store = new BlobS3Store(CompatibleProfile(true), "secret"))
                    {
                        string missing = Path.Combine(Path.GetTempPath(), "s3drive-missing-" + Guid.NewGuid().ToString("N"));
                        await Assert.ThrowsAsync<FileNotFoundException>(() => store.PutFromFileAsync("k", missing, ct)).ConfigureAwait(false);
                    }
                })
            };

            return new TestSuiteDescriptor(SuiteId, "S3 store (offline)", cases);
        }

        private static DriveProfile CompatibleProfile(bool pathStyle)
        {
            return new DriveProfile
            {
                Id = "drv_offline",
                Provider = S3ProviderEnum.S3Compatible,
                ServiceUrl = UnreachableEndpoint,
                UseSsl = false,
                UsePathStyle = pathStyle,
                Region = "us-east-1",
                Bucket = "bucket",
                AccessKey = "access"
            };
        }
    }
}
