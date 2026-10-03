namespace S3Drive.Core.Configuration
{
    using System;

    /// <summary>
    /// Telemetry (metrics, traces, and logs) settings for the S3Drive agent, modeled on the fleet's
    /// standard telemetry settings. The agent always emits on the <c>S3Drive</c> meter and activity
    /// source; these settings decide whether and where that telemetry is exported. Every exporter is
    /// off by default because a desktop install usually has no collector; enable OTLP (push to an
    /// OpenTelemetry Collector or Tempo) or the in-process Prometheus endpoint to observe the agent.
    /// Changes take effect when the agent restarts.
    /// </summary>
    public class TelemetrySettings
    {
        private string _ServiceName = "s3drive-agent";
        private string _OtlpEndpoint = "http://127.0.0.1:4317";
        private string _OtlpProtocol = "grpc";
        private string _PrometheusHostname = "127.0.0.1";
        private int _PrometheusPort = 9464;
        private string _LokiEndpoint = "http://127.0.0.1:3100/otlp";
        private double _SamplingRatio = 1.0;
        private int _MetricsExportIntervalMs = 15000;

        /// <summary>
        /// Master switch. When false the agent builds no telemetry pipeline and binds no ports;
        /// emission stays a no-op. Defaults to true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// The service name reported as <c>service.name</c> on every metric, span, and log record.
        /// Defaults to <c>s3drive-agent</c>. Null or empty resets to the default.
        /// </summary>
        public string ServiceName
        {
            get { return _ServiceName; }
            set { _ServiceName = string.IsNullOrWhiteSpace(value) ? "s3drive-agent" : value.Trim(); }
        }

        /// <summary>
        /// Whether metrics, traces, and (when <see cref="ExportLogs"/> is true) logs are pushed over
        /// OTLP to <see cref="OtlpEndpoint"/>. Defaults to false.
        /// </summary>
        public bool OtlpEnabled { get; set; } = false;

        /// <summary>
        /// The OTLP collector endpoint. Use the gRPC port (4317) with the <c>grpc</c> protocol or the
        /// HTTP port (4318) with <c>httpprotobuf</c>. Defaults to <c>http://127.0.0.1:4317</c>.
        /// Null or empty resets to the default.
        /// </summary>
        public string OtlpEndpoint
        {
            get { return _OtlpEndpoint; }
            set { _OtlpEndpoint = string.IsNullOrWhiteSpace(value) ? "http://127.0.0.1:4317" : value.Trim(); }
        }

        /// <summary>
        /// The OTLP protocol: <c>grpc</c> (default) or <c>httpprotobuf</c>. Any other value is
        /// treated as <c>grpc</c>.
        /// </summary>
        public string OtlpProtocol
        {
            get { return _OtlpProtocol; }
            set
            {
                _OtlpProtocol = string.Equals(value?.Trim(), "httpprotobuf", StringComparison.OrdinalIgnoreCase) ? "httpprotobuf" : "grpc";
            }
        }

        /// <summary>
        /// Whether the agent serves an in-process Prometheus scrape endpoint at
        /// <c>http://{PrometheusHostname}:{PrometheusPort}/metrics</c>. Defaults to false.
        /// </summary>
        public bool PrometheusEnabled { get; set; } = false;

        /// <summary>
        /// The hostname the Prometheus endpoint binds. Defaults to <c>127.0.0.1</c> (loopback only).
        /// On Windows, binding anything other than <c>localhost</c> as a standard user requires an
        /// HTTP URL reservation (<c>netsh http add urlacl</c>). Null or empty resets to the default.
        /// </summary>
        public string PrometheusHostname
        {
            get { return _PrometheusHostname; }
            set { _PrometheusHostname = string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim(); }
        }

        /// <summary>
        /// The Prometheus endpoint port. Minimum 1, maximum 65535. Defaults to 9464.
        /// </summary>
        public int PrometheusPort
        {
            get { return _PrometheusPort; }
            set { _PrometheusPort = Math.Clamp(value, 1, 65535); }
        }

        /// <summary>
        /// Whether the agent's log stream is exported (over OTLP, and directly to Loki when
        /// <see cref="LokiEnabled"/> is true) with trace correlation. Log lines contain file and
        /// folder paths from the mounted buckets, so this is opt-in. Defaults to false.
        /// </summary>
        public bool ExportLogs { get; set; } = false;

        /// <summary>
        /// Whether logs are pushed directly to a Loki 3.x OTLP endpoint (no collector in the path).
        /// Requires <see cref="ExportLogs"/>. Defaults to false.
        /// </summary>
        public bool LokiEnabled { get; set; } = false;

        /// <summary>
        /// The Loki OTLP base endpoint. Defaults to <c>http://127.0.0.1:3100/otlp</c>. Null or empty
        /// resets to the default.
        /// </summary>
        public string LokiEndpoint
        {
            get { return _LokiEndpoint; }
            set { _LokiEndpoint = string.IsNullOrWhiteSpace(value) ? "http://127.0.0.1:3100/otlp" : value.Trim(); }
        }

        /// <summary>
        /// The head-based trace sampling ratio. Minimum 0.0, maximum 1.0. Defaults to 1.0 (every
        /// trace). Lower it on a busy drive; metrics are unaffected by sampling.
        /// </summary>
        public double SamplingRatio
        {
            get { return _SamplingRatio; }
            set { _SamplingRatio = double.IsNaN(value) ? 1.0 : Math.Clamp(value, 0.0, 1.0); }
        }

        /// <summary>
        /// The OTLP metric export interval in milliseconds. Minimum 1000, maximum 300000. Defaults to 15000.
        /// </summary>
        public int MetricsExportIntervalMs
        {
            get { return _MetricsExportIntervalMs; }
            set { _MetricsExportIntervalMs = Math.Clamp(value, 1000, 300000); }
        }

        /// <summary>
        /// Whether spans carry object keys and prefixes (<c>s3drive.object.key</c>). Object keys are
        /// file and folder names from the bucket, so they are omitted by default. Metrics never carry
        /// object keys. Defaults to false.
        /// </summary>
        public bool IncludeObjectKeys { get; set; } = false;
    }
}
