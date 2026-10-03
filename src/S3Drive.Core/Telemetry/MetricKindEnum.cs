namespace S3Drive.Core.Telemetry
{
    /// <summary>
    /// The kind of a metric instrument in the S3Drive metric catalog.
    /// </summary>
    public enum MetricKindEnum
    {
        /// <summary>
        /// A monotonic counter.
        /// </summary>
        Counter,

        /// <summary>
        /// A counter that can rise and fall (an in-use or size value maintained by deltas).
        /// </summary>
        UpDownCounter,

        /// <summary>
        /// A histogram of measurements (durations, in seconds).
        /// </summary>
        Histogram,

        /// <summary>
        /// An observable gauge read from state at collection time.
        /// </summary>
        Gauge
    }
}
