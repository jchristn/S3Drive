namespace S3Drive.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

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
        private readonly long _TtlTicks;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="ttlSeconds">The time-to-live in seconds. Zero disables caching. Negative values are treated as zero.</param>
        public MetadataCache(int ttlSeconds)
        {
            long seconds = ttlSeconds < 0 ? 0 : ttlSeconds;
            _TtlTicks = seconds * Stopwatch.Frequency;
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
            if (!Enabled) return false;

            lock (_Sync)
            {
                if (_Listings.TryGetValue(prefix, out ListingEntry entry) && !IsExpired(entry.Timestamp))
                {
                    entries = entry.Entries;
                    return true;
                }
            }

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

            lock (_Sync)
            {
                _Listings[prefix] = new ListingEntry(entries, Now());
            }
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
            if (!Enabled) return false;

            lock (_Sync)
            {
                if (_Heads.TryGetValue(key, out HeadEntry head) && !IsExpired(head.Timestamp))
                {
                    entry = head.Entry;
                    return true;
                }
            }

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

            lock (_Sync)
            {
                _Heads[key] = new HeadEntry(entry, Now());
            }
        }

        /// <summary>
        /// Invalidates a key and the listing of its parent prefix.
        /// </summary>
        /// <param name="key">The key. Cannot be null.</param>
        public void InvalidateKey(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            lock (_Sync)
            {
                _Heads.Remove(key);
                _Listings.Remove(ParentPrefix(key));
            }
        }

        /// <summary>
        /// Invalidates a prefix listing and any cached descendants.
        /// </summary>
        /// <param name="prefix">The prefix. Cannot be null.</param>
        public void InvalidatePrefix(string prefix)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));

            lock (_Sync)
            {
                _Listings.Remove(prefix);
                _Listings.Remove(ParentPrefix(prefix.TrimEnd('/')));

                List<string> staleHeads = new List<string>();
                foreach (KeyValuePair<string, HeadEntry> pair in _Heads)
                {
                    if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) staleHeads.Add(pair.Key);
                }
                foreach (string stale in staleHeads) _Heads.Remove(stale);

                List<string> staleListings = new List<string>();
                foreach (KeyValuePair<string, ListingEntry> pair in _Listings)
                {
                    if (pair.Key.StartsWith(prefix, StringComparison.Ordinal)) staleListings.Add(pair.Key);
                }
                foreach (string stale in staleListings) _Listings.Remove(stale);
            }
        }

        /// <summary>
        /// Clears the entire cache.
        /// </summary>
        public void Clear()
        {
            lock (_Sync)
            {
                _Listings.Clear();
                _Heads.Clear();
            }
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
