namespace Constellation.Worker
{
    using System;
    using Constellation.Core.Telemetry;

    internal class WorkerTelemetryState : IWorkerTelemetryState
    {
        private readonly Func<bool> _IsConnected;

        internal WorkerTelemetryState(Func<bool> isConnected)
        {
            _IsConnected = isConnected ?? throw new ArgumentNullException(nameof(isConnected));
        }

        public bool IsConnected
        {
            get
            {
                try
                {
                    return _IsConnected();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }
}
