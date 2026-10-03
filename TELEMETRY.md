# Constellation Telemetry

Constellation emits metrics, traces, and (in the controller server) structured logs so an on-call engineer can answer two questions from Grafana alone: **where did the time go**, and **what failed**. A slow request resolves to a proxy stage (placement, dispatch, waiting on the worker, responding) and to the specific worker that owned the resource. A failure resolves to an outcome (`no_worker`, `send_failed`, `timeout`, `no_response`, `error`) with an error span and, when it was an exception, the exception type.

The libraries only **emit**; the application **hosts**. `Constellation.Core`, `Constellation.Controller`, and `Constellation.Worker` record into one BCL `Meter` and one `ActivitySource`, both named `Constellation`, and take no dependency on any exporter. When nothing subscribes, every recording is a near-free no-op and every span start returns null. `Constellation.ControllerServer` (the Docker image) is the host: it starts one [Radiant](https://github.com/jchristn/Radiant) host that subscribes to Constellation and Watson and exports to Prometheus, an OTLP collector (Tempo for traces, Loki for logs), or any OTLP backend.

## Contents

1. [Sources and signals](#sources-and-signals)
2. [Subscribing](#subscribing)
3. [Configuration](#configuration)
4. [Metrics catalog](#metrics-catalog)
5. [Watson HTTP metrics](#watson-http-metrics)
6. [Spans catalog](#spans-catalog)
7. [Context propagation](#context-propagation)
8. [Logs](#logs)
9. [Observability stack](#observability-stack)
10. [Dashboard map](#dashboard-map)
11. [Recommended alerts](#recommended-alerts)
12. [Conventions, cardinality, and safety](#conventions-cardinality-and-safety)
13. [Known limitations](#known-limitations)

## Sources and signals

| Name | Kind | Emitted by | Notes |
| --- | --- | --- | --- |
| `Constellation` | `Meter` | Core, Controller, Worker | All application metrics. Version = package version. |
| `Constellation` | `ActivitySource` | Core, Controller, Worker | All application spans. |
| `Watson` | `Meter` + `ActivitySource` | Watson 7.2 (controller HTTP listener) | HTTP server metrics and one server span per request. On by default. |

All names live in `Constellation.Core.Telemetry.TelemetryConstants` (meter/source names, every instrument name, span name, stage, label key, and label value). `Constellation.Core.Telemetry.ConstellationTelemetry` exposes the `Meter`, the `ActivitySource`, and `Version`.

| Signal | Where | Backend |
| --- | --- | --- |
| Metrics | Constellation and Watson meters, plus .NET runtime/process metrics from Radiant | Prometheus (scrapes the controller's in-process endpoint) |
| Traces | Constellation and Watson activity sources | Tempo (via OTLP to the OpenTelemetry Collector) |
| Logs | `ConstellationControllerBase.Logger` (structured `ILogger`) | Loki (via OTLP to the collector, or Radiant's direct Loki export) |

Logs are included because the controller runs background work (a heartbeat loop per worker and a response-store cleanup loop). Every structured log record written inside a span carries its `trace_id` and `span_id`, so Grafana links a log line to its trace and back.

## Subscribing

### The controller server (Docker image)

Nothing to do: `Constellation.ControllerServer` starts a Radiant host from the `Telemetry` section of `constellation.json` (see [Configuration](#configuration)), subscribes it to `Constellation` and to Watson's meter and activity source, and disposes it (with a final flush) on shutdown. If the host cannot start (for example the Prometheus port is already in use) the server logs a warning that includes the cause and keeps running without exporting.

### Your own controller or worker host

The packages emit through the BCL only, so attach whatever collector you already run.

With Radiant:

```csharp
RadiantSettings settings = new RadiantSettings("my-worker");
settings.Sources.AddMeter("Constellation");
settings.Sources.AddActivitySource("Constellation");
settings.Sources.AddMeter("Watson");             // controller hosts only (Watson HTTP listener)
settings.Sources.AddActivitySource("Watson");    // controller hosts only
settings.Otlp.Endpoint = "http://127.0.0.1:4317";

using (RadiantHost host = RadiantHost.Start(settings))
{
    controller.Logger = host.CreateLogger("Constellation.Controller"); // optional structured logs
    await controller.Start();
    // ...
}
```

With the OpenTelemetry SDK:

```csharp
using MeterProvider meters = Sdk.CreateMeterProviderBuilder()
    .AddMeter("Constellation").AddMeter("Watson")
    .AddOtlpExporter().Build();
using TracerProvider traces = Sdk.CreateTracerProviderBuilder()
    .AddSource("Constellation").AddSource("Watson")
    .AddOtlpExporter().Build();
```

In tests, attach a `System.Diagnostics.Metrics.MeterListener` and an `ActivityListener` to the same names. `src/Test.Shared/TelemetryCapture.cs` is a ready-made in-memory collector, and `src/Test.Shared/Suites/TelemetrySuite.cs` shows assertions against it.

A worker process that exports its own telemetry joins the controller's traces automatically (see [Context propagation](#context-propagation)). When it pushes OTLP to the bundled collector, its metrics appear in Prometheus under `job="otel-collector"` with a `service_name` label.

## Configuration

### `constellation.json` → `Telemetry` (controller server)

Modeled on Pneuma's `TelemetrySettings`. Loopback defaults use `127.0.0.1`, never `localhost`.

| Key | Default | Notes |
| --- | --- | --- |
| `Enable` | `true` | Master switch. When false no exporter starts and emission stays a no-op. |
| `ServiceName` | `constellation-controller` | Stamped as `service.name`. Dashboards and Loki queries use this value. |
| `OtlpEnable` | `true` | Push traces, logs, and metrics over OTLP. |
| `OtlpEndpoint` | `http://127.0.0.1:4317` | Collector, Tempo, or any OTLP backend. Docker: `http://otel-collector:4317`. |
| `OtlpProtocol` | `grpc` | `grpc` (port 4317) or `httpprotobuf` (port 4318). |
| `PrometheusEnable` | `true` | Serve an in-process Prometheus scrape endpoint (anonymous; keep it internal). |
| `PrometheusHostname` | `127.0.0.1` | Bind address. In a container use the container's own hostname (Docker: `constellation-controller`). Wildcards are not supported; see [Known limitations](#known-limitations). |
| `PrometheusPort` | `9464` | 1..65535. |
| `LokiEnable` | `false` | Direct OTLP/HTTP log export to Loki, bypassing a collector. |
| `LokiEndpoint` | `http://127.0.0.1:3100/otlp` | Loki 3.x OTLP base endpoint. |
| `LokiTenantId` | `null` | Sent as `X-Scope-OrgID`. |
| `LogsMinimumSeverity` | `2` | 0 (trace) .. 7 (none). 2 = Information. |
| `TraceSamplingRatio` | `1.0` | Parent-based head sampling, 0.0..1.0. |
| `MetricExportIntervalMs` | `15000` | OTLP metric push interval, 1000..300000. Docker uses 60000 because Prometheus scrapes instead. |
| `IncludeRuntimeMetrics` | `true` | .NET runtime (GC, JIT, thread pool) and process metrics. |

### `constellation.json` → `Webserver.Telemetry` (Watson)

Watson's built-in telemetry is enabled by default (`Enable`, `EnableMetrics`, `EnableTraces`, `PropagateContext` all `true`). Leave it on; the server logs a warning at startup if it is disabled. `MeterName`/`ActivitySourceName` default to `Watson`, and the controller server subscribes to whatever they are set to. See Watson's `TELEMETRY.md` for `TrustForwardedHeaders` (off by default; only enable behind a known proxy).

### Library hosts

- `ConstellationControllerBase.Logger` (`Microsoft.Extensions.Logging.ILogger`, default no-op): structured operational logs. Set it before `Start()`.
- Nothing else is required. Emission is always on and costs a few nanoseconds per call when nothing listens.

## Metrics catalog

Instrument names are dotted and use UCUM units. The Prometheus column shows the exported family name (Radiant's in-process exporter, verified against a running server): counters gain `_total`, `s` gains `_seconds`, `By` gains `_bytes`, and dots in label keys become underscores (`http.request.method` → `http_request_method`, `message.type` → `message_type`, `error.type` → `error_type`).

### Proxy pipeline (controller)

| Instrument | Type | Unit | Labels | Prometheus | Description |
| --- | --- | --- | --- | --- | --- |
| `constellation.proxy.requests` | Counter | `{request}` | `outcome`, `http.request.method` | `constellation_proxy_requests_total` | Proxied requests by outcome. |
| `constellation.proxy.request.duration` | Histogram | `s` | `outcome`, `http.request.method` | `constellation_proxy_request_duration_seconds` | End-to-end proxy time (placement through response). |
| `constellation.proxy.stage.duration` | Histogram | `s` | `stage`, `outcome` | `constellation_proxy_stage_duration_seconds` | Per-stage time. |
| `constellation.proxy.stage.events` | Counter | `{event}` | `stage`, `outcome` | `constellation_proxy_stage_events_total` | Per-stage executions. |
| `constellation.proxy.active_requests` | UpDownCounter | `{request}` | `http.request.method` | `constellation_proxy_active_requests` | Requests in flight. |

`outcome` values: `success`, `no_worker`, `send_failed`, `timeout`, `no_response`, `canceled`, `error`. `no_response` is a defensive outcome: the response store raises a timeout rather than returning nothing, so an unanswered request normally records `timeout`. A request the worker answered with a 4xx/5xx is still `success` at this layer (the controller proxied it); the status code is on Watson's HTTP metrics and on the `worker request` span.

`stage` values, in order: `placement` (choose or reuse the owning worker), `dispatch` (serialize and send over WebSocket), `await_response` (the request is queued waiting for the worker; the "queued" state of this pipeline), `respond` (write the worker's answer to the caller).

`http.request.method` is normalized to `GET`, `HEAD`, `POST`, `PUT`, `DELETE`, `PATCH`, `OPTIONS`, `TRACE`, `CONNECT`, or `_OTHER`.

### Placement and pinned resources (controller)

| Instrument | Type | Unit | Labels | Prometheus | Description |
| --- | --- | --- | --- | --- | --- |
| `constellation.placement.decisions` | Counter | `{decision}` | `decision` | `constellation_placement_decisions_total` | `pinned`, `assigned`, `reassigned` (failover), `no_workers`, `no_healthy_workers`. |
| `constellation.placement.duration` | Histogram | `s` | `decision` | `constellation_placement_duration_seconds` | Placement decision time (includes lock wait). |
| `constellation.resources.mapped` | ObservableGauge | `{resource}` | none | `constellation_resources_mapped` | Pinned resources currently mapped. |

### Worker pool and heartbeats (controller background job)

| Instrument | Type | Unit | Labels | Prometheus | Description |
| --- | --- | --- | --- | --- | --- |
| `constellation.workers` | ObservableGauge | `{worker}` | `state` (`healthy`, `unhealthy`) | `constellation_workers` | Registered workers. |
| `constellation.workers.events` | Counter | `{event}` | `event` (`connected`, `disconnected`, `evicted`, `recovered`) | `constellation_workers_events_total` | Pool lifecycle. `evicted` = heartbeat limit exceeded. |
| `constellation.workers.session.duration` | Histogram | `s` | none | `constellation_workers_session_duration_seconds` | Registration to disconnection. |
| `constellation.heartbeats` | Counter | `{heartbeat}` | `outcome` (`success`, `failure`, `error`) | `constellation_heartbeats_total` | Heartbeats sent. |
| `constellation.heartbeat.duration` | Histogram | `s` | `outcome` | `constellation_heartbeat_duration_seconds` | Heartbeat send time. |
| `constellation.heartbeat.last_success` | ObservableGauge | `s` | none | `constellation_heartbeat_last_success_seconds` | Unix time of the most recent successful heartbeat. Survives worker removal. |

### Response correlation store (controller)

| Instrument | Type | Unit | Labels | Prometheus | Description |
| --- | --- | --- | --- | --- | --- |
| `constellation.responses.pending` | ObservableGauge | `{response}` | none | `constellation_responses_pending` | Responses waiting for pickup. Growth means responses arrive after their request timed out. |
| `constellation.responses.received` | Counter | `{response}` | `outcome` (`success`, `duplicate`, `discarded`) | `constellation_responses_received_total` | Responses offered to the store. |
| `constellation.responses.expired` | Counter | `{response}` | none | `constellation_responses_expired_total` | Orphaned responses evicted by cleanup. |
| `constellation.responses.cleanup.duration` | Histogram | `s` | `outcome` | `constellation_responses_cleanup_duration_seconds` | Cleanup pass time (every 60 s). |
| `constellation.responses.cleanup.last_success` | ObservableGauge | `s` | none | `constellation_responses_cleanup_last_success_seconds` | Unix time of the last successful cleanup pass. |

### WebSocket messaging (controller and worker)

| Instrument | Type | Unit | Labels | Prometheus | Description |
| --- | --- | --- | --- | --- | --- |
| `constellation.websocket.messages` | Counter | `{message}` | `component`, `direction`, `message.type`, `outcome` | `constellation_websocket_messages_total` | Messages by `controller`/`worker`, `sent`/`received`, `request`/`response`/`heartbeat`/`unknown`, and `success`/`failure`/`discarded`/`duplicate`/`error`. |
| `constellation.websocket.message.size` | Histogram | `By` | `component`, `direction`, `message.type` | `constellation_websocket_message_size_bytes` | Message size. |

### Worker processes (`Constellation.Worker`)

| Instrument | Type | Unit | Labels | Prometheus | Description |
| --- | --- | --- | --- | --- | --- |
| `constellation.worker.requests` | Counter | `{request}` | `outcome` (`success`, `no_response`, `send_failed`, `canceled`, `error`) | `constellation_worker_requests_total` | Requests handled. |
| `constellation.worker.request.duration` | Histogram | `s` | `outcome` | `constellation_worker_request_duration_seconds` | Deserialize + handler + reply. |
| `constellation.worker.handler.duration` | Histogram | `s` | `outcome` | `constellation_worker_handler_duration_seconds` | Your `OnRequestReceived` only. |
| `constellation.worker.connection.attempts` | Counter | `{attempt}` | `outcome` (`success`, `failure`, `canceled`, `error`) | `constellation_worker_connection_attempts_total` | Connection attempts to the controller. |
| `constellation.worker.connection.events` | Counter | `{event}` | `event` (`connected`, `disconnected`) | `constellation_worker_connection_events_total` | Connection state changes. |
| `constellation.worker.connected` | ObservableGauge | `{worker}` | none | `constellation_worker_connected` | Worker instances in this process that are connected. |

### Admin API, errors, build, and configuration

| Instrument | Type | Unit | Labels | Prometheus | Description |
| --- | --- | --- | --- | --- | --- |
| `constellation.admin.requests` | Counter | `{request}` | `operation` (`list_workers`, `list_maps`, `authenticate`), `outcome` (`success`, `unauthorized`, `error`) | `constellation_admin_requests_total` | Admin API calls. Invalid API keys count as `authenticate`/`unauthorized`. |
| `constellation.errors` | Counter | `{error}` | `component`, `operation` (`proxy`, `receive`, `heartbeat`, `cleanup`, `connect`, `handle`, `lifecycle`), `error.type` | `constellation_errors_total` | Caught exceptions; `error.type` is the exception's full type name. |
| `constellation.build.info` | ObservableGauge | none | `version`, `runtime` | `constellation_build_info` | Always 1. |
| `constellation.config.heartbeat.interval` | ObservableGauge | `s` | none | `constellation_config_heartbeat_interval_seconds` | `Heartbeat.IntervalMs` / 1000. |
| `constellation.config.heartbeat.max_failures` | ObservableGauge | `{failure}` | none | `constellation_config_heartbeat_max_failures` | `Heartbeat.MaxFailures`. |
| `constellation.config.proxy.timeout` | ObservableGauge | `s` | none | `constellation_config_proxy_timeout_seconds` | `Proxy.TimeoutMs` / 1000. |

Controller gauges sum (counts) or take the maximum (timestamps, configuration) across every controller instance in the process. Gauges with no registered controller or worker report nothing rather than zero.

### Runtime and process

With `IncludeRuntimeMetrics`, Radiant adds the OpenTelemetry .NET runtime instrumentation (`dotnet_gc_*`, `dotnet_jit_*`, `dotnet_thread_pool_*`, `dotnet_exceptions_total`, `dotnet_process_cpu_time_seconds_total`, `dotnet_process_memory_working_set_bytes`, ...) and process metrics (`process_memory_usage_bytes`, `process_thread_count`, `process_uptime_seconds`).

## Watson HTTP metrics

Watson 7.2 instruments the HTTP layer itself; Constellation does not duplicate it. Key families: `http_server_request_duration_seconds` (labels `http_request_method`, `http_response_status_code`), `http_server_active_requests`, `http_server_request_body_size_bytes`, `http_server_response_body_size_bytes`, `watson_server_up`, `watson_server_uptime_seconds`, `watson_server_connections_active`, `watson_server_connections_total`, `watson_server_received_bytes_total`, `watson_server_sent_bytes_total`, `watson_server_exceptions_total`. Constellation serves every proxied request from Watson's default route, so these series carry no `http_route` label (the resource key is a raw path and is intentionally kept off metrics). See Watson's `TELEMETRY.md` for the full list.

## Spans catalog

| Span | Kind | Emitted by | Parent | Key attributes | Status |
| --- | --- | --- | --- | --- | --- |
| `GET /...` (Watson) | Server | Watson | inbound `traceparent`, or root | `http.request.method`, `http.response.status_code`, `url.path`, `client.address` | Error on 5xx |
| `constellation proxy` | Internal | Controller | Watson server span | `constellation.resource`, `constellation.worker.id`, `constellation.message.id`, `constellation.placement.decision`, `constellation.outcome`, `http.response.status_code` | Ok on `success`, Error otherwise |
| `stage:placement` | Internal | Controller | `constellation proxy` | `constellation.placement.decision`, `constellation.worker.id` | Error on `no_worker` |
| `worker request` | Client | Controller | `constellation proxy` | `constellation.worker.id`, `constellation.message.id`, `messaging.system=websocket`, `server.address`, `server.port`, `http.response.status_code` | Error on `send_failed`, `timeout`, `no_response`, exception |
| `stage:dispatch` | Internal | Controller | `worker request` | | Error on `send_failed` |
| `stage:await_response` | Internal | Controller | `worker request` | | Error on `timeout`, `no_response`, `canceled` |
| `worker handle` | Server | Worker | `worker request` (remote, via `TraceParent`) | `constellation.message.id`, `constellation.worker.id`, `constellation.resource`, `http.request.method`, `http.response.status_code` | Error on handler exception, `no_response`, `send_failed` |
| `stage:handler` | Internal | Worker | `worker handle` | | Error on exception or null response |
| `stage:reply` | Producer | Worker | `worker handle` | | Error on send failure |
| `websocket receive response` | Consumer | Controller | `stage:reply` (remote, via `TraceParent`) | `constellation.worker.id`, `constellation.message.id` | Error on `duplicate` |
| `stage:respond` | Internal | Controller | `constellation proxy` | `http.response.status_code` | Error on exception |
| `admin list_workers`, `admin list_maps` | Internal | Controller | Watson server span | `constellation.count` | Error on exception |
| `worker register` / `worker unregister` | Internal | Controller | root | `constellation.worker.id` | Error on exception |
| `worker heartbeat` | Producer | Controller | root | `constellation.worker.id`, `constellation.heartbeat.failures` | Always Error (emitted only for failed or errored heartbeats) |
| `worker evict` | Internal | Controller | root | `constellation.worker.id`, `constellation.heartbeat.failures` | Error |
| `responses cleanup` | Internal | Controller | root | `constellation.count` (evicted) | Ok, or Error with exception |
| `worker connect` | Client | Worker | root | `constellation.worker.id`, `server.address`, `server.port` | Ok, or Error on failure/exception |

Exceptions are attached as an `exception` event (`exception.type`, `exception.message`, `exception.stacktrace`) and `error.type` is set. One proxied request is one trace:

```
GET /api/users                       (Watson, server)
└─ constellation proxy
   ├─ stage:placement
   ├─ worker request                 (client)
   │  ├─ stage:dispatch
   │  ├─ stage:await_response
   │  └─ worker handle               (server, worker process)
   │     ├─ stage:handler            (your OnRequestReceived; your own spans nest here)
   │     └─ stage:reply
   │        └─ websocket receive response   (consumer, controller)
   └─ stage:respond
```

Successful heartbeats (one every `Heartbeat.IntervalMs` per worker) emit metrics only; spans for them would be noise. Background spans (`worker register`, `worker heartbeat`, `worker evict`, `responses cleanup`, `worker connect`) are always roots, even when the callback runs on a captured execution context that still carries an older span.

## Context propagation

- **HTTP → controller:** Watson adopts an inbound W3C `traceparent`/`tracestate` (`Webserver.Telemetry.PropagateContext`), so a caller's trace continues into Constellation.
- **Controller → worker:** the `worker request` span's context is written to `WebsocketMessage.TraceParent` / `TraceState`. The worker continues the trace in `worker handle`.
- **Worker → controller:** the worker writes `stage:reply`'s context into the response message, and the controller's `websocket receive response` span continues it.
- **Worker → downstream:** `Activity.Current` is the `stage:handler` span while your `OnRequestReceived` runs, so `HttpClient` and other instrumented clients propagate automatically.

`TraceParent` and `TraceState` are optional JSON properties. A peer built before this release ignores them, and a message without them starts a new root trace on the receiving side.

## Logs

The controller writes structured records through `ConstellationControllerBase.Logger` (in addition to the existing SyslogLogging output). Records written inside a span carry `trace_id`/`span_id`.

| Level | Event | Fields |
| --- | --- | --- |
| Information | Worker connected / disconnected (with session length) | `WorkerId`, `WorkerCount`, `SessionSeconds` |
| Information | Worker recovered after a successful heartbeat | `WorkerId` |
| Information | Orphaned responses evicted by cleanup | `ExpiredCount` |
| Warning | Worker exceeded the heartbeat failure limit and was marked unhealthy | `WorkerId` |
| Warning | Heartbeat raised an exception | `WorkerId`, exception |
| Warning | No healthy worker for a resource | `Resource`, `Decision` |
| Warning | Request could not be sent to the worker | `Resource`, `WorkerId` |
| Warning | Timed out waiting for the worker | `TimeoutMs`, `WorkerId`, `Resource` |
| Warning | Admin request with an invalid API key | `Path` |
| Warning | Message from an unknown worker discarded, or a message failed to process | `WorkerId`, exception |
| Error | Unhandled exception while proxying | `Method`, `Path`, exception |

In Loki: `{service_name="constellation-controller"}`. Warnings and errors: `{service_name="constellation-controller"} | severity_text=~"(?i)warn.*|error|critical|fatal"`.

## Observability stack

`docker/compose.yaml` (project name `constellation`) brings everything up with `docker compose up -d` from `docker/`:

| Service | Image | Host port | Role |
| --- | --- | --- | --- |
| `controller` | `jchristn77/constellation:v1.0.0` | 8000 (REST), 8001 (WebSocket) | Constellation; Prometheus endpoint on 9464 inside the network only |
| `dashboard` | built from `dashboard/` | 8080 | Product dashboard (External Services card on the home page) |
| `otel-collector` | `otel/opentelemetry-collector-contrib:0.109.0` | 4317, 4318 | OTLP in; traces → Tempo, logs → Loki, other services' metrics → `:8889` |
| `prometheus` | `prom/prometheus:v3.5.4` | 9090 | Scrapes `constellation-controller:9464` and `otel-collector:8889` |
| `tempo` | `grafana/tempo:2.6.1` | 3200 | Traces |
| `loki` | `grafana/loki:3.2.1` | 3100 | Logs |
| `grafana` | `grafana/grafana-oss:13.0.2` | 3000 | Dashboards; `admin` / `admin` locally |

```
controller ──OTLP──> otel-collector ──> tempo   (traces)
     │                     ├──────────> loki    (logs)
     │                     └── :8889 <─ prometheus (metrics pushed by workers/other services)
     └──── :9464 <──────────────────── prometheus (controller metrics, scraped)
grafana ──> prometheus, tempo, loki   (trace <-> log links by trace_id)
```

Every HTTP service has a curl/wget healthcheck on `127.0.0.1` (`interval: 5s`, `retries: 2`). Grafana starts only after Prometheus, Tempo, and Loki are healthy; the collector after Tempo and Loki; Prometheus after the controller. The collector image is distroless (no shell), so it has no healthcheck and dependents use `service_started`.

Grafana is provisioned as code: `docker/grafana/provisioning/datasources/constellation-datasources.yaml` (UIDs `prometheus`, `tempo`, `loki`, with Tempo→Loki and Loki→Tempo links) and `docker/grafana/provisioning/dashboards/constellation-dashboards.yaml`, which loads `assets/grafana/*.json` into the **Constellation** folder.

**Before sharing a deployment:** set `GRAFANA_ADMIN_USER` / `GRAFANA_ADMIN_PASSWORD` (environment or `.env`; never commit a real password), and do not publish Prometheus, Tempo, Loki, the collector, or the 9464 scrape endpoint on a public interface. None of them authenticate.

## Dashboard map

| Dashboard | UID | Answers |
| --- | --- | --- |
| Constellation Overview | `constellation-overview` | Is it up? Traffic, 5xx ratio, proxy success ratio and p95, healthy/unhealthy workers, heartbeat age, error rate, recent warning/error logs. Start here. |
| Constellation HTTP | `constellation-http` | Watson HTTP layer: rate by status and method, latency quantiles, p95 by status, payload sizes, throughput, connections, server exceptions. |
| Constellation Proxy and Placement | `constellation-proxy` | Where proxy time goes (p95 per stage), stage failures by outcome, timeouts, in-flight and pending responses, placement decisions and failovers, recent failed and slowest traces (Tempo). |
| Constellation Workers and Heartbeats | `constellation-workers` | Pool by state, evictions and disconnects, session length, heartbeat outcomes and latency, last-success ages, WebSocket messages, worker-process request/handler latency and connection attempts, lifecycle logs. |
| Constellation Runtime and Errors | `constellation-runtime` | Errors by component/operation/type, admin API outcomes, cleanup duration, configuration and build, .NET memory/GC/CPU/thread pool. |

Typical path: the Overview's proxy success ratio drops → Proxy and Placement shows `await_response` p95 climbing and `timeout` outcomes → the failed-traces table opens one trace, whose `worker request` span names the worker → Workers and Heartbeats shows that worker's evictions, and the trace's logs show the timeout warning.

## Recommended alerts

```yaml
groups:
  - name: constellation
    rules:
      - alert: ConstellationDown
        expr: absent(watson_server_up == 1)
        for: 1m
        annotations: { summary: "Constellation controller is not up (or not scraped)" }

      - alert: ConstellationNoHealthyWorkers
        expr: sum(constellation_workers{state="healthy"}) == 0
        for: 1m
        annotations: { summary: "No healthy workers; every new resource request returns 502" }

      - alert: ConstellationUnhealthyWorkers
        expr: sum(constellation_workers{state="unhealthy"}) > 0
        for: 5m
        annotations: { summary: "Workers are failing heartbeats and are out of rotation" }

      - alert: ConstellationProxySuccessLow
        expr: |
          sum(rate(constellation_proxy_requests_total{outcome="success"}[5m]))
            / clamp_min(sum(rate(constellation_proxy_requests_total[5m])), 1e-9) < 0.99
          and sum(rate(constellation_proxy_requests_total[5m])) > 0.1
        for: 5m
        annotations: { summary: "Less than 99% of proxied requests succeed" }

      - alert: ConstellationProxyTimeouts
        expr: sum(rate(constellation_proxy_requests_total{outcome="timeout"}[5m])) > 0
        for: 5m
        annotations: { summary: "Workers are not answering within Proxy.TimeoutMs" }

      - alert: ConstellationAwaitResponseSlow
        expr: |
          histogram_quantile(0.95, sum by (le) (rate(constellation_proxy_stage_duration_seconds_bucket{stage="await_response"}[5m])))
            > 0.5 * max(constellation_config_proxy_timeout_seconds)
        for: 10m
        annotations: { summary: "Worker response p95 is above half the proxy timeout" }

      - alert: ConstellationHeartbeatStale
        expr: (time() - max(constellation_heartbeat_last_success_seconds)) > 3 * max(constellation_config_heartbeat_interval_seconds) and sum(constellation_workers) > 0
        for: 2m
        annotations: { summary: "No successful heartbeat recently while workers are registered" }

      - alert: ConstellationWorkerEvictions
        expr: sum(increase(constellation_workers_events_total{event="evicted"}[15m])) > 0
        annotations: { summary: "A worker exceeded the heartbeat failure limit" }

      - alert: ConstellationPendingResponsesGrowing
        expr: sum(constellation_responses_pending) > 100
        for: 10m
        annotations: { summary: "Responses are arriving after their requests timed out" }

      - alert: ConstellationCleanupStale
        expr: (time() - max(constellation_responses_cleanup_last_success_seconds)) > 300
        for: 5m
        annotations: { summary: "Response-store cleanup has not completed in 5 minutes" }

      - alert: ConstellationErrors
        expr: sum by (component, operation) (rate(constellation_errors_total[5m])) > 0.1
        for: 5m
        annotations: { summary: "Caught errors in {{ $labels.component }}/{{ $labels.operation }}" }

      - alert: ConstellationHttp5xx
        expr: |
          sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]))
            / clamp_min(sum(rate(http_server_request_duration_seconds_count[5m])), 1e-9) > 0.05
        for: 5m
        annotations: { summary: "More than 5% of HTTP responses are 5xx" }
```

## Conventions, cardinality, and safety

- OpenTelemetry semantic-convention keys where they exist (`http.request.method`, `http.response.status_code`, `error.type`, `server.address`, `messaging.system`); product families are prefixed `constellation.`.
- Durations are seconds; sizes are bytes. Quantiles are computed in Prometheus from histogram buckets, never in-process.
- **Metric labels are bounded.** Resource keys (raw URL paths), worker GUIDs, and message GUIDs appear only as span attributes. HTTP methods are normalized. `error.type` is an exception type name. A test (`LabelsAreBounded`) fails if any Constellation metric label looks like a GUID or a path.
- **No secrets, payloads, or headers** are recorded. Admin API keys are never logged or tagged; request and response bodies never leave the proxy path.
- **Best-effort.** Every recording and span operation is wrapped so a failing or throwing listener cannot affect request handling (covered by the `ThrowingListenerContained` test), and a telemetry host that fails to start leaves the server running.
- **Cost.** Unobserved: a few nanoseconds per call, no allocation. Observed: tens of nanoseconds per counter/histogram, about a microsecond per span. Volume is dominated by one proxy trace (about 10 spans) per request; reduce with `TraceSamplingRatio`.

## Known limitations

- **Prometheus bind address.** Radiant 0.1.2 (through `OpenTelemetry.Exporter.Prometheus.HttpListener`) rejects `*` and `+` (URI parse failure) and `0.0.0.0` (unsupported), although Radiant's README documents `*`/`+`. In a container, bind the container's hostname instead (`PrometheusHostname: "constellation-controller"` plus `hostname:` in compose).
- **Prometheus 3 scrape format.** Prometheus 3 rejects the exporter's OpenMetrics output (UNIT metadata vs. suffixed names), so `docker/prometheus.yaml` pins `scrape_protocols: ['PrometheusText0.0.4']` for the controller job.
- **Worker metrics require the worker host to export.** `Constellation.Worker` emits; a worker process must run its own collector (for example a Radiant host pushing OTLP to the bundled collector on port 4317) for its metrics and spans to appear.
- **Watson route label.** All proxied traffic uses Watson's default route, so Watson's HTTP metrics are not split by route; use the Constellation proxy metrics and spans for per-stage and per-outcome detail.
