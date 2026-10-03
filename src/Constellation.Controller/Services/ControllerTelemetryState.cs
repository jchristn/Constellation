namespace Constellation.Controller.Services
{
    using System;
    using Constellation.Core.Telemetry;

    internal class ControllerTelemetryState : IControllerTelemetryState
    {
        private readonly Settings _Settings;
        private readonly Func<WorkerService> _WorkerService;
        private readonly Func<ResponseService> _ResponseService;

        internal ControllerTelemetryState(Settings settings, Func<WorkerService> workerService, Func<ResponseService> responseService)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _WorkerService = workerService ?? throw new ArgumentNullException(nameof(workerService));
            _ResponseService = responseService ?? throw new ArgumentNullException(nameof(responseService));
        }

        public long HealthyWorkers
        {
            get
            {
                WorkerService svc = _WorkerService();
                return svc != null ? svc.HealthyCount : 0;
            }
        }

        public long UnhealthyWorkers
        {
            get
            {
                WorkerService svc = _WorkerService();
                return svc != null ? svc.UnhealthyCount : 0;
            }
        }

        public long MappedResources
        {
            get
            {
                WorkerService svc = _WorkerService();
                return svc != null ? svc.MappedResourceCount : 0;
            }
        }

        public long PendingResponses
        {
            get
            {
                ResponseService svc = _ResponseService();
                return svc != null ? svc.PendingCount : 0;
            }
        }

        public double LastHeartbeatSuccessUnixSeconds
        {
            get
            {
                WorkerService svc = _WorkerService();
                if (svc == null) return 0;
                DateTime last = svc.LastHeartbeatSuccessUtc;
                return last > DateTime.MinValue ? ConstellationTelemetry.UnixSeconds(last) : 0;
            }
        }

        public double LastCleanupSuccessUnixSeconds
        {
            get
            {
                ResponseService svc = _ResponseService();
                if (svc == null) return 0;
                DateTime last = svc.LastCleanupSuccessUtc;
                return last > DateTime.MinValue ? ConstellationTelemetry.UnixSeconds(last) : 0;
            }
        }

        public double HeartbeatIntervalSeconds
        {
            get => _Settings.Heartbeat.IntervalMs / 1000.0;
        }

        public long HeartbeatMaxFailures
        {
            get => _Settings.Heartbeat.MaxFailures;
        }

        public double ProxyTimeoutSeconds
        {
            get => _Settings.Proxy.TimeoutMs / 1000.0;
        }
    }
}
