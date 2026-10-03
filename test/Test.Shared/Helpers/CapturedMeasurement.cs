namespace Test.Shared.Helpers
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One metric measurement captured by <see cref="TelemetryCapture"/>.
    /// </summary>
    public sealed class CapturedMeasurement
    {
        /// <summary>
        /// Initializes a new captured measurement.
        /// </summary>
        /// <param name="name">The instrument name.</param>
        /// <param name="value">The measured value.</param>
        /// <param name="tags">The measurement tags, as strings.</param>
        public CapturedMeasurement(string name, double value, IReadOnlyDictionary<string, string?> tags)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Value = value;
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
        }

        /// <summary>
        /// The instrument name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The measured value.
        /// </summary>
        public double Value { get; }

        /// <summary>
        /// The measurement tags.
        /// </summary>
        public IReadOnlyDictionary<string, string?> Tags { get; }

        /// <summary>
        /// Reads a tag value.
        /// </summary>
        /// <param name="key">The tag key.</param>
        /// <returns>The value, or null when absent.</returns>
        public string? Tag(string key)
        {
            return Tags.TryGetValue(key, out string? value) ? value : null;
        }

        /// <summary>
        /// Whether every supplied <c>key=value</c> pair is present on the measurement.
        /// </summary>
        /// <param name="pairs">The pairs, each formatted <c>key=value</c>.</param>
        /// <returns>True when all match.</returns>
        public bool Matches(params string[] pairs)
        {
            foreach (string pair in pairs)
            {
                int eq = pair.IndexOf('=');
                string key = pair.Substring(0, eq);
                string value = pair.Substring(eq + 1);
                if (!string.Equals(Tag(key), value, StringComparison.Ordinal)) return false;
            }

            return true;
        }
    }
}
