# Change Log

## Current Version

v1.1.2

### Dependencies

- `SyslogLogging` 2.0.13 -> 2.3.1 (Core, Controller, Worker)
- `Watson` 7.2.1 -> 7.2.2 (Controller)
- `Microsoft.Extensions.Logging.Abstractions` 8.0.3 -> 10.0.12 (Controller)
- `Timestamps` 1.0.11 -> 1.0.13 (Core)
- Test projects: `Touchstone.*` 0.1.12 -> 0.2.0, `Microsoft.NET.Test.Sdk` 17.12.0 -> 18.10.1, `xunit.runner.visualstudio` 3.0.0 -> 4.0.0, `NUnit` 4.3.2 -> 5.0.0, `NUnit3TestAdapter` 4.6.0 -> 6.3.0, `Microsoft.Data.Sqlite` 10.0.4 -> 10.0.12

### Tests

- Added a Structured Logging suite (4 cases) covering `ConstellationControllerBase.Logger` against the updated logging abstractions: the no-op default and null handling, worker connect/disconnect records with named template values, the no-worker warning, and containment of a throwing logger
- Added `Test.Shared/CapturingLogger.cs`, an in-memory `ILogger` for assertions on structured log records

## v1.1.1

### Fixes

- Fixed a deadlock between worker placement and worker removal: `WorkerService.RemoveWorker` took the worker and resource-map locks in the opposite order from placement
- `WorkerService.ResourceMap` now returns copies of each resource list, so callers (such as `GET /maps`) can no longer enumerate a list that placement is mutating
- Successful proxied responses now keep the controller's `x-request` and `x-worker` headers; the worker's headers are merged in rather than replacing the collection
- `GET /favicon.ico` is answered by the controller and no longer also proxied and pinned as a resource; `HEAD /favicon.ico` no longer fails with 500 when `assets/favicon.png` is absent
- The forwarded request now carries the caller's content type in `WebsocketMessage.ContentType`
- `x-forwarded-for` is added once (client IP, appended to any existing chain) instead of twice
- `WorkerMetadata.LastMessageUtc` is now updated whenever the controller receives a message from the worker
- `ApiErrorEnum.TokenExpired` now has a specific message

### Documentation

- Added `REST_API.md` and a Postman collection (`assets/postman/Constellation.postman_collection.json`)
- Corrected the README health-check semantics, test worker arguments, data-flow ports, and placement decision names, and added a REST API section
- `docker/constellation.json` now includes an explicit `Admin` block

## v1.1.0

### Observability

- Added built-in telemetry: every Constellation package emits metrics and spans through a BCL `Meter` and `ActivitySource` named `Constellation` (names in `Constellation.Core.Telemetry.TelemetryConstants`), with no exporter dependency and near-zero cost when nothing subscribes
- Controller metrics: proxy requests and duration by outcome and method, per-stage duration and events (`placement`, `dispatch`, `await_response`, `respond`), in-flight requests, placement decisions (`pinned`, `assigned`, `reassigned`, `no_workers`, `no_healthy_workers`), pinned resources, workers by health state, worker lifecycle events and session length, heartbeat outcomes, latency, and last success, response-store activity, expiry, and cleanup, WebSocket message counts and sizes, admin API outcomes, caught errors by type, build info, and configuration gauges
- Worker metrics: requests and duration by outcome, handler duration, connection attempts and events, connected gauge
- Spans for the full proxy path (`constellation proxy`, `stage:*`, `worker request`), worker handling (`worker handle`, `stage:handler`, `stage:reply`), response receipt, admin calls, worker register/unregister, heartbeat failures, eviction, response cleanup, and worker connection attempts, with explicit status and exception events
- W3C trace context now crosses the controller/worker WebSocket hop through the optional `WebsocketMessage.TraceParent` and `TraceState` properties (ignored by older peers), so a request is one trace across processes
- Added `ConstellationControllerBase.Logger` for structured operational logs (worker lifecycle, eviction, proxy failures, cleanup) that correlate with traces
- Added `Settings.Telemetry` (`TelemetrySettings`): the controller server now starts a Radiant 0.1.2 host that subscribes to Constellation and Watson and exports Prometheus (in-process scrape endpoint) and OTLP (traces, logs, metrics), with optional direct Loki export
- `docker/compose.yaml` now includes an OpenTelemetry Collector, Prometheus, Tempo, Loki, and Grafana with healthchecks, ordered startup, and provisioned datasources and five domain dashboards in a Constellation folder (`assets/grafana/`)
- The dashboard home page has an External Services card linking to Grafana, Prometheus, Tempo, and Loki
- Added `TELEMETRY.md` (metric and span catalog, configuration, dashboard map, recommended alerts) and a Telemetry test suite (15 cases) covering every instrumented area, failure paths, and the no-listener path

### Changes and fixes

- Upgraded Watson 6.5.5 to 7.2.1, which provides built-in HTTP metrics and per-request server spans
- Fixed forwarding when the controller binds a wildcard hostname: the proxied URL is now built from the request's `Host` header (Watson 7 composes `Url.Full` from the listener hostname)
- Fixed proxy timeouts being answered as HTTP 500 instead of 408: an elapsed wait now surfaces as `TimeoutException`
- An exception in a worker's message handler is now caught and recorded instead of escaping an `async void` handler and terminating the process
- The controller now disposes its response store (and its cleanup task) on dispose
- `docker/compose.yaml` now uses an explicit project name (`constellation`), bridge networking with published ports, and the published `v1.0.0` image tag; added `docker/update.bat` and `docker/update.sh`
- Added `build-*.sh` equivalents of the `build-*.bat` image build scripts
- Corrected the README installation instructions (`Constellation.Controller` and `Constellation.Worker` packages)

## v1.0.x

- Initial release

## Previous Versions

Notes from previous versions will be shown here.
