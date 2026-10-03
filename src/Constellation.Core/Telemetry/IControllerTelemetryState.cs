namespace Constellation.Core.Telemetry
{
    /// <summary>
    /// Point-in-time controller state sampled by the observable gauges in <see cref="ConstellationTelemetry"/>.
    /// Implementations must be thread safe and must never throw.
    /// </summary>
    internal interface IControllerTelemetryState
    {
        long HealthyWorkers { get; }

        long UnhealthyWorkers { get; }

        long MappedResources { get; }

        long PendingResponses { get; }

        double LastHeartbeatSuccessUnixSeconds { get; }

        double LastCleanupSuccessUnixSeconds { get; }

        double HeartbeatIntervalSeconds { get; }

        long HeartbeatMaxFailures { get; }

        double ProxyTimeoutSeconds { get; }
    }
}
