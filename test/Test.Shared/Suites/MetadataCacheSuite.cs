namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using S3Drive.Core.Storage;
    using Test.Shared.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Tests for <see cref="MetadataCache"/>: TTL behavior, negative caching, and invalidation.
    /// </summary>
    public static class MetadataCacheSuite
    {
        private const string SuiteId = "MetadataCache";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TestCases.Create(SuiteId, "DisabledAtZeroTtl", "MetadataCache is disabled at TTL 0 and never returns hits", () =>
                {
                    MetadataCache cache = new MetadataCache(0);
                    Assert.False(cache.Enabled);
                    cache.SetHead("k", new S3Entry());
                    Assert.False(cache.TryGetHead("k", out _));
                    cache.SetListing("p/", new List<S3Entry>());
                    Assert.False(cache.TryGetListing("p/", out _));
                }),

                TestCases.Create(SuiteId, "NegativeTtlDisables", "MetadataCache treats a negative TTL as disabled", () =>
                {
                    MetadataCache cache = new MetadataCache(-10);
                    Assert.False(cache.Enabled);
                    cache.SetHead("k", new S3Entry());
                    Assert.False(cache.TryGetHead("k", out _));
                }),

                TestCases.Create(SuiteId, "HeadHit", "MetadataCache returns a cached HEAD entry", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    Assert.True(cache.Enabled);
                    cache.SetHead("k", new S3Entry { Key = "k" });
                    Assert.True(cache.TryGetHead("k", out S3Entry? got));
                    Assert.NotNull(got);
                    Assert.Equal("k", got!.Key);
                }),

                TestCases.Create(SuiteId, "HeadMiss", "MetadataCache reports a miss for a key never cached", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    Assert.False(cache.TryGetHead("never", out S3Entry? got));
                    Assert.Null(got);
                    Assert.False(cache.TryGetListing("never/", out IReadOnlyList<S3Entry>? listing));
                    Assert.Null(listing);
                }),

                TestCases.Create(SuiteId, "CachesKnownAbsent", "MetadataCache caches a known-absent HEAD result", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    cache.SetHead("k", null);
                    Assert.True(cache.TryGetHead("k", out S3Entry? got));
                    Assert.Null(got);
                }),

                TestCases.Create(SuiteId, "ListingHit", "MetadataCache returns a cached listing", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    List<S3Entry> list = new List<S3Entry> { new S3Entry { Name = "a" } };
                    cache.SetListing("p/", list);
                    Assert.True(cache.TryGetListing("p/", out IReadOnlyList<S3Entry>? got));
                    Assert.NotNull(got);
                    Assert.Equal(1, got!.Count);
                }),

                TestCases.Create(SuiteId, "InvalidateKeyClearsHeadAndParentListing", "MetadataCache.InvalidateKey clears the HEAD and the parent listing", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    cache.SetHead("a/b", new S3Entry());
                    cache.SetListing("a/", new List<S3Entry>());
                    cache.InvalidateKey("a/b");
                    Assert.False(cache.TryGetHead("a/b", out _));
                    Assert.False(cache.TryGetListing("a/", out _));
                }),

                TestCases.Create(SuiteId, "InvalidateKeyAtRootClearsRootListing", "MetadataCache.InvalidateKey on a root-level key clears the root listing", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    cache.SetHead("top.txt", new S3Entry());
                    cache.SetListing(string.Empty, new List<S3Entry>());
                    cache.InvalidateKey("top.txt");
                    Assert.False(cache.TryGetListing(string.Empty, out _));
                }),

                TestCases.Create(SuiteId, "InvalidateKeyLeavesUnrelatedEntries", "MetadataCache.InvalidateKey does not evict unrelated keys or listings", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    cache.SetHead("a/b", new S3Entry());
                    cache.SetHead("c/d", new S3Entry());
                    cache.SetListing("c/", new List<S3Entry>());
                    cache.InvalidateKey("a/b");
                    Assert.True(cache.TryGetHead("c/d", out _), "sibling HEAD should survive");
                    Assert.True(cache.TryGetListing("c/", out _), "sibling listing should survive");
                }),

                TestCases.Create(SuiteId, "InvalidatePrefixClearsDescendants", "MetadataCache.InvalidatePrefix clears descendant HEADs and listings", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    cache.SetHead("p/x", new S3Entry());
                    cache.SetHead("p/sub/y", new S3Entry());
                    cache.SetListing("p/", new List<S3Entry>());
                    cache.SetListing("p/sub/", new List<S3Entry>());
                    cache.InvalidatePrefix("p/");
                    Assert.False(cache.TryGetHead("p/x", out _));
                    Assert.False(cache.TryGetHead("p/sub/y", out _));
                    Assert.False(cache.TryGetListing("p/", out _));
                    Assert.False(cache.TryGetListing("p/sub/", out _));
                }),

                TestCases.Create(SuiteId, "InvalidatePrefixClearsParentListing", "MetadataCache.InvalidatePrefix clears the listing that contains the folder", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    cache.SetListing("a/", new List<S3Entry>());
                    cache.InvalidatePrefix("a/b/");
                    Assert.False(cache.TryGetListing("a/", out _));
                }),

                TestCases.Create(SuiteId, "InvalidatePrefixLeavesSiblings", "MetadataCache.InvalidatePrefix keeps entries under sibling prefixes", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    cache.SetHead("p/x", new S3Entry());
                    cache.SetHead("q/x", new S3Entry());
                    cache.SetHead("px", new S3Entry());
                    cache.InvalidatePrefix("p/");
                    Assert.True(cache.TryGetHead("q/x", out _), "q/x should survive");
                    Assert.True(cache.TryGetHead("px", out _), "px is not under p/ and should survive");
                }),

                TestCases.Create(SuiteId, "Clear", "MetadataCache.Clear evicts HEADs and listings", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    cache.SetHead("k", new S3Entry());
                    cache.SetListing("p/", new List<S3Entry>());
                    cache.Clear();
                    Assert.False(cache.TryGetHead("k", out _));
                    Assert.False(cache.TryGetListing("p/", out _));
                }),

                TestCases.Create(SuiteId, "SetOverwrites", "MetadataCache replaces an existing entry for the same key", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    cache.SetHead("k", new S3Entry { SizeBytes = 1 });
                    cache.SetHead("k", new S3Entry { SizeBytes = 2 });
                    Assert.True(cache.TryGetHead("k", out S3Entry? got));
                    Assert.Equal(2L, got!.SizeBytes);
                }),

                TestCases.Create(SuiteId, "NullArgumentsThrow", "MetadataCache rejects null keys, prefixes, and listings", () =>
                {
                    MetadataCache cache = new MetadataCache(60);
                    Assert.Throws<ArgumentNullException>(() => cache.SetHead(null!, null));
                    Assert.Throws<ArgumentNullException>(() => cache.TryGetHead(null!, out _));
                    Assert.Throws<ArgumentNullException>(() => cache.SetListing(null!, new List<S3Entry>()));
                    Assert.Throws<ArgumentNullException>(() => cache.SetListing("p/", null!));
                    Assert.Throws<ArgumentNullException>(() => cache.TryGetListing(null!, out _));
                    Assert.Throws<ArgumentNullException>(() => cache.InvalidateKey(null!));
                    Assert.Throws<ArgumentNullException>(() => cache.InvalidatePrefix(null!));
                }),

                TestCases.Create(SuiteId, "NullArgumentsThrowWhenDisabled", "MetadataCache validates arguments even when disabled", () =>
                {
                    MetadataCache cache = new MetadataCache(0);
                    Assert.Throws<ArgumentNullException>(() => cache.SetHead(null!, null));
                    Assert.Throws<ArgumentNullException>(() => cache.TryGetListing(null!, out _));
                }),

                TestCases.Create(SuiteId, "EntriesExpire", "MetadataCache entries expire after the TTL", async ct =>
                {
                    MetadataCache cache = new MetadataCache(1);
                    cache.SetHead("k", new S3Entry());
                    cache.SetListing("p/", new List<S3Entry>());
                    Assert.True(cache.TryGetHead("k", out _));
                    Assert.True(cache.TryGetListing("p/", out _));
                    await Task.Delay(1200, ct).ConfigureAwait(false);
                    Assert.False(cache.TryGetHead("k", out _));
                    Assert.False(cache.TryGetListing("p/", out _));
                })
            };

            return new TestSuiteDescriptor(SuiteId, "Metadata cache", cases);
        }
    }
}
