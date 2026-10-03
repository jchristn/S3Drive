namespace S3Drive.Core.Telemetry
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Describes one metric instrument S3Drive emits: its name, kind, unit, description, the
    /// complete set of label keys it may carry, and (for histograms) its bucket boundaries. A host
    /// can translate the catalog into its own registration format, for example Radiant conventions.
    /// </summary>
    public sealed class MetricDescriptor
    {
        /// <summary>
        /// Initializes a new descriptor.
        /// </summary>
        /// <param name="name">The instrument name. Cannot be null or empty.</param>
        /// <param name="kind">The instrument kind.</param>
        /// <param name="unit">The UCUM unit. Cannot be null or empty.</param>
        /// <param name="description">A human-readable description. Cannot be null or empty.</param>
        /// <param name="labelKeys">The allowed label keys. Cannot be null; may be empty.</param>
        /// <param name="buckets">Histogram bucket boundaries in the instrument's unit, or null for non-histograms.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="name"/>, <paramref name="unit"/>, or <paramref name="description"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="labelKeys"/> is null.</exception>
        public MetricDescriptor(string name, MetricKindEnum kind, string unit, string description, IReadOnlyList<string> labelKeys, double[]? buckets)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Metric name must be provided.", nameof(name));
            if (string.IsNullOrEmpty(unit)) throw new ArgumentException("Metric unit must be provided for " + name + ".", nameof(unit));
            if (string.IsNullOrEmpty(description)) throw new ArgumentException("Metric description must be provided for " + name + ".", nameof(description));
            if (labelKeys == null) throw new ArgumentNullException(nameof(labelKeys));

            Name = name;
            Kind = kind;
            Unit = unit;
            Description = description;
            LabelKeys = labelKeys;
            Buckets = buckets;
        }

        /// <summary>
        /// The instrument name. Never null or empty.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The instrument kind.
        /// </summary>
        public MetricKindEnum Kind { get; }

        /// <summary>
        /// The UCUM unit (for example <c>s</c>, <c>By</c>, or <c>{request}</c>). Never null or empty.
        /// </summary>
        public string Unit { get; }

        /// <summary>
        /// A human-readable description. Never null or empty.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// The complete set of label keys the instrument may carry. Never null.
        /// </summary>
        public IReadOnlyList<string> LabelKeys { get; }

        /// <summary>
        /// Histogram bucket boundaries, in the instrument's unit. Null for non-histogram instruments.
        /// </summary>
        public double[]? Buckets { get; }
    }
}
