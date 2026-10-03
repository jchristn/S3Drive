namespace S3Drive.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using S3Drive.Core.Telemetry;

    /// <summary>
    /// A thread-safe, time-limited in-memory cache of directory listings and object attributes.
    /// A time-to-live of zero disables caching. Local mutations invalidate affected entries so
    /// the drive always reflects its own writes.
    /// </summary>
    public sealed class MetadataCache
    {
        private readonly object _Sync = new object();
        private readonly Dictionary<string, ListingEntry> _Listings = new Dictionary<string, ListingEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, HeadEntry> _Heads = new Dictionary<string, HeadEntry>(StringComparer.Ordinal);
        private const string KindHead = "head";
        private const string KindListing = "listing";

        private readonly long _TtlTicks;
        private readonly string _Drive;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="ttlSeconds">The time-to-live in seconds. Zero disables caching. Negative values are treated as zero.</param>
        public MetadataCache(int ttlSeconds) : this(ttlSeconds, null)
        {
        }

        /// <summary>
        /// Initializes a new instance whose telemetry is labeled with a drive letter.
        /// </summary>
        /// <param name="ttlSeconds">The time-to-live in seconds. Zero disables caching. Negative values are treated as zero.</param>
        /// <param name="driveLetter">The drive letter reported as the <c>s3drive.drive</c> telemetry label. Null or invalid values report <c>unknown</c>.</param>
        public MetadataCache(int ttlSeconds, string? driveLetter)
        {
            long seconds = ttlSeconds < 0 ? 0 : ttlSeconds;
            _TtlTicks = seconds * Stopwatch.Frequency;
            _Drive = S3DriveTelemetry.NormalizeDrive(driveLetter);
        }

        /// <summary>
        /// Whether caching is enabled (time-to-live greater than zero).
        /// </summary>
        public bool Enabled
        {
            get { return _TtlTicks > 0; }
        }

        /// <summary>
        /// Attempts to read a cached listing for a prefix.
        /// </summary>
        /// <param name="prefix">The prefix. Cannot be null.</param>
        /// <param name="entries">The cached entries when a fresh entry exists; otherwise null.</param>
        /// <returns>True on a cache hit; otherwise false.</returns>
        public bool TryGetListing(string prefix, out IReadOnlyList<S3Entry>? entries)
        {
            entries = null;
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));
            if (!Enabled)
            {
                S3DriveTelemetry.RecordCacheLookup(_Drive, KindListing, "bypass");
                return false;
            }

            lock (_Sync)
            {
                if (_Listings.TryGetValue(prefix, out ListingEntry entry) && !IsExpired(entry.Timestamp))
                {
                    entries = entry.Entries;
                    S3DriveTelemetry.RecordCacheLookup(_Drive, KindListing, "hit");
                    return true;
                }
            }

            S3DriveTelemetry.RecordCacheLookup(_Drive, KindListing, "miss");
            return false;
        }

        /// <summary>
        /// Stores a listing for a prefix.
        /// </summary>
        /// <param name="prefix">The prefix. Cannot be null.</param>
        /// <param name="entries">The entries to cache. Cannot be null.</param>
        public void SetListing(string prefix, IReadOnlyList<S3Entry> entries)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (!Enabled) return;

            int delta;
            lock (_Sync)
            {
                delta = _Listings.ContainsKey(prefix) ? 0 : 1;
                _Listings[prefix] = new ListingEntry(entries, Now());
            }

            S3DriveTelemetry.RecordCacheEntries(_Drive, KindListing, delta);
        }

        /// <summary>
        /// Attempts to read a cached object attribute entry for a key.
        /// </summary>
        /// <param name="key">The key. Cannot be null.</param>
        /// <param name="entry">The cached entry (which may itself be null to represent a known-absent object) when fresh.</param>
        /// <returns>True on a cache hit; otherwise false.</returns>
        public bool TryGetHead(string key, out S3Entry? entry)
        {
            entry = null;
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!Enabled)
            {
                S3DriveTelemetry.RecordCacheLookup(_Drive, KindHead, "bypass");
                return false;
            }

            lock (_Sync)
            {
                if (_Heads.TryGetValue(key, out HeadEntry head) && !IsExpired(head.Timestamp))
                {
                    entry = head.Entry;
                    S3DriveTelemetry.RecordCacheLookup(_Drive, KindHead, "hit");
                    return true;
                }
            }

            S3DriveTelemetry.RecordCacheLookup(_Drive, KindHead, "miss");
            return false;
        }

        /// <summary>
        /// Stores an object attribute entry for a key. A null entry caches a known-absent object.
        /// </summary>
        /// <param name="key">The key. Cannot be null.</param>
        /// <param name="entry">The entry, or null to record that the object does not exist.</param>
        public void SetHead(string key, S3Entry? entry)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!Enabled) return;

            int delta;
            lock (_Sync)
            {
                delta = _Heads.ContainsKey(key) ? 0 : 1;
                _Heads[key] = new HeadEntry(entry, Now());
            }

            S3DriveTelemetry.RecordCacheEntries(_Drive, KindHead, delta);
        }

        /// <summary>
        /// Invalidates a key and the listing of its parent prefix.
        /// </summary>
        /// <param name="key">The key. Cannot be null.</param>
        public void InvalidateKey(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            int headsRemoved = 0;
            int listingsRemoved = 0;
            lock (_Sync)
            {
                if (_Heads.Remove(key)) headsRemoved++;
                if (_Listings.Remove(ParentPrefix(key))) listingsRemoved++;
            }

            RecordRemoval(headsRemoved, listingsRemoved, "key");
        }

        /// <summary>
        /// Invalidates a prefix listing and any cached descendants.
        /// </summary>
        /// <param name="prefix">The prefix. Cannot be null.</param>
        public void InvalidatePrefix(string prefix)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));

            int headsRemoved = 0;
            int listingsRemoved = 0;
            lock (_Sync)
            {
                if (_Listings.Remove(prefix)) listingsRemoved++;
                if (_Listings.Remove(ParentPrefix(prefix.TrimEnd('/')))) listingsRemoved++;

                List<string> staleHeads = new List<string>();
                foreach (KeyValuePair<string, HeadEntry> pair in _Heads)
                {
                    if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) staleHeads.Add(pair.Key);
                }
                foreach (string stale in staleHeads)
                {
                    if (_Heads.Remove(stale)) headsRemoved++;
                }

                List<string> staleListings = new List<string>();
                foreach (KeyValuePair<string, ListingEntry> pair in _Listings)
                {
                    if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) staleListings.Add(pair.Key);
                }
                foreach (string stale in staleListings)
                {
                    if (_Listings.Remove(stale)) listingsRemoved++;
                }
            }

            RecordRemoval(headsRemoved, listingsRemoved, "prefix");
        }

        /// <summary>
        /// Clears the entire cache.
        /// </summary>
        public void Clear()
        {
            int headsRemoved;
            int listingsRemoved;
            lock (_Sync)
            {
                headsRemoved = _Heads.Count;
                listingsRemoved = _Listings.Count;
                _Listings.Clear();
                _Heads.Clear();
            }

            RecordRemoval(headsRemoved, listingsRemoved, "clear");
        }

        private void RecordRemoval(int headsRemoved, int listingsRemoved, string scope)
        {
            S3DriveTelemetry.RecordCacheInvalidation(_Drive, scope);
            S3DriveTelemetry.RecordCacheEntries(_Drive, KindHead, -headsRemoved);
            S3DriveTelemetry.RecordCacheEntries(_Drive, KindListing, -listingsRemoved);
        }

        private static long Now()
        {
            return Stopwatch.GetTimestamp();
        }

        private bool IsExpired(long timestamp)
        {
            return Now() - timestamp > _TtlTicks;
        }

        private static string ParentPrefix(string key)
        {
            string trimmed = key.TrimEnd('/');
            int slash = trimmed.LastIndexOf('/');
            if (slash < 0) return string.Empty;
            return trimmed.Substring(0, slash + 1);
        }

        private readonly struct ListingEntry
        {
            public ListingEntry(IReadOnlyList<S3Entry> entries, long timestamp)
            {
                Entries = entries;
                Timestamp = timestamp;
            }

            public IReadOnlyList<S3Entry> Entries { get; }

            public long Timestamp { get; }
        }

        private readonly struct HeadEntry
        {
            public HeadEntry(S3Entry? entry, long timestamp)
            {
                Entry = entry;
                Timestamp = timestamp;
            }

            public S3Entry? Entry { get; }

            public long Timestamp { get; }
        }
    }
}
