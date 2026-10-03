namespace Test.Shared.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Globalization;
    using S3Drive.Core.Telemetry;

    /// <summary>
    /// An in-memory telemetry listener for tests. Subscribes to the S3Drive meter and activity
    /// source through the base class library only (MeterListener and ActivityListener), the same
    /// way any host would, and records every measurement and every stopped span.
    /// </summary>
    public sealed class TelemetryCapture : IDisposable
    {
        private readonly object _Sync = new object();
        private readonly List<CapturedMeasurement> _Measurements = new List<CapturedMeasurement>();
        private readonly List<Activity> _Spans = new List<Activity>();
        private readonly Dictionary<string, string?> _Published = new Dictionary<string, string?>(StringComparer.Ordinal);
        private readonly MeterListener _Meters;
        private readonly ActivityListener _Activities;
        private bool _Disposed;

        /// <summary>
        /// Starts capturing.
        /// </summary>
        public TelemetryCapture()
        {
            _Meters = new MeterListener();
            _Meters.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name != TelemetryNames.MeterName) return;
                lock (_Sync)
                {
                    _Published[instrument.Name] = instrument.Unit;
                }

                listener.EnableMeasurementEvents(instrument);
            };
            _Meters.SetMeasurementEventCallback<long>((instrument, value, tags, state) => Add(instrument, value, tags));
            _Meters.SetMeasurementEventCallback<int>((instrument, value, tags, state) => Add(instrument, value, tags));
            _Meters.SetMeasurementEventCallback<double>((instrument, value, tags, state) => Add(instrument, value, tags));
            _Meters.Start();

            _Activities = new ActivityListener();
            _Activities.ShouldListenTo = source => source.Name == TelemetryNames.ActivitySourceName;
            _Activities.Sample = SampleAll;
            _Activities.ActivityStopped = activity =>
            {
                lock (_Sync)
                {
                    _Spans.Add(activity);
                }
            };
            ActivitySource.AddActivityListener(_Activities);
        }

        /// <summary>
        /// Instrument names (and units) published on the S3Drive meter.
        /// </summary>
        public IReadOnlyDictionary<string, string?> Published
        {
            get
            {
                lock (_Sync)
                {
                    return new Dictionary<string, string?>(_Published, StringComparer.Ordinal);
                }
            }
        }

        /// <summary>
        /// Every captured measurement.
        /// </summary>
        public IReadOnlyList<CapturedMeasurement> All
        {
            get
            {
                lock (_Sync)
                {
                    return new List<CapturedMeasurement>(_Measurements);
                }
            }
        }

        /// <summary>
        /// Every captured (stopped) span.
        /// </summary>
        public IReadOnlyList<Activity> AllSpans
        {
            get
            {
                lock (_Sync)
                {
                    return new List<Activity>(_Spans);
                }
            }
        }

        /// <summary>
        /// Polls observable gauges so their current values are captured.
        /// </summary>
        public void CollectObservables()
        {
            _Meters.RecordObservableInstruments();
        }

        /// <summary>
        /// Captured measurements for one instrument that match every <c>key=value</c> pair.
        /// </summary>
        /// <param name="name">The instrument name.</param>
        /// <param name="pairs">Required tags, each <c>key=value</c>.</param>
        /// <returns>The matching measurements.</returns>
        public IReadOnlyList<CapturedMeasurement> Find(string name, params string[] pairs)
        {
            List<CapturedMeasurement> result = new List<CapturedMeasurement>();
            foreach (CapturedMeasurement measurement in All)
            {
                if (measurement.Name == name && measurement.Matches(pairs)) result.Add(measurement);
            }

            return result;
        }

        /// <summary>
        /// The sum of matching measurement values.
        /// </summary>
        /// <param name="name">The instrument name.</param>
        /// <param name="pairs">Required tags, each <c>key=value</c>.</param>
        /// <returns>The sum.</returns>
        public double Sum(string name, params string[] pairs)
        {
            double total = 0;
            foreach (CapturedMeasurement measurement in Find(name, pairs)) total += measurement.Value;
            return total;
        }

        /// <summary>
        /// Captured spans with a given name that carry every <c>key=value</c> tag.
        /// </summary>
        /// <param name="name">The span (display) name.</param>
        /// <param name="pairs">Required tags, each <c>key=value</c>.</param>
        /// <returns>The matching spans.</returns>
        public IReadOnlyList<Activity> Spans(string name, params string[] pairs)
        {
            List<Activity> result = new List<Activity>();
            foreach (Activity activity in AllSpans)
            {
                if (activity.DisplayName != name) continue;
                bool ok = true;
                foreach (string pair in pairs)
                {
                    int eq = pair.IndexOf('=');
                    object? value = activity.GetTagItem(pair.Substring(0, eq));
                    if (!string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), pair.Substring(eq + 1), StringComparison.Ordinal)) ok = false;
                }

                if (ok) result.Add(activity);
            }

            return result;
        }

        /// <summary>
        /// Stops capturing.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Meters.Dispose();
            _Activities.Dispose();
        }

        private void Add<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags) where T : struct
        {
            Dictionary<string, string?> copy = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                copy[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture);
            }

            double numeric = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            lock (_Sync)
            {
                _Measurements.Add(new CapturedMeasurement(instrument.Name, numeric, copy));
            }
        }

        private static ActivitySamplingResult SampleAll(ref ActivityCreationOptions<ActivityContext> options)
        {
            return ActivitySamplingResult.AllDataAndRecorded;
        }
    }
}
