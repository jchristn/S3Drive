namespace S3Drive.Agent
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Extensions.Logging;
    using Radiant;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Diagnostics;
    using S3Drive.Core.Telemetry;

    /// <summary>
    /// The agent's single telemetry host. Wraps one Radiant host subscribed to the S3Drive meter and
    /// activity source (plus the .NET HTTP client meter), exports per <see cref="TelemetrySettings"/>,
    /// and bridges the agent's log stream into the OpenTelemetry log pipeline when log export is on.
    /// Startup is best-effort: a failure degrades to running without telemetry, never a crash.
    /// </summary>
    internal sealed class AgentTelemetry : IDisposable
    {
        [ThreadStatic]
        private static bool _InBridge;

        private readonly RadiantHost? _Host;
        private readonly ILogger? _Logger;
        private bool _Disposed;

        private AgentTelemetry(RadiantHost? host, ILogger? logger)
        {
            _Host = host;
            _Logger = logger;
            if (_Logger != null) S3DriveLog.MessageLogged += OnMessageLogged;
        }

        /// <summary>
        /// Whether a live telemetry pipeline is running.
        /// </summary>
        public bool IsActive
        {
            get { return _Host != null && _Host.IsEnabled; }
        }

        /// <summary>
        /// Starts telemetry from settings. Never throws.
        /// </summary>
        /// <param name="settings">The telemetry settings.</param>
        /// <returns>The telemetry host; inert when telemetry is disabled or could not start.</returns>
        public static AgentTelemetry Start(TelemetrySettings settings)
        {
            S3DriveTelemetry.IncludeObjectKeys = settings.IncludeObjectKeys;

            if (!settings.Enabled)
            {
                S3DriveLog.Info("Telemetry disabled by configuration.");
                return new AgentTelemetry(null, null);
            }

            bool lokiEnabled = settings.ExportLogs && settings.LokiEnabled;
            if (!settings.OtlpEnabled && !settings.PrometheusEnabled && !lokiEnabled)
            {
                S3DriveLog.Info("Telemetry: no exporter enabled (OTLP, Prometheus, Loki all off); metrics and spans are emitted but not exported.");
                return new AgentTelemetry(null, null);
            }

            RadiantHost? host = TryStart(settings, settings.PrometheusEnabled);
            if (host == null && settings.PrometheusEnabled && (settings.OtlpEnabled || lokiEnabled))
            {
                S3DriveLog.Warn("Telemetry: retrying without the Prometheus endpoint.");
                host = TryStart(settings, false);
            }

            if (host == null) return new AgentTelemetry(null, null);

            ILogger? logger = null;
            if (settings.ExportLogs)
            {
                try
                {
                    logger = host.CreateLogger("S3Drive.Agent");
                }
                catch (Exception ex)
                {
                    S3DriveLog.Warn("Telemetry: log export unavailable: " + ex.Message);
                }
            }

            List<string> exporters = new List<string>();
            if (settings.OtlpEnabled) exporters.Add("OTLP " + settings.OtlpProtocol + " -> " + settings.OtlpEndpoint);
            if (settings.PrometheusEnabled) exporters.Add("Prometheus http://" + settings.PrometheusHostname + ":" + settings.PrometheusPort + "/metrics");
            if (lokiEnabled) exporters.Add("Loki -> " + settings.LokiEndpoint);
            S3DriveLog.Info("Telemetry enabled (" + settings.ServiceName + "): " + string.Join("; ", exporters));

            return new AgentTelemetry(host, logger);
        }

        /// <summary>
        /// Flushes exporters and releases the telemetry host (including the Prometheus port).
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            if (_Logger != null) S3DriveLog.MessageLogged -= OnMessageLogged;

            try
            {
                _Host?.Dispose();
            }
            catch (Exception ex)
            {
                S3DriveLog.Warn("Telemetry: shutdown did not complete cleanly: " + ex.Message);
            }
        }

        private static RadiantHost? TryStart(TelemetrySettings settings, bool withPrometheus)
        {
            try
            {
                return RadiantHost.Start(BuildSettings(settings, withPrometheus));
            }
            catch (Exception ex)
            {
                S3DriveLog.Warn("Telemetry: startup failed (" + ex.GetType().Name + ": " + ex.Message + ")" + (withPrometheus ? "" : "; continuing without telemetry"));
                return null;
            }
        }

        private static RadiantSettings BuildSettings(TelemetrySettings settings, bool withPrometheus)
        {
            RadiantSettings radiant = new RadiantSettings(settings.ServiceName);
            radiant.Enable = true;

            radiant.Sources.AddMeter(TelemetryNames.MeterName);
            radiant.Sources.AddActivitySource(TelemetryNames.ActivitySourceName);
            radiant.Sources.AddMeter(TelemetryNames.HttpClientMeterName);

            radiant.Metrics.Enable = true;
            radiant.Metrics.IncludeRuntime = true;
            radiant.Metrics.IncludeProcess = true;
            radiant.Metrics.ExportIntervalMs = settings.MetricsExportIntervalMs;
            radiant.Metrics.LabelPolicy = LabelPolicyEnum.Lenient;
            radiant.Metrics.DefineAll(ToConventions());

            radiant.Traces.Enable = true;
            radiant.Traces.SamplingRatio = settings.SamplingRatio;
            radiant.Traces.PropagateContext = true;

            // Export Information and above: Debug lines (every metadata lookup) stay in the local
            // log file rather than flooding Loki.
            radiant.Logs.Enable = settings.ExportLogs;
            radiant.Logs.IncludeTraceCorrelation = true;
            radiant.Logs.MinimumSeverity = 2;

            radiant.Otlp.Enable = settings.OtlpEnabled;
            radiant.Otlp.Endpoint = settings.OtlpEndpoint;
            radiant.Otlp.Protocol = string.Equals(settings.OtlpProtocol, "httpprotobuf", StringComparison.OrdinalIgnoreCase)
                ? OtlpProtocolEnum.HttpProtobuf
                : OtlpProtocolEnum.Grpc;

            radiant.Prometheus.Enable = withPrometheus;
            radiant.Prometheus.Hostname = settings.PrometheusHostname;
            radiant.Prometheus.Port = settings.PrometheusPort;

            radiant.Loki.Enable = settings.ExportLogs && settings.LokiEnabled;
            radiant.Loki.Endpoint = settings.LokiEndpoint;
            radiant.Loki.MinimumSeverity = 2;

            radiant.DiagnosticCallback = message => S3DriveLog.Debug("[telemetry] " + message);
            return radiant;
        }

        private static List<Convention> ToConventions()
        {
            List<Convention> conventions = new List<Convention>();
            foreach (MetricDescriptor descriptor in S3DriveMetricCatalog.All)
            {
                string[] labels = new List<string>(descriptor.LabelKeys).ToArray();
                switch (descriptor.Kind)
                {
                    case S3Drive.Core.Telemetry.MetricKindEnum.Counter:
                        conventions.Add(Convention.Counter(descriptor.Name, descriptor.Unit, labels));
                        break;
                    case S3Drive.Core.Telemetry.MetricKindEnum.UpDownCounter:
                        conventions.Add(Convention.UpDownCounter(descriptor.Name, descriptor.Unit, labels));
                        break;
                    case S3Drive.Core.Telemetry.MetricKindEnum.Histogram:
                        conventions.Add(Convention.Histogram(descriptor.Name, descriptor.Unit, descriptor.Buckets, labels));
                        break;
                    case S3Drive.Core.Telemetry.MetricKindEnum.Gauge:
                        conventions.Add(Convention.Gauge(descriptor.Name, descriptor.Unit, labels));
                        break;
                }
            }

            return conventions;
        }

        private void OnMessageLogged(string severity, string message)
        {
            ILogger? logger = _Logger;
            if (logger == null || _InBridge) return;

            _InBridge = true;
            try
            {
                logger.Log(ToLevel(severity), new EventId(0), message, null, (state, exception) => state);
            }
            catch (Exception)
            {
                // Log export is best-effort and must never affect the caller.
            }
            finally
            {
                _InBridge = false;
            }
        }

        private static LogLevel ToLevel(string severity)
        {
            switch (severity)
            {
                case "Debug":
                    return LogLevel.Debug;
                case "Info":
                    return LogLevel.Information;
                case "Warn":
                    return LogLevel.Warning;
                case "Error":
                    return LogLevel.Error;
                case "Alert":
                case "Critical":
                case "Emergency":
                    return LogLevel.Critical;
                default:
                    return LogLevel.Information;
            }
        }
    }
}
