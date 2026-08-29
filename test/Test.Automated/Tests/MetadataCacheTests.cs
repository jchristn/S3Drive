namespace Test.Automated.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using S3Drive.Core.Storage;
    using Test.Automated.Harness;

    /// <summary>
    /// Tests for <see cref="MetadataCache"/>.
    /// </summary>
    public static class MetadataCacheTests
    {
        /// <summary>
        /// Registers the tests.
        /// </summary>
        /// <param name="runner">The runner.</param>
        public static void Register(TestRunner runner)
        {
            runner.Add("MetadataCache disabled at ttl 0", () =>
            {
                MetadataCache cache = new MetadataCache(0);
                Assert.False(cache.Enabled);
                cache.SetHead("k", new S3Entry());
                Assert.False(cache.TryGetHead("k", out _));
            });

            runner.Add("MetadataCache head hit", () =>
            {
                MetadataCache cache = new MetadataCache(60);
                cache.SetHead("k", new S3Entry { Key = "k" });
                Assert.True(cache.TryGetHead("k", out S3Entry? got));
                Assert.NotNull(got);
                Assert.Equal("k", got!.Key);
            });

            runner.Add("MetadataCache caches known-absent", () =>
            {
                MetadataCache cache = new MetadataCache(60);
                cache.SetHead("k", null);
                Assert.True(cache.TryGetHead("k", out S3Entry? got));
                Assert.Null(got);
            });

            runner.Add("MetadataCache listing hit", () =>
            {
                MetadataCache cache = new MetadataCache(60);
                List<S3Entry> list = new List<S3Entry> { new S3Entry { Name = "a" } };
                cache.SetListing("p/", list);
                Assert.True(cache.TryGetListing("p/", out IReadOnlyList<S3Entry>? got));
                Assert.NotNull(got);
                Assert.Equal(1, got!.Count);
            });

            runner.Add("MetadataCache invalidateKey clears head and parent listing", () =>
            {
                MetadataCache cache = new MetadataCache(60);
                cache.SetHead("a/b", new S3Entry());
                cache.SetListing("a/", new List<S3Entry>());
                cache.InvalidateKey("a/b");
                Assert.False(cache.TryGetHead("a/b", out _));
                Assert.False(cache.TryGetListing("a/", out _));
            });

            runner.Add("MetadataCache invalidatePrefix clears descendants", () =>
            {
                MetadataCache cache = new MetadataCache(60);
                cache.SetHead("p/x", new S3Entry());
                cache.SetListing("p/", new List<S3Entry>());
                cache.InvalidatePrefix("p/");
                Assert.False(cache.TryGetHead("p/x", out _));
                Assert.False(cache.TryGetListing("p/", out _));
            });

            runner.Add("MetadataCache clear", () =>
            {
                MetadataCache cache = new MetadataCache(60);
                cache.SetHead("k", new S3Entry());
                cache.Clear();
                Assert.False(cache.TryGetHead("k", out _));
            });

            runner.Add("MetadataCache null key throws", () =>
            {
                MetadataCache cache = new MetadataCache(60);
                Assert.Throws<ArgumentNullException>(() => cache.SetHead(null!, null));
            });

            runner.Add("MetadataCache entries expire", async () =>
            {
                MetadataCache cache = new MetadataCache(1);
                cache.SetHead("k", new S3Entry());
                Assert.True(cache.TryGetHead("k", out _));
                await Task.Delay(1200);
                Assert.False(cache.TryGetHead("k", out _));
            });
        }
    }
}
