namespace S3Drive.Core.Telemetry
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// One timed, instrumented unit of work: an optional span plus a duration histogram and an
    /// optional counter recorded once, on completion, with a bounded outcome label. Every member is
    /// best-effort and never throws into the caller. Disposing a scope that was never completed
    /// records it as an error, so an unexpected exception path is never silently dropped.
    /// </summary>
    internal sealed class TelemetryScope : IDisposable
    {
        private readonly Activity? _Activity;
        private readonly Histogram<double>? _Duration;
        private readonly Counter<long>? _Counter;
        private readonly TagList _Tags;
        private readonly bool _ErrorTypeLabel;
        private readonly long _StartTimestamp;
        private string? _ErrorType;
        private bool _Completed;
        private bool _Disposed;

        internal TelemetryScope(Activity? activity, Histogram<double>? duration, Counter<long>? counter, TagList tags, bool errorTypeLabel)
        {
            _Activity = activity;
            _Duration = duration;
            _Counter = counter;
            _Tags = tags;
            _ErrorTypeLabel = errorTypeLabel;
            _StartTimestamp = Stopwatch.GetTimestamp();
        }

        internal Activity? Activity
        {
            get { return _Activity; }
        }

        internal bool Completed
        {
            get { return _Completed; }
        }

        internal void SetTag(string key, object? value)
        {
            if (_Activity == null) return;
            try
            {
                _Activity.SetTag(key, value);
            }
            catch (Exception)
            {
            }
        }

        internal void SetObjectKey(string key)
        {
            if (_Activity == null || !S3DriveTelemetry.IncludeObjectKeys) return;
            SetTag(TelemetryNames.AttrObjectKey, key);
        }

        internal void RecordException(Exception exception)
        {
            if (exception == null) return;
            _ErrorType = S3DriveTelemetry.ErrorType(exception);
            S3DriveTelemetry.AddException(_Activity, exception);
        }

        internal void SetErrorType(string errorType)
        {
            if (string.IsNullOrEmpty(errorType)) return;
            if (_ErrorType == null) _ErrorType = errorType;
        }

        internal void Fail(Exception exception)
        {
            RecordException(exception);
            Complete(S3DriveTelemetry.OutcomeFor(exception));
        }

        internal void Complete(string outcome)
        {
            if (_Completed) return;
            _Completed = true;

            try
            {
                double seconds = Stopwatch.GetElapsedTime(_StartTimestamp).TotalSeconds;
                bool failed = !string.Equals(outcome, TelemetryNames.OutcomeSuccess, StringComparison.Ordinal);

                TagList tags = _Tags;
                tags.Add(TelemetryNames.AttrOutcome, outcome);
                if (_ErrorTypeLabel && failed && _ErrorType != null) tags.Add(TelemetryNames.AttrErrorType, _ErrorType);

                _Duration?.Record(seconds, tags);
                _Counter?.Add(1, tags);

                if (_Activity != null)
                {
                    _Activity.SetTag(TelemetryNames.AttrOutcome, outcome);
                    if (failed && _ErrorType != null) _Activity.SetTag(TelemetryNames.AttrErrorType, _ErrorType);

                    if (string.Equals(outcome, TelemetryNames.OutcomeError, StringComparison.Ordinal))
                    {
                        _Activity.SetStatus(ActivityStatusCode.Error, _ErrorType ?? outcome);
                    }
                    else if (!string.Equals(outcome, TelemetryNames.OutcomeCancelled, StringComparison.Ordinal))
                    {
                        _Activity.SetStatus(ActivityStatusCode.Ok);
                    }
                }
            }
            catch (Exception)
            {
                // Instrumentation is best-effort and must never affect the operation.
            }
        }

        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            if (!_Completed) Complete(TelemetryNames.OutcomeError);

            try
            {
                _Activity?.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }
}
