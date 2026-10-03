namespace Constellation.Controller
{
    using System;

    /// <summary>
    /// Telemetry export settings consumed by the hosting application (for example Constellation.ControllerServer,
    /// which starts a Radiant host from these values).  The controller library itself always emits through the BCL
    /// <c>Constellation</c> meter and activity source and does not read these settings; they exist so that one
    /// <c>constellation.json</c> configures the whole process.  Watson's own HTTP telemetry is configured separately
    /// under <c>Webserver.Telemetry</c>.
    /// Loopback defaults use <c>127.0.0.1</c> rather than <c>localhost</c>.
    /// Thread safety: not thread safe; configure before starting the host.
    /// </summary>
    public class TelemetrySettings
    {
        /// <summary>
        /// Master switch for exporting telemetry from the host.  Default true.  When false the host starts no
        /// exporter and every Constellation and Watson measurement remains a near-free no-op.
        /// </summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// Service name stamped as the <c>service.name</c> resource attribute.  Default <c>constellation-controller</c>.
        /// Must be non-empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string ServiceName
        {
            get => _ServiceName;
            set => _ServiceName = (!String.IsNullOrEmpty(value) ? value : throw new ArgumentNullException(nameof(ServiceName)));
        }

        /// <summary>
        /// Whether to push metrics, traces, and logs over OTLP.  Default true.
        /// </summary>
        public bool OtlpEnable { get; set; } = true;

        /// <summary>
        /// OTLP endpoint (an OpenTelemetry Collector, Tempo, or any OTLP backend).  Default <c>http://127.0.0.1:4317</c>.
        /// Use the gRPC port (4317) with the <c>grpc</c> protocol, or the HTTP port (4318) with <c>httpprotobuf</c>.
        /// Must be an absolute URI.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the value is not an absolute URI.</exception>
        public string OtlpEndpoint
        {
            get => _OtlpEndpoint;
            set
            {
                if (String.IsNullOrEmpty(value) || !Uri.TryCreate(value, UriKind.Absolute, out _))
                    throw new ArgumentException("OtlpEndpoint must be an absolute URI.", nameof(OtlpEndpoint));
                _OtlpEndpoint = value;
            }
        }

        /// <summary>
        /// OTLP protocol: <c>grpc</c> (default) or <c>httpprotobuf</c>.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown for any other value.</exception>
        public string OtlpProtocol
        {
            get => _OtlpProtocol;
            set
            {
                if (!String.Equals(value, "grpc", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(value, "httpprotobuf", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("OtlpProtocol must be 'grpc' or 'httpprotobuf'.", nameof(OtlpProtocol));
                _OtlpProtocol = value.ToLowerInvariant();
            }
        }

        /// <summary>
        /// Whether to serve an in-process Prometheus scrape endpoint.  Default true.  The endpoint is anonymous; keep
        /// it on an internal interface.
        /// </summary>
        public bool PrometheusEnable { get; set; } = true;

        /// <summary>
        /// Hostname the Prometheus scrape endpoint binds to.  Default <c>127.0.0.1</c>.  Inside a container, set this
        /// to the container's own hostname (for example <c>constellation-controller</c>) so the endpoint binds the
        /// address other containers reach.  Wildcards (<c>*</c>, <c>+</c>) and <c>0.0.0.0</c> are rejected by the
        /// underlying OpenTelemetry Prometheus listener.  Must be non-empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string PrometheusHostname
        {
            get => _PrometheusHostname;
            set => _PrometheusHostname = (!String.IsNullOrEmpty(value) ? value : throw new ArgumentNullException(nameof(PrometheusHostname)));
        }

        /// <summary>
        /// Port the Prometheus scrape endpoint binds to.  Default 9464.  Minimum 1, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside 1..65535.</exception>
        public int PrometheusPort
        {
            get => _PrometheusPort;
            set => _PrometheusPort = (value >= 1 && value <= 65535 ? value : throw new ArgumentOutOfRangeException(nameof(PrometheusPort)));
        }

        /// <summary>
        /// Whether to export logs directly to Loki over OTLP/HTTP (bypassing a collector).  Default false.
        /// </summary>
        public bool LokiEnable { get; set; } = false;

        /// <summary>
        /// Loki OTLP base endpoint.  Default <c>http://127.0.0.1:3100/otlp</c>.  Must be an absolute URI.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the value is not an absolute URI.</exception>
        public string LokiEndpoint
        {
            get => _LokiEndpoint;
            set
            {
                if (String.IsNullOrEmpty(value) || !Uri.TryCreate(value, UriKind.Absolute, out _))
                    throw new ArgumentException("LokiEndpoint must be an absolute URI.", nameof(LokiEndpoint));
                _LokiEndpoint = value;
            }
        }

        /// <summary>
        /// Loki tenant identifier sent as <c>X-Scope-OrgID</c>.  Default null (single-tenant Loki).
        /// </summary>
        public string LokiTenantId { get; set; } = null;

        /// <summary>
        /// Minimum severity of structured logs to export, 0 (trace) through 7 (none).  Default 2 (information).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside 0..7.</exception>
        public int LogsMinimumSeverity
        {
            get => _LogsMinimumSeverity;
            set => _LogsMinimumSeverity = (value >= 0 && value <= 7 ? value : throw new ArgumentOutOfRangeException(nameof(LogsMinimumSeverity)));
        }

        /// <summary>
        /// Head-based trace sampling ratio, 0.0 (sample nothing) through 1.0 (sample everything).  Default 1.0.
        /// Parent-based, so a sampled inbound trace keeps its children sampled.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside 0.0..1.0.</exception>
        public double TraceSamplingRatio
        {
            get => _TraceSamplingRatio;
            set => _TraceSamplingRatio = (value >= 0.0 && value <= 1.0 ? value : throw new ArgumentOutOfRangeException(nameof(TraceSamplingRatio)));
        }

        /// <summary>
        /// OTLP metric export interval in milliseconds.  Default 15000.  Minimum 1000, maximum 300000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside 1000..300000.</exception>
        public int MetricExportIntervalMs
        {
            get => _MetricExportIntervalMs;
            set => _MetricExportIntervalMs = (value >= 1000 && value <= 300000 ? value : throw new ArgumentOutOfRangeException(nameof(MetricExportIntervalMs)));
        }

        /// <summary>
        /// Whether to include .NET runtime and process metrics (GC, heap, threads, working set).  Default true.
        /// </summary>
        public bool IncludeRuntimeMetrics { get; set; } = true;

        private string _ServiceName = "constellation-controller";
        private string _OtlpEndpoint = "http://127.0.0.1:4317";
        private string _OtlpProtocol = "grpc";
        private string _PrometheusHostname = "127.0.0.1";
        private int _PrometheusPort = 9464;
        private string _LokiEndpoint = "http://127.0.0.1:3100/otlp";
        private int _LogsMinimumSeverity = 2;
        private double _TraceSamplingRatio = 1.0;
        private int _MetricExportIntervalMs = 15000;

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public TelemetrySettings()
        {

        }
    }
}
