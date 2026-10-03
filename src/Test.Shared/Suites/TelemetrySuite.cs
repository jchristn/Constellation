namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Controller;
    using Constellation.Controller.Services;
    using Constellation.Core;
    using Constellation.Core.Telemetry;
    using SyslogLogging;
    using Touchstone.Core;
    using WatsonWebsocket;

    using TC = Constellation.Core.Telemetry.TelemetryConstants;

    /// <summary>
    /// Proves that Constellation emits its documented metrics and spans: placement, the proxy pipeline and every
    /// stage, the outbound worker call and W3C propagation across the WebSocket hop, worker-side handling, the
    /// heartbeat background job (including eviction), the response correlation store, admin APIs, worker
    /// lifecycle, observable gauges, and failure paths.  Also proves the no-listener path is inert and that a
    /// throwing listener never escapes into application code.  Each case subscribes an in-memory
    /// <see cref="TelemetryCapture"/> by meter/source name, exactly as a host would.
    /// </summary>
    public static class TelemetrySuite
    {
        private const string SuiteId = "Telemetry";
        private static readonly Regex _UnboundedValue = new Regex(
            "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-|^/",
            RegexOptions.Compiled);

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("StableNames", "Meter and activity source use the documented stable names", ct =>
                {
                    Check.Equal("Constellation", ConstellationTelemetry.Meter.Name, "meter name");
                    Check.Equal("Constellation", ConstellationTelemetry.ActivitySource.Name, "activity source name");
                    Check.Equal(TC.MeterName, ConstellationTelemetry.Meter.Name, "meter name constant");
                    Check.True(!String.IsNullOrEmpty(ConstellationTelemetry.Version), "version populated");
                    return Task.CompletedTask;
                }),

                Case("NoListenerIsInert", "With no listener, spans are null and recording never throws", async ct =>
                {
                    Check.False(ConstellationTelemetry.ActivitySource.HasListeners(), "no activity listeners attached");
                    Check.Null(ConstellationTelemetry.StartActivity("x", ActivityKind.Internal), "unobserved span is null");
                    Check.Null(ConstellationTelemetry.StartActivityFromTraceParent("x", ActivityKind.Server, "garbage", null), "unobserved remote span is null");

                    ConstellationTelemetry.SetOk(null);
                    ConstellationTelemetry.SetError(null, TC.OutcomeError, "x");
                    ConstellationTelemetry.SetException(null, new InvalidOperationException("x"));
                    ConstellationTelemetry.SetTag(null, "k", "v");
                    ConstellationTelemetry.Stop(null);
                    ConstellationTelemetry.Inject(null, null);
                    ConstellationTelemetry.Inject(null, new WebsocketMessage());
                    ConstellationTelemetry.RecordError(TC.ComponentController, TC.OperationProxy, (Exception)null);
                    ConstellationTelemetry.RecordWebsocketMessage(TC.ComponentWorker, TC.DirectionSent, WebsocketMessageTypeEnum.Request, TC.OutcomeSuccess, 10);

                    WorkerService svc = NewService();
                    svc.AddWorker(NewWorker());
                    Check.NotNull(svc.GetByResource("/inert"), "placement works without telemetry");

                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        await env.AddWorkerAsync(1, 1200);
                        Check.True(await env.WaitForWorkerCountAsync(1), "worker connected");
                        HttpTestResponse resp = await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/api/inert");
                        Check.Equal(200, resp.StatusCode, "proxy works without telemetry");
                    }
                }),

                Case("ThrowingListenerContained", "A listener that throws never escapes into application code", async ct =>
                {
                    using (MeterListener meterListener = new MeterListener())
                    using (ActivityListener activityListener = new ActivityListener
                    {
                        ShouldListenTo = s => s.Name == TC.ActivitySourceName,
                        Sample = (ref ActivityCreationOptions<ActivityContext> o) => ActivitySamplingResult.AllDataAndRecorded,
                        ActivityStarted = a => throw new InvalidOperationException("listener start failure"),
                        ActivityStopped = a => throw new InvalidOperationException("listener stop failure")
                    })
                    {
                        meterListener.InstrumentPublished = (i, l) => { if (i.Meter.Name == TC.MeterName) l.EnableMeasurementEvents(i); };
                        meterListener.SetMeasurementEventCallback<long>((i, v, t, s) => throw new InvalidOperationException("listener measurement failure"));
                        meterListener.SetMeasurementEventCallback<double>((i, v, t, s) => throw new InvalidOperationException("listener measurement failure"));
                        meterListener.Start();
                        ActivitySource.AddActivityListener(activityListener);

                        WorkerService svc = NewService();
                        svc.AddWorker(NewWorker());
                        Check.NotNull(svc.GetByResource("/throwing"), "placement survives a throwing listener");

                        using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                        {
                            await env.AddWorkerAsync(1, 1200);
                            Check.True(await env.WaitForWorkerCountAsync(1), "worker connected");
                            HttpTestResponse resp = await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/api/throwing");
                            Check.Equal(200, resp.StatusCode, "proxy survives a throwing listener");
                        }
                    }
                }),

                Case("PlacementDecisions", "Placement emits decision counters and duration for every decision", ct =>
                {
                    using (TelemetryCapture cap = new TelemetryCapture())
                    {
                        WorkerService empty = NewService();
                        Check.Null(empty.GetByResource("/none"), "no workers");

                        WorkerService allSick = NewService();
                        WorkerMetadata sick = NewWorker();
                        sick.Healthy = false;
                        allSick.AddWorker(sick);
                        Check.Null(allSick.GetByResource("/sick"), "no healthy workers");

                        WorkerService svc = NewService();
                        WorkerMetadata a = NewWorker();
                        WorkerMetadata b = NewWorker();
                        svc.AddWorker(a);
                        svc.AddWorker(b);
                        WorkerMetadata owner = svc.GetByResource("/p");
                        svc.GetByResource("/p");
                        owner.Healthy = false;
                        svc.GetByResource("/p");

                        Check.True(cap.Sum(TC.PlacementDecisions, TC.LabelDecision, TC.DecisionNoWorkers) >= 1, "no_workers counted");
                        Check.True(cap.Sum(TC.PlacementDecisions, TC.LabelDecision, TC.DecisionNoHealthyWorkers) >= 1, "no_healthy_workers counted");
                        Check.True(cap.Sum(TC.PlacementDecisions, TC.LabelDecision, TC.DecisionAssigned) >= 1, "assigned counted");
                        Check.True(cap.Sum(TC.PlacementDecisions, TC.LabelDecision, TC.DecisionPinned) >= 1, "pinned counted");
                        Check.True(cap.Sum(TC.PlacementDecisions, TC.LabelDecision, TC.DecisionReassigned) >= 1, "reassigned (failover) counted");
                        Check.True(cap.Count(TC.PlacementDuration) >= 5, "placement duration recorded per decision");
                        Check.True(cap.Measurements(TC.PlacementDuration).All(m => m.Value >= 0), "durations non-negative");
                    }
                    return Task.CompletedTask;
                }),

                Case("ProxySuccessTraceAndMetrics", "A proxied request emits every stage and one stitched trace across the WebSocket hop", async ct =>
                {
                    using (TelemetryCapture cap = new TelemetryCapture())
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        await env.AddWorkerAsync(1, 1200);
                        Check.True(await env.WaitForWorkerCountAsync(1), "worker connected");

                        ActivityTraceId traceId = ActivityTraceId.CreateRandom();
                        string traceparent = "00-" + traceId.ToHexString() + "-" + ActivitySpanId.CreateRandom().ToHexString() + "-01";
                        HttpTestResponse resp = await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/api/traced", null,
                            new Dictionary<string, string> { ["traceparent"] = traceparent });
                        Check.Equal(200, resp.StatusCode, "status");

                        string[] expected =
                        {
                            TC.SpanProxy,
                            TC.SpanStagePrefix + TC.StagePlacement,
                            TC.SpanWorkerRequest,
                            TC.SpanStagePrefix + TC.StageDispatch,
                            TC.SpanStagePrefix + TC.StageAwaitResponse,
                            TC.SpanStagePrefix + TC.StageRespond,
                            TC.SpanWorkerHandle,
                            TC.SpanStagePrefix + TC.StageHandler,
                            TC.SpanStagePrefix + TC.StageReply,
                            TC.SpanResponseReceive
                        };

                        bool complete = await cap.WaitForAsync(() =>
                        {
                            List<string> names = cap.Trace(traceId).Select(a => a.DisplayName).ToList();
                            return expected.All(n => names.Contains(n));
                        });
                        List<Activity> trace = cap.Trace(traceId);
                        Check.True(complete, "all spans share the caller's trace id; got: " + String.Join(", ", trace.Select(a => a.DisplayName)));

                        Activity proxy = trace.First(a => a.DisplayName == TC.SpanProxy);
                        Activity client = trace.First(a => a.DisplayName == TC.SpanWorkerRequest);
                        Activity handle = trace.First(a => a.DisplayName == TC.SpanWorkerHandle);
                        Activity reply = trace.First(a => a.DisplayName == TC.SpanStagePrefix + TC.StageReply);
                        Activity receive = trace.First(a => a.DisplayName == TC.SpanResponseReceive);

                        Check.Equal(ActivityKind.Client, client.Kind, "worker request is a client span");
                        Check.Equal(ActivityKind.Server, handle.Kind, "worker handle is a server span");
                        Check.Equal(client.SpanId, handle.ParentSpanId, "worker handle is a child of the controller's client span");
                        Check.Equal(reply.SpanId, receive.ParentSpanId, "controller receipt is a child of the worker's reply span");
                        Check.Equal(proxy.SpanId, client.ParentSpanId, "client span nests under the proxy span");
                        Check.Equal(ActivityStatusCode.Ok, proxy.Status, "proxy span status ok");
                        Check.Equal("/api/traced", proxy.GetTagItem(TC.AttrResource) as string, "resource on span (not on metrics)");
                        Check.NotNull(proxy.GetTagItem(TC.AttrWorkerId), "worker id on span");
                        Check.Equal(TC.DecisionAssigned, proxy.GetTagItem(TC.AttrPlacementDecision) as string, "placement decision on span");

                        Check.True(await cap.WaitForAsync(() => cap.Sum(TC.WorkerRequests, TC.LabelOutcome, TC.OutcomeSuccess) >= 1), "worker request counted");
                        Check.True(cap.Sum(TC.ProxyRequests, TC.LabelOutcome, TC.OutcomeSuccess, TC.LabelHttpMethod, "GET") >= 1, "proxy success counted with method");
                        Check.True(cap.Count(TC.ProxyRequestDuration, TC.LabelOutcome, TC.OutcomeSuccess) >= 1, "proxy duration recorded");
                        foreach (string stage in new[] { TC.StagePlacement, TC.StageDispatch, TC.StageAwaitResponse, TC.StageRespond })
                        {
                            Check.True(cap.Sum(TC.ProxyStageEvents, TC.LabelStage, stage, TC.LabelOutcome, TC.OutcomeSuccess) >= 1, "stage event " + stage);
                            Check.True(cap.Count(TC.ProxyStageDuration, TC.LabelStage, stage) >= 1, "stage duration " + stage);
                        }

                        Check.Equal(0.0, cap.Sum(TC.ProxyActiveRequests), "active requests return to zero");
                        Check.True(cap.Sum(TC.ResponsesReceived, TC.LabelOutcome, TC.OutcomeSuccess) >= 1, "response stored");
                        Check.True(cap.Count(TC.WorkerHandlerDuration) >= 1, "worker handler duration recorded");
                        Check.True(cap.Sum(TC.WebsocketMessages, TC.LabelComponent, TC.ComponentController, TC.LabelDirection, TC.DirectionSent, TC.LabelMessageType, "request") >= 1, "controller request message counted");
                        Check.True(cap.Sum(TC.WebsocketMessages, TC.LabelComponent, TC.ComponentWorker, TC.LabelDirection, TC.DirectionSent, TC.LabelMessageType, "response") >= 1, "worker response message counted");
                        Check.True(cap.Count(TC.WebsocketMessageSize) >= 1, "message size recorded");
                        Check.True(cap.Count("http.server.request.duration") >= 1, "Watson HTTP metrics flow through the same subscription");
                    }
                }),

                Case("NoWorkerFailurePath", "A request with no workers records no_worker outcome and an error span", async ct =>
                {
                    using (TelemetryCapture cap = new TelemetryCapture())
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        HttpTestResponse resp = await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/api/orphan");
                        Check.Equal(502, resp.StatusCode, "status");

                        Check.True(await cap.WaitForAsync(() => cap.Sum(TC.ProxyRequests, TC.LabelOutcome, TC.OutcomeNoWorker) >= 1), "no_worker outcome counted");
                        Check.True(cap.Sum(TC.ProxyStageEvents, TC.LabelStage, TC.StagePlacement, TC.LabelOutcome, TC.OutcomeNoWorker) >= 1, "placement stage failure counted");

                        Activity proxy = cap.Spans(TC.SpanProxy).LastOrDefault(a => (a.GetTagItem(TC.AttrResource) as string) == "/api/orphan");
                        Check.NotNull(proxy, "proxy span emitted");
                        Check.Equal(ActivityStatusCode.Error, proxy.Status, "proxy span marked error");
                        Check.Equal(TC.OutcomeNoWorker, proxy.GetTagItem(TC.AttrOutcome) as string, "outcome attribute");
                        Check.True(cap.Spans(TC.SpanStagePrefix + TC.StagePlacement).Any(a => a.Status == ActivityStatusCode.Error), "placement span marked error");
                    }
                }),

                Case("TimeoutFailurePath", "A slow worker yields 408 with timeout outcome on the await_response stage", async ct =>
                {
                    using (TelemetryCapture cap = new TelemetryCapture())
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync(proxyTimeoutMs: 1000))
                    {
                        TestWorkerHarness worker = await env.AddWorkerAsync(1, 1200);
                        worker.ResponseDelayMs = 2500;
                        Check.True(await env.WaitForWorkerCountAsync(1), "worker connected");

                        HttpTestResponse resp = await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/api/slow");
                        Check.Equal(408, resp.StatusCode, "timeout answers 408");

                        Check.True(await cap.WaitForAsync(() => cap.Sum(TC.ProxyRequests, TC.LabelOutcome, TC.OutcomeTimeout) >= 1), "timeout outcome counted");
                        Check.True(cap.Sum(TC.ProxyStageEvents, TC.LabelStage, TC.StageAwaitResponse, TC.LabelOutcome, TC.OutcomeTimeout) >= 1, "await_response timeout counted");
                        Check.True(cap.Spans(TC.SpanWorkerRequest).Any(a => a.Status == ActivityStatusCode.Error && (a.GetTagItem(TC.AttrOutcome) as string) == TC.OutcomeTimeout), "client span marked timeout");
                        Check.True(cap.Measurements(TC.ProxyRequestDuration).Any(m => m.Tag(TC.LabelOutcome) == TC.OutcomeTimeout && m.Value >= 0.9), "timeout duration reflects the wait");
                    }
                }),

                Case("WorkerHandlerFailurePath", "A throwing worker handler records an error span with an exception event", async ct =>
                {
                    using (TelemetryCapture cap = new TelemetryCapture())
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync(proxyTimeoutMs: 1000))
                    {
                        TestWorkerHarness worker = await env.AddWorkerAsync(1, 1200);
                        worker.ThrowOnRequest = true;
                        Check.True(await env.WaitForWorkerCountAsync(1), "worker connected");

                        HttpTestResponse resp = await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/api/throws");
                        Check.Equal(408, resp.StatusCode, "controller times out waiting for the failed worker");

                        Check.True(await cap.WaitForAsync(() => cap.Sum(TC.WorkerRequests, TC.LabelOutcome, TC.OutcomeError) >= 1), "worker error outcome counted");
                        Check.True(cap.Sum(TC.Errors, TC.LabelComponent, TC.ComponentWorker, TC.LabelOperation, TC.OperationHandle, TC.LabelErrorType, typeof(InvalidOperationException).FullName) >= 1, "error counted by type");
                        Activity handle = cap.Spans(TC.SpanWorkerHandle).LastOrDefault(a => a.Status == ActivityStatusCode.Error);
                        Check.NotNull(handle, "worker handle span marked error");
                        Check.True(handle.Events.Any(e => e.Name == "exception"), "exception event recorded");
                        Check.True(cap.Count(TC.WorkerHandlerDuration, TC.LabelOutcome, TC.OutcomeError) >= 1, "handler error duration recorded");
                    }
                }),

                Case("AdminApiMetrics", "Admin APIs record operation/outcome counters and spans", async ct =>
                {
                    using (TelemetryCapture cap = new TelemetryCapture())
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        Dictionary<string, string> good = new Dictionary<string, string> { ["x-api-key"] = "constellationadmin" };
                        Dictionary<string, string> bad = new Dictionary<string, string> { ["x-api-key"] = "wrong" };
                        Check.Equal(200, (await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/workers", null, good)).StatusCode, "workers");
                        Check.Equal(200, (await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/maps", null, good)).StatusCode, "maps");
                        Check.Equal(401, (await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/maps", null, bad)).StatusCode, "bad key");

                        Check.True(await cap.WaitForAsync(() => cap.Sum(TC.AdminRequests, TC.LabelOperation, TC.OperationAuthenticate, TC.LabelOutcome, TC.OutcomeUnauthorized) >= 1), "unauthorized counted");
                        Check.True(cap.Sum(TC.AdminRequests, TC.LabelOperation, TC.OperationListWorkers, TC.LabelOutcome, TC.OutcomeSuccess) >= 1, "list_workers counted");
                        Check.True(cap.Sum(TC.AdminRequests, TC.LabelOperation, TC.OperationListMaps, TC.LabelOutcome, TC.OutcomeSuccess) >= 1, "list_maps counted");
                        Check.True(cap.Spans(TC.SpanAdminPrefix + TC.OperationListWorkers).Any(a => a.Status == ActivityStatusCode.Ok), "admin span emitted");
                    }
                }),

                Case("WorkerLifecycleAndHeartbeat", "Worker connect, heartbeats, gauges, and disconnect are all observable", async ct =>
                {
                    using (TelemetryCapture cap = new TelemetryCapture())
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync(heartbeatIntervalMs: 1000))
                    {
                        TestWorkerHarness w1 = await env.AddWorkerAsync(1, 1200);
                        await env.AddWorkerAsync(2, 1200);
                        Check.True(await env.WaitForWorkerCountAsync(2), "two workers connected");
                        Check.Equal(200, (await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/api/gauge")).StatusCode, "mapped one resource");

                        Check.True(await cap.WaitForAsync(() => cap.Sum(TC.Heartbeats, TC.LabelOutcome, TC.OutcomeSuccess) >= 2), "heartbeats counted");
                        Check.True(cap.Count(TC.HeartbeatDuration) >= 2, "heartbeat duration recorded");
                        Check.True(cap.Sum(TC.WorkerPoolEvents, TC.LabelEvent, TC.EventConnected) >= 2, "pool connected events");
                        Check.True(cap.Sum(TC.WorkerConnectionAttempts, TC.LabelOutcome, TC.OutcomeSuccess) >= 2, "worker connection attempts");
                        Check.True(cap.Sum(TC.WorkerConnectionEvents, TC.LabelEvent, TC.EventConnected) >= 2, "worker connection events");
                        Check.True(cap.Sum(TC.WebsocketMessages, TC.LabelMessageType, "heartbeat", TC.LabelDirection, TC.DirectionSent, TC.LabelComponent, TC.ComponentController) >= 2, "heartbeat messages");

                        Activity register = cap.Spans(TC.SpanWorkerRegister).LastOrDefault();
                        Check.NotNull(register, "register span");
                        Check.Equal(default(ActivitySpanId), register.ParentSpanId, "register span is a root span");

                        cap.RecordObservables();
                        Check.True(cap.Sum(TC.Workers, TC.LabelState, TC.StateHealthy) >= 2, "healthy workers gauge");
                        Check.True(cap.Sum(TC.ResourcesMapped) >= 1, "mapped resources gauge");
                        Check.True(cap.Measurements(TC.HeartbeatLastSuccess).Any(m => m.Value > 1_600_000_000), "last heartbeat gauge is a unix timestamp");
                        Check.True(cap.Measurements(TC.ConfigHeartbeatInterval).Any(m => m.Value >= 1.0), "heartbeat interval config gauge");
                        Check.True(cap.Measurements(TC.ConfigProxyTimeout).Any(m => m.Value >= 1.0), "proxy timeout config gauge");
                        Check.True(cap.Measurements(TC.ConfigHeartbeatMaxFailures).Any(m => m.Value >= 1), "max failures config gauge");
                        Check.True(cap.Measurements(TC.WorkerConnected).Any(m => m.Value >= 2), "connected workers gauge");
                        Check.True(cap.Measurements(TC.BuildInfo).Any(m => m.Value == 1 && m.Tag(TC.LabelVersion) == ConstellationTelemetry.Version && m.Tag(TC.LabelRuntime) != null), "build info gauge");
                        Check.True(cap.Measurements(TC.ResponsesPending).Any(), "pending responses gauge");

                        await env.RemoveWorkerAsync(w1, 1500);
                        Check.True(await env.WaitForWorkerCountAsync(1), "one worker left");
                        Check.True(await cap.WaitForAsync(() => cap.Sum(TC.WorkerPoolEvents, TC.LabelEvent, TC.EventDisconnected) >= 1), "pool disconnected event");
                        Check.True(cap.Count(TC.WorkerSessionDuration) >= 1, "session duration recorded");
                        Check.True(cap.Spans(TC.SpanWorkerUnregister).Any(), "unregister span");
                    }
                }),

                Case("HeartbeatFailureEviction", "Undeliverable heartbeats record failures, then an eviction event and span", async ct =>
                {
                    using (TelemetryCapture cap = new TelemetryCapture())
                    using (WatsonWsServer server = new WatsonWsServer("localhost", PortAllocator.Next().Websocket, false))
                    using (CancellationTokenSource cts = new CancellationTokenSource())
                    {
                        Settings settings = new Settings();
                        settings.Heartbeat.IntervalMs = 1000;
                        settings.Heartbeat.MaxFailures = 1;
                        LoggingModule logging = new LoggingModule();
                        logging.Settings.EnableConsole = false;

                        Guid guid = Guid.NewGuid();
                        WorkerMetadata worker = new WorkerMetadata(settings, server, logging, guid, "127.0.0.1", 1, cts);

                        bool evicted = await cap.WaitForAsync(() => !worker.Healthy, 10000);
                        cts.Cancel();
                        Check.True(evicted, "worker marked unhealthy");
                        Check.True(cap.Sum(TC.Heartbeats, TC.LabelOutcome, TC.OutcomeFailure) >= 2, "heartbeat failures counted");
                        Check.True(cap.Sum(TC.WorkerPoolEvents, TC.LabelEvent, TC.EventEvicted) >= 1, "eviction event counted");

                        List<Activity> failures = cap.Spans(TC.SpanHeartbeat).Where(a => (a.GetTagItem(TC.AttrWorkerId) as string) == guid.ToString()).ToList();
                        Check.True(failures.Count >= 2 && failures.All(a => a.Status == ActivityStatusCode.Error), "heartbeat failure spans marked error");
                        Activity evict = cap.Spans(TC.SpanWorkerEvict).FirstOrDefault(a => (a.GetTagItem(TC.AttrWorkerId) as string) == guid.ToString());
                        Check.NotNull(evict, "evict span emitted");
                        Check.Equal(ActivityStatusCode.Error, evict.Status, "evict span marked error");
                        Check.Equal(default(ActivitySpanId), evict.ParentSpanId, "evict span is a root span");
                    }
                }),

                Case("ResponseStoreAndCleanup", "Correlation store records stored/duplicate/discarded, expiry, cleanup span, and true timeouts", async ct =>
                {
                    using (TelemetryCapture cap = new TelemetryCapture())
                    {
                        Settings settings = new Settings();
                        LoggingModule logging = new LoggingModule();
                        logging.Settings.EnableConsole = false;

                        using (ResponseService svc = new ResponseService(settings, logging))
                        {
                            // The background cleanup loop runs once immediately, then every 60 seconds.  Wait for that
                            // first pass so it cannot race the explicit CleanupExpired call below.
                            Check.True(await cap.WaitForAsync(() => svc.LastCleanupSuccessUtc > DateTime.MinValue), "initial background cleanup pass ran");

                            WebsocketMessage expired = new WebsocketMessage { ExpirationUtc = DateTime.UtcNow.AddSeconds(-1) };
                            Check.True(svc.AddResponse(expired), "stored");
                            Check.False(svc.AddResponse(expired), "duplicate rejected");
                            Check.False(svc.AddResponse(null), "null discarded");
                            Check.Equal(1L, svc.PendingCount, "one pending");

                            Check.Equal(1, svc.CleanupExpired(), "one expired response evicted");
                            Check.Equal(0L, svc.PendingCount, "none pending");
                            Check.True(svc.LastCleanupSuccessUtc > DateTime.UtcNow.AddMinutes(-1), "last cleanup success recorded");

                            Check.True(cap.Sum(TC.ResponsesReceived, TC.LabelOutcome, TC.OutcomeSuccess) >= 1, "stored counted");
                            Check.True(cap.Sum(TC.ResponsesReceived, TC.LabelOutcome, TC.OutcomeDuplicate) >= 1, "duplicate counted");
                            Check.True(cap.Sum(TC.ResponsesReceived, TC.LabelOutcome, TC.OutcomeDiscarded) >= 1, "discarded counted");
                            Check.True(cap.Sum(TC.ResponsesExpired) >= 1, "expired counted");
                            Check.True(cap.Count(TC.ResponsesCleanupDuration, TC.LabelOutcome, TC.OutcomeSuccess) >= 1, "cleanup duration recorded");
                            Activity cleanup = cap.Spans(TC.SpanResponsesCleanup).LastOrDefault();
                            Check.NotNull(cleanup, "cleanup span");
                            Check.Equal(ActivityStatusCode.Ok, cleanup.Status, "cleanup span ok");

                            bool timedOut = false;
                            try
                            {
                                await svc.WaitForResponse(Guid.NewGuid(), 1000, true, CancellationToken.None);
                            }
                            catch (TimeoutException)
                            {
                                timedOut = true;
                            }

                            Check.True(timedOut, "an elapsed wait surfaces as TimeoutException (answered as 408), not TaskCanceledException");
                        }
                    }
                }),

                Case("ControllerReceiveErrorPath", "A malformed WebSocket message is counted as a receive error, not thrown", async ct =>
                {
                    using (TelemetryCapture cap = new TelemetryCapture())
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    using (WatsonWsClient raw = new WatsonWsClient("localhost", env.WebsocketPort, false))
                    {
                        await raw.StartAsync();
                        Check.True(await env.WaitForWorkerCountAsync(1), "raw client registered");
                        await raw.SendAsync(Encoding.UTF8.GetBytes("this is not json"), System.Net.WebSockets.WebSocketMessageType.Binary);

                        Check.True(await cap.WaitForAsync(() => cap.Sum(TC.Errors, TC.LabelComponent, TC.ComponentController, TC.LabelOperation, TC.OperationReceive) >= 1), "receive error counted");
                        Check.True(cap.Sum(TC.WebsocketMessages, TC.LabelComponent, TC.ComponentController, TC.LabelDirection, TC.DirectionReceived, TC.LabelOutcome, TC.OutcomeError) >= 1, "errored message counted");
                    }
                }),

                Case("RootSpansIgnoreAmbientActivity", "Background/receive spans with no parent never inherit a leaked ambient span", ct =>
                {
                    using (TelemetryCapture cap = new TelemetryCapture())
                    {
                        using (Activity ambient = ConstellationTelemetry.StartActivity("ambient", ActivityKind.Internal))
                        {
                            Check.NotNull(ambient, "ambient span started");
                            Activity root = ConstellationTelemetry.StartActivity("root", ActivityKind.Internal, default(ActivityContext));
                            Check.NotNull(root, "root span started");
                            Check.Equal(default(ActivitySpanId), root.ParentSpanId, "no parent");
                            Check.NotEqual(ambient.TraceId, root.TraceId, "new trace");
                            ConstellationTelemetry.Stop(root);

                            Activity remote = ConstellationTelemetry.StartActivityFromTraceParent("remote", ActivityKind.Server, "not-a-traceparent", null);
                            Check.Equal(default(ActivitySpanId), remote.ParentSpanId, "invalid traceparent yields a root span");
                            ConstellationTelemetry.Stop(remote);
                        }

                        using (Activity source = ConstellationTelemetry.StartActivity("source", ActivityKind.Client))
                        {
                            WebsocketMessage msg = new WebsocketMessage();
                            ConstellationTelemetry.Inject(source, msg);
                            Check.Equal(source.Id, msg.TraceParent, "traceparent injected");
                            Activity child = ConstellationTelemetry.StartActivityFromTraceParent("child", ActivityKind.Server, msg.TraceParent, msg.TraceState);
                            Check.Equal(source.TraceId, child.TraceId, "same trace across the hop");
                            Check.Equal(source.SpanId, child.ParentSpanId, "parented to the injecting span");
                            ConstellationTelemetry.Stop(child);
                        }
                    }
                    return Task.CompletedTask;
                }),

                Case("LabelsAreBounded", "Metric labels never carry ids or paths and methods are normalized", async ct =>
                {
                    using (TelemetryCapture cap = new TelemetryCapture())
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        await env.AddWorkerAsync(1, 1200);
                        Check.True(await env.WaitForWorkerCountAsync(1), "worker connected");
                        for (int i = 0; i < 3; i++)
                            Check.Equal(200, (await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/api/unique/" + Guid.NewGuid())).StatusCode, "request " + i);
                        Check.Equal(TC.HttpMethodOther, ConstellationTelemetry.NormalizeMethod("FROB"), "unknown method normalized to _OTHER");
                        Check.Equal("GET", ConstellationTelemetry.NormalizeMethod("get"), "known method normalized to upper case");
                        Check.True(await cap.WaitForAsync(() => cap.Sum(TC.ProxyRequests, TC.LabelOutcome, TC.OutcomeSuccess) >= 3), "requests counted");
                        cap.RecordObservables();

                        List<CapturedMeasurement> ours = cap.AllMeasurements().Where(m => m.Name.StartsWith("constellation.", StringComparison.Ordinal)).ToList();
                        Check.True(ours.Count > 0, "constellation measurements captured");
                        foreach (CapturedMeasurement m in ours)
                        {
                            foreach (KeyValuePair<string, string> tag in m.Tags)
                            {
                                Check.False(tag.Value != null && _UnboundedValue.IsMatch(tag.Value), "unbounded label " + m.Name + " " + tag.Key + "=" + tag.Value);
                            }
                        }

                        Check.True(ours.Where(m => m.Name == TC.ProxyRequests).All(m => m.Tags.Keys.OrderBy(k => k).SequenceEqual(new[] { TC.LabelHttpMethod, TC.LabelOutcome })), "proxy request label set is exactly method + outcome");
                    }
                })
            };

            return new TestSuiteDescriptor(SuiteId, "Telemetry", cases);
        }

        private static WorkerService NewService()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new WorkerService(new Settings(), logging);
        }

        private static WorkerMetadata NewWorker()
        {
            return new WorkerMetadata { GUID = Guid.NewGuid(), Healthy = true };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> execute)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, execute);
        }
    }
}
