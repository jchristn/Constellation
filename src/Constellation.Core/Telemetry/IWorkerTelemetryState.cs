namespace Constellation.Core.Telemetry
{
    /// <summary>
    /// Point-in-time worker state sampled by the observable gauges in <see cref="ConstellationTelemetry"/>.
    /// Implementations must be thread safe and must never throw.
    /// </summary>
    internal interface IWorkerTelemetryState
    {
        bool IsConnected { get; }
    }
}
