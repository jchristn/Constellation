namespace Constellation.ControllerServer
{
    using System;
    using Constellation.Controller;
    using Constellation.Core.Telemetry;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using Radiant;
    using SyslogLogging;

    using ILogger = Microsoft.Extensions.Logging.ILogger;
    using TelemetrySettings = Constellation.Controller.TelemetrySettings;

    /// <summary>
    /// The process's single Radiant host.  Subscribes to the Constellation and Watson meters and activity sources and
    /// exports metrics, traces, and logs per <see cref="TelemetrySettings"/>.  Best-effort: if the host cannot start,
    /// a warning is logged and the server runs without exporting telemetry.
    /// </summary>
    internal sealed class TelemetryHost : IDisposable
    {
        private readonly RadiantHost _Host = null;
        private readonly LoggingModule _Logging;
        private bool _Disposed = false;

        internal TelemetryHost(Settings settings, LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            TelemetrySettings telemetry = settings.Telemetry;
            if (telemetry == null || !telemetry.Enable)
            {
                _Logging.Info("[Telemetry] export disabled by configuration");
                return;
            }

            try
            {
                RadiantSettings radiant = new RadiantSettings(telemetry.ServiceName);

                radiant.Sources.AddMeter(TelemetryConstants.MeterName);
                radiant.Sources.AddActivitySource(TelemetryConstants.ActivitySourceName);
                radiant.Sources.AddMeter(settings.Webserver.Telemetry.MeterName);
                radiant.Sources.AddActivitySource(settings.Webserver.Telemetry.ActivitySourceName);

                radiant.Otlp.Enable = telemetry.OtlpEnable;
                radiant.Otlp.Endpoint = telemetry.OtlpEndpoint;
                radiant.Otlp.Protocol = String.Equals(telemetry.OtlpProtocol, "httpprotobuf", StringComparison.OrdinalIgnoreCase)
                    ? OtlpProtocolEnum.HttpProtobuf
                    : OtlpProtocolEnum.Grpc;

                radiant.Prometheus.Enable = telemetry.PrometheusEnable;
                radiant.Prometheus.Hostname = telemetry.PrometheusHostname;
                radiant.Prometheus.Port = telemetry.PrometheusPort;

                radiant.Loki.Enable = telemetry.LokiEnable;
                radiant.Loki.Endpoint = telemetry.LokiEndpoint;
                radiant.Loki.TenantId = telemetry.LokiTenantId;
                radiant.Loki.MinimumSeverity = telemetry.LogsMinimumSeverity;

                radiant.Logs.MinimumSeverity = telemetry.LogsMinimumSeverity;
                radiant.Traces.SamplingRatio = telemetry.TraceSamplingRatio;
                radiant.Metrics.ExportIntervalMs = telemetry.MetricExportIntervalMs;
                radiant.Metrics.IncludeRuntime = telemetry.IncludeRuntimeMetrics;
                radiant.Metrics.IncludeProcess = telemetry.IncludeRuntimeMetrics;

                radiant.DiagnosticCallback = (msg) => _Logging.Debug("[Telemetry] " + msg);

                _Host = RadiantHost.Start(radiant);

                _Logging.Info(
                    "[Telemetry] Radiant host started for " + telemetry.ServiceName +
                    " (otlp " + (telemetry.OtlpEnable ? telemetry.OtlpProtocol + " " + telemetry.OtlpEndpoint : "off") +
                    ", prometheus " + (telemetry.PrometheusEnable ? "http://" + telemetry.PrometheusHostname + ":" + telemetry.PrometheusPort + "/metrics" : "off") +
                    ", loki " + (telemetry.LokiEnable ? telemetry.LokiEndpoint : "off") + ")");
            }
            catch (Exception e)
            {
                _Host = null;
                string cause = e.InnerException != null ? " (" + e.InnerException.GetType().Name + ": " + e.InnerException.Message + ")" : "";
                _Logging.Warn("[Telemetry] telemetry export disabled (host failed to start): " + e.Message + cause);
            }
        }

        internal bool IsEnabled
        {
            get => _Host != null && _Host.IsEnabled;
        }

        internal ILogger CreateLogger(string category)
        {
            if (_Host == null) return NullLogger.Instance;

            try
            {
                return _Host.CreateLogger(category);
            }
            catch (Exception)
            {
                return NullLogger.Instance;
            }
        }

        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            try
            {
                _Host?.ForceFlush(5000);
            }
            catch (Exception)
            {
                // best-effort
            }

            try
            {
                _Host?.Dispose();
            }
            catch (Exception)
            {
                // best-effort
            }
        }
    }
}
