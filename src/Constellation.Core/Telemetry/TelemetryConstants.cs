namespace Constellation.Core.Telemetry
{
    /// <summary>
    /// Stable telemetry names emitted by Constellation: the meter and activity source names, every
    /// metric instrument name, every span name, and every metric label and span attribute key.
    /// These strings are a public contract consumed by collectors and Grafana dashboards; treat a
    /// rename as a breaking change.  See TELEMETRY.md for the full catalog.
    /// Thread safety: all members are constants and are safe to read from any thread.
    /// </summary>
    public static class TelemetryConstants
    {
        #region Sources

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.Metrics.Meter"/> that every Constellation
        /// metric is recorded on.  Subscribe a collector to this name, for example
        /// <c>settings.Sources.AddMeter("Constellation")</c> with Radiant.
        /// </summary>
        public const string MeterName = "Constellation";

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.ActivitySource"/> that every Constellation
        /// span is started on.  Subscribe a collector to this name, for example
        /// <c>settings.Sources.AddActivitySource("Constellation")</c> with Radiant.
        /// </summary>
        public const string ActivitySourceName = "Constellation";

        #endregion

        #region Metrics-Proxy

        /// <summary>
        /// Counter of proxied requests by outcome and HTTP method.
        /// </summary>
        public const string ProxyRequests = "constellation.proxy.requests";

        /// <summary>
        /// Histogram of end-to-end proxy duration (placement through response), in seconds.
        /// </summary>
        public const string ProxyRequestDuration = "constellation.proxy.request.duration";

        /// <summary>
        /// Histogram of per-stage proxy duration, in seconds, labeled by stage and outcome.
        /// </summary>
        public const string ProxyStageDuration = "constellation.proxy.stage.duration";

        /// <summary>
        /// Counter of proxy stage executions, labeled by stage and outcome.
        /// </summary>
        public const string ProxyStageEvents = "constellation.proxy.stage.events";

        /// <summary>
        /// Up/down counter of proxy requests currently in flight (awaiting a worker response).
        /// </summary>
        public const string ProxyActiveRequests = "constellation.proxy.active_requests";

        #endregion

        #region Metrics-Placement

        /// <summary>
        /// Counter of placement (resource-to-worker) decisions, labeled by decision.
        /// </summary>
        public const string PlacementDecisions = "constellation.placement.decisions";

        /// <summary>
        /// Histogram of placement decision duration, in seconds, labeled by decision.
        /// </summary>
        public const string PlacementDuration = "constellation.placement.duration";

        /// <summary>
        /// Observable gauge of pinned resources currently mapped to workers.
        /// </summary>
        public const string ResourcesMapped = "constellation.resources.mapped";

        #endregion

        #region Metrics-Worker-Pool

        /// <summary>
        /// Observable gauge of workers registered with the controller, labeled by state (healthy, unhealthy).
        /// </summary>
        public const string Workers = "constellation.workers";

        /// <summary>
        /// Counter of worker pool lifecycle events (connected, disconnected, unhealthy, recovered, evicted).
        /// </summary>
        public const string WorkerPoolEvents = "constellation.workers.events";

        /// <summary>
        /// Histogram of worker session lifetime from registration to disconnection, in seconds.
        /// </summary>
        public const string WorkerSessionDuration = "constellation.workers.session.duration";

        #endregion

        #region Metrics-Heartbeat

        /// <summary>
        /// Counter of heartbeats sent by the controller, labeled by outcome.
        /// </summary>
        public const string Heartbeats = "constellation.heartbeats";

        /// <summary>
        /// Histogram of heartbeat send duration, in seconds, labeled by outcome.
        /// </summary>
        public const string HeartbeatDuration = "constellation.heartbeat.duration";

        /// <summary>
        /// Observable gauge of the most recent successful heartbeat, as Unix time in seconds.
        /// </summary>
        public const string HeartbeatLastSuccess = "constellation.heartbeat.last_success";

        #endregion

        #region Metrics-Responses

        /// <summary>
        /// Observable gauge of worker responses held in the correlation store awaiting pickup.
        /// </summary>
        public const string ResponsesPending = "constellation.responses.pending";

        /// <summary>
        /// Counter of worker responses offered to the correlation store, labeled by outcome.
        /// </summary>
        public const string ResponsesReceived = "constellation.responses.received";

        /// <summary>
        /// Counter of orphaned responses evicted from the correlation store after expiry.
        /// </summary>
        public const string ResponsesExpired = "constellation.responses.expired";

        /// <summary>
        /// Histogram of correlation-store cleanup pass duration, in seconds, labeled by outcome.
        /// </summary>
        public const string ResponsesCleanupDuration = "constellation.responses.cleanup.duration";

        /// <summary>
        /// Observable gauge of the most recent successful correlation-store cleanup pass, as Unix time in seconds.
        /// </summary>
        public const string ResponsesCleanupLastSuccess = "constellation.responses.cleanup.last_success";

        #endregion

        #region Metrics-Websocket

        /// <summary>
        /// Counter of WebSocket messages, labeled by component, direction, message type, and outcome.
        /// </summary>
        public const string WebsocketMessages = "constellation.websocket.messages";

        /// <summary>
        /// Histogram of WebSocket message size in bytes, labeled by component, direction, and message type.
        /// </summary>
        public const string WebsocketMessageSize = "constellation.websocket.message.size";

        #endregion

        #region Metrics-Admin

        /// <summary>
        /// Counter of administrative API requests, labeled by operation and outcome.
        /// </summary>
        public const string AdminRequests = "constellation.admin.requests";

        #endregion

        #region Metrics-Worker

        /// <summary>
        /// Counter of requests handled by a worker, labeled by outcome.
        /// </summary>
        public const string WorkerRequests = "constellation.worker.requests";

        /// <summary>
        /// Histogram of worker request handling duration (deserialize, handler, reply), in seconds, labeled by outcome.
        /// </summary>
        public const string WorkerRequestDuration = "constellation.worker.request.duration";

        /// <summary>
        /// Histogram of the application's request handler duration on the worker, in seconds, labeled by outcome.
        /// </summary>
        public const string WorkerHandlerDuration = "constellation.worker.handler.duration";

        /// <summary>
        /// Counter of worker connection attempts to the controller, labeled by outcome.
        /// </summary>
        public const string WorkerConnectionAttempts = "constellation.worker.connection.attempts";

        /// <summary>
        /// Counter of worker connection state changes (connected, disconnected), labeled by event.
        /// </summary>
        public const string WorkerConnectionEvents = "constellation.worker.connection.events";

        /// <summary>
        /// Observable gauge of worker instances in this process currently connected to a controller.
        /// </summary>
        public const string WorkerConnected = "constellation.worker.connected";

        #endregion

        #region Metrics-General

        /// <summary>
        /// Counter of caught errors, labeled by component, operation, and error type.
        /// </summary>
        public const string Errors = "constellation.errors";

        /// <summary>
        /// Observable gauge with constant value 1 carrying build information as labels (version, runtime).
        /// </summary>
        public const string BuildInfo = "constellation.build.info";

        /// <summary>
        /// Observable gauge of the configured heartbeat interval, in seconds.
        /// </summary>
        public const string ConfigHeartbeatInterval = "constellation.config.heartbeat.interval";

        /// <summary>
        /// Observable gauge of the configured maximum heartbeat failures before eviction.
        /// </summary>
        public const string ConfigHeartbeatMaxFailures = "constellation.config.heartbeat.max_failures";

        /// <summary>
        /// Observable gauge of the configured proxy timeout, in seconds.
        /// </summary>
        public const string ConfigProxyTimeout = "constellation.config.proxy.timeout";

        #endregion

        #region Spans

        /// <summary>
        /// Span covering one proxied request inside the Watson server span.
        /// </summary>
        public const string SpanProxy = "constellation proxy";

        /// <summary>
        /// Client span covering the outbound call to a worker over WebSocket (dispatch plus wait).
        /// </summary>
        public const string SpanWorkerRequest = "worker request";

        /// <summary>
        /// Worker-side server span covering the handling of one proxied request.
        /// </summary>
        public const string SpanWorkerHandle = "worker handle";

        /// <summary>
        /// Controller-side consumer span covering the receipt of one worker response.
        /// </summary>
        public const string SpanResponseReceive = "websocket receive response";

        /// <summary>
        /// Span covering an administrative API call.  The operation name is appended.
        /// </summary>
        public const string SpanAdminPrefix = "admin ";

        /// <summary>
        /// Span covering registration of a newly connected worker.
        /// </summary>
        public const string SpanWorkerRegister = "worker register";

        /// <summary>
        /// Span covering removal of a disconnected worker.
        /// </summary>
        public const string SpanWorkerUnregister = "worker unregister";

        /// <summary>
        /// Span emitted for a failed or errored heartbeat.  Successful heartbeats emit metrics only.
        /// </summary>
        public const string SpanHeartbeat = "worker heartbeat";

        /// <summary>
        /// Span emitted when a worker exceeds its heartbeat failure limit and is marked unhealthy.
        /// </summary>
        public const string SpanWorkerEvict = "worker evict";

        /// <summary>
        /// Root span covering one correlation-store cleanup pass.
        /// </summary>
        public const string SpanResponsesCleanup = "responses cleanup";

        /// <summary>
        /// Worker-side span covering one connection attempt to the controller.
        /// </summary>
        public const string SpanWorkerConnect = "worker connect";

        /// <summary>
        /// Prefix for stage spans, for example <c>stage:placement</c>.
        /// </summary>
        public const string SpanStagePrefix = "stage:";

        #endregion

        #region Stages

        /// <summary>
        /// Proxy stage: choose (or reuse) the worker that owns the resource.
        /// </summary>
        public const string StagePlacement = "placement";

        /// <summary>
        /// Proxy stage: serialize the request and send it to the worker over WebSocket.
        /// </summary>
        public const string StageDispatch = "dispatch";

        /// <summary>
        /// Proxy stage: wait for the correlated worker response (the "queued" state of the proxy).
        /// </summary>
        public const string StageAwaitResponse = "await_response";

        /// <summary>
        /// Proxy stage: write the worker response back to the HTTP caller.
        /// </summary>
        public const string StageRespond = "respond";

        /// <summary>
        /// Worker stage: run the application's request handler.
        /// </summary>
        public const string StageHandler = "handler";

        /// <summary>
        /// Worker stage: serialize and send the response back to the controller.
        /// </summary>
        public const string StageReply = "reply";

        #endregion

        #region Label-Keys

        /// <summary>
        /// Label key: operation or stage outcome.
        /// </summary>
        public const string LabelOutcome = "outcome";

        /// <summary>
        /// Label key: proxy stage.
        /// </summary>
        public const string LabelStage = "stage";

        /// <summary>
        /// Label key: placement decision.
        /// </summary>
        public const string LabelDecision = "decision";

        /// <summary>
        /// Label key: lifecycle event.
        /// </summary>
        public const string LabelEvent = "event";

        /// <summary>
        /// Label key: worker state.
        /// </summary>
        public const string LabelState = "state";

        /// <summary>
        /// Label key: emitting component (controller or worker).
        /// </summary>
        public const string LabelComponent = "component";

        /// <summary>
        /// Label key: message direction (sent or received).
        /// </summary>
        public const string LabelDirection = "direction";

        /// <summary>
        /// Label key: WebSocket message type.
        /// </summary>
        public const string LabelMessageType = "message.type";

        /// <summary>
        /// Label key: operation name.
        /// </summary>
        public const string LabelOperation = "operation";

        /// <summary>
        /// Label key: error type (exception type name or a bounded error code).
        /// </summary>
        public const string LabelErrorType = "error.type";

        /// <summary>
        /// Label key: HTTP request method (normalized to a bounded set).
        /// </summary>
        public const string LabelHttpMethod = "http.request.method";

        /// <summary>
        /// Label key: build version.
        /// </summary>
        public const string LabelVersion = "version";

        /// <summary>
        /// Label key: .NET runtime description.
        /// </summary>
        public const string LabelRuntime = "runtime";

        #endregion

        #region Span-Attribute-Keys

        /// <summary>
        /// Span attribute: the resource key (raw URL path without query).  Span only, never a metric label.
        /// </summary>
        public const string AttrResource = "constellation.resource";

        /// <summary>
        /// Span attribute: the worker GUID.  Span only, never a metric label.
        /// </summary>
        public const string AttrWorkerId = "constellation.worker.id";

        /// <summary>
        /// Span attribute: the WebSocket message GUID.  Span only, never a metric label.
        /// </summary>
        public const string AttrMessageId = "constellation.message.id";

        /// <summary>
        /// Span attribute: the placement decision.
        /// </summary>
        public const string AttrPlacementDecision = "constellation.placement.decision";

        /// <summary>
        /// Span attribute: the outcome of the span's operation.
        /// </summary>
        public const string AttrOutcome = "constellation.outcome";

        /// <summary>
        /// Span attribute: the number of consecutive heartbeat failures.
        /// </summary>
        public const string AttrHeartbeatFailures = "constellation.heartbeat.failures";

        /// <summary>
        /// Span attribute: number of items affected by the operation.
        /// </summary>
        public const string AttrCount = "constellation.count";

        /// <summary>
        /// Span attribute: HTTP response status code returned by the worker.
        /// </summary>
        public const string AttrHttpStatusCode = "http.response.status_code";

        /// <summary>
        /// Span attribute: HTTP request method.
        /// </summary>
        public const string AttrHttpMethod = "http.request.method";

        /// <summary>
        /// Span attribute: messaging system, always <c>websocket</c>.
        /// </summary>
        public const string AttrMessagingSystem = "messaging.system";

        /// <summary>
        /// Span attribute: server address the worker connects to.
        /// </summary>
        public const string AttrServerAddress = "server.address";

        /// <summary>
        /// Span attribute: server port the worker connects to.
        /// </summary>
        public const string AttrServerPort = "server.port";

        /// <summary>
        /// Span attribute: error type for failed spans.
        /// </summary>
        public const string AttrErrorType = "error.type";

        #endregion

        #region Label-Values

        /// <summary>
        /// Component value: controller.
        /// </summary>
        public const string ComponentController = "controller";

        /// <summary>
        /// Component value: worker.
        /// </summary>
        public const string ComponentWorker = "worker";

        /// <summary>
        /// Outcome value: success.
        /// </summary>
        public const string OutcomeSuccess = "success";

        /// <summary>
        /// Outcome value: failure (an expected, handled failure such as a failed send).
        /// </summary>
        public const string OutcomeFailure = "failure";

        /// <summary>
        /// Outcome value: error (an unexpected exception).
        /// </summary>
        public const string OutcomeError = "error";

        /// <summary>
        /// Outcome value: timeout.
        /// </summary>
        public const string OutcomeTimeout = "timeout";

        /// <summary>
        /// Outcome value: no worker was available to own the resource.
        /// </summary>
        public const string OutcomeNoWorker = "no_worker";

        /// <summary>
        /// Outcome value: the request could not be sent to the worker.
        /// </summary>
        public const string OutcomeSendFailed = "send_failed";

        /// <summary>
        /// Outcome value: the worker returned no response.
        /// </summary>
        public const string OutcomeNoResponse = "no_response";

        /// <summary>
        /// Outcome value: the operation was canceled (for example during shutdown).
        /// </summary>
        public const string OutcomeCanceled = "canceled";

        /// <summary>
        /// Outcome value: the caller was not authorized.
        /// </summary>
        public const string OutcomeUnauthorized = "unauthorized";

        /// <summary>
        /// Outcome value: a duplicate item was rejected.
        /// </summary>
        public const string OutcomeDuplicate = "duplicate";

        /// <summary>
        /// Outcome value: the message was discarded (for example from an unknown sender).
        /// </summary>
        public const string OutcomeDiscarded = "discarded";

        /// <summary>
        /// Decision value: the resource was already pinned to a healthy worker.
        /// </summary>
        public const string DecisionPinned = "pinned";

        /// <summary>
        /// Decision value: the resource was assigned to a worker for the first time.
        /// </summary>
        public const string DecisionAssigned = "assigned";

        /// <summary>
        /// Decision value: the resource's previous worker was gone or unhealthy and it was reassigned (failover).
        /// </summary>
        public const string DecisionReassigned = "reassigned";

        /// <summary>
        /// Decision value: no workers were registered.
        /// </summary>
        public const string DecisionNoWorkers = "no_workers";

        /// <summary>
        /// Decision value: workers were registered but none were healthy.
        /// </summary>
        public const string DecisionNoHealthyWorkers = "no_healthy_workers";

        /// <summary>
        /// State value: healthy.
        /// </summary>
        public const string StateHealthy = "healthy";

        /// <summary>
        /// State value: unhealthy.
        /// </summary>
        public const string StateUnhealthy = "unhealthy";

        /// <summary>
        /// Event value: connected.
        /// </summary>
        public const string EventConnected = "connected";

        /// <summary>
        /// Event value: disconnected.
        /// </summary>
        public const string EventDisconnected = "disconnected";

        /// <summary>
        /// Event value: marked unhealthy after exceeding the heartbeat failure limit.
        /// </summary>
        public const string EventEvicted = "evicted";

        /// <summary>
        /// Event value: returned to healthy after a successful heartbeat.
        /// </summary>
        public const string EventRecovered = "recovered";

        /// <summary>
        /// Direction value: sent.
        /// </summary>
        public const string DirectionSent = "sent";

        /// <summary>
        /// Direction value: received.
        /// </summary>
        public const string DirectionReceived = "received";

        /// <summary>
        /// Operation value: list workers (admin API).
        /// </summary>
        public const string OperationListWorkers = "list_workers";

        /// <summary>
        /// Operation value: list resource maps (admin API).
        /// </summary>
        public const string OperationListMaps = "list_maps";

        /// <summary>
        /// Operation value: authenticate an admin request.
        /// </summary>
        public const string OperationAuthenticate = "authenticate";

        /// <summary>
        /// Operation value: proxy a request.
        /// </summary>
        public const string OperationProxy = "proxy";

        /// <summary>
        /// Operation value: process an inbound WebSocket message.
        /// </summary>
        public const string OperationReceive = "receive";

        /// <summary>
        /// Operation value: send a heartbeat.
        /// </summary>
        public const string OperationHeartbeat = "heartbeat";

        /// <summary>
        /// Operation value: correlation-store cleanup.
        /// </summary>
        public const string OperationCleanup = "cleanup";

        /// <summary>
        /// Operation value: worker connection maintenance.
        /// </summary>
        public const string OperationConnect = "connect";

        /// <summary>
        /// Operation value: worker request handling.
        /// </summary>
        public const string OperationHandle = "handle";

        /// <summary>
        /// Operation value: lifecycle callback (OnConnection or OnDisconnection).
        /// </summary>
        public const string OperationLifecycle = "lifecycle";

        /// <summary>
        /// HTTP method value used for any method outside the well-known set.
        /// </summary>
        public const string HttpMethodOther = "_OTHER";

        #endregion
    }
}
