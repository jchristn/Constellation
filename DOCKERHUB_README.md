<div align="center">
  <img src="https://raw.githubusercontent.com/jchristn/constellation/main/assets/logo.png" width="192" height="192">
</div>

# Constellation

**RESTful workload placement and virtualization for exactly-one resource ownership patterns**

This image runs the Constellation **controller**: it receives HTTP requests, pins each resource (the raw URL path) to exactly one worker, forwards the request to that worker over WebSocket, and fails the resource over to a healthy worker when its owner disappears. Source, worker libraries, and documentation: [github.com/jchristn/constellation](https://github.com/jchristn/constellation).

## Use cases

- **SQLite databases**: scale SQLite across nodes while each file has exactly one writer
- **Machine learning models**: keep each customer's model loaded on one worker instead of everywhere
- **Game servers**: one authoritative server per world
- **Media processing**: never transcode the same file twice at once
- **IoT and hardware**: one connection per device, one owner per USB/serial port
- **Wallets and ledgers**: exclusive access to wallet files

## How it works

1. Clients send HTTP requests to the controller (port `8000`).
2. Workers, built with the `Constellation.Worker` NuGet package, connect to the controller over WebSocket (port `8001`).
3. The raw URL path (without query) is the resource key. The first request for a key is assigned round-robin to a healthy worker; later requests go to the same worker.
4. The controller heartbeats every worker. A worker that stops answering is taken out of rotation, and its resources move to healthy workers on their next request.

```
client ──HTTP──> controller ──WebSocket──> worker A  (owns /databases/users.db)
                     │      ──WebSocket──> worker B  (owns /databases/orders.db)
                     └── heartbeats, pinning, failover
```

## Getting started

```bash
docker run -d --name constellation-controller \
  -p 8000:8000 -p 8001:8001 \
  -v ./constellation.json:/app/constellation.json \
  -v ./logs/:/app/logs/ \
  jchristn77/constellation:v1.0.0
```

Start from the ready-made [`docker/constellation.json`](https://github.com/jchristn/constellation/blob/main/docker/constellation.json), which binds all interfaces (`"Hostname": "*"`) so the published ports are reachable. A file generated with defaults binds `localhost` inside the container and only works with `--network host`. The same [`docker/`](https://github.com/jchristn/constellation/tree/main/docker) directory holds the full Compose stack.

```bash
curl http://localhost:8000/                                              # health check
curl -H "x-api-key: constellationadmin" http://localhost:8000/workers   # connected workers
curl -H "x-api-key: constellationadmin" http://localhost:8000/maps      # resource-to-worker map
curl http://localhost:8000/databases/users.db                           # routed to the owning worker
```

Change the default admin API key (`Admin.ApiKeys` in `constellation.json`) before exposing the controller.

## Full stack with dashboard and observability

```bash
git clone https://github.com/jchristn/constellation
cd constellation/docker
docker compose up -d
```

| Service | URL | Credentials |
| --- | --- | --- |
| Controller (REST / WebSocket) | `http://localhost:8000` / `ws://localhost:8001` | admin API key `constellationadmin` |
| Dashboard | `http://localhost:8080` | controller URL plus admin API key |
| Grafana (Constellation dashboard folder) | `http://localhost:3000` | `admin` / `admin` |
| Prometheus | `http://localhost:9090` | none |
| Tempo | `http://localhost:3200` | none |
| Loki | `http://localhost:3100` | none |
| OpenTelemetry Collector (OTLP) | `localhost:4317` (gRPC), `localhost:4318` (HTTP) | none |

These credentials are local-development defaults. For any shared deployment set `GRAFANA_ADMIN_USER` / `GRAFANA_ADMIN_PASSWORD`, and keep Prometheus, Tempo, Loki, and the collector off public interfaces.

## Observability

The controller emits metrics, traces, and structured logs out of the box:

- **Metrics** (Prometheus): proxy requests and latency per stage (`placement`, `dispatch`, `await_response`, `respond`) and outcome (`success`, `no_worker`, `timeout`, ...), placement decisions and failovers, workers by health, heartbeats, WebSocket traffic, errors by type, plus Watson HTTP metrics and .NET runtime metrics.
- **Traces** (OTLP to Tempo or any backend): one trace per request across the controller and the worker that served it.
- **Logs** (OTLP to Loki): worker lifecycle, evictions, timeouts, and failures, linked to their traces.

Configure export under `Telemetry` in `constellation.json` (`OtlpEndpoint`, `PrometheusEnable`, `PrometheusHostname`, `PrometheusPort`, `LokiEnable`, `TraceSamplingRatio`, ...). Inside a container, set `PrometheusHostname` to the container's hostname. See [TELEMETRY.md](https://github.com/jchristn/constellation/blob/main/TELEMETRY.md) for the full catalog, dashboards, and recommended alerts.

## Configuration highlights

| Setting | Default | Meaning |
| --- | --- | --- |
| `Webserver.Port` | `8000` | HTTP port for client requests |
| `Websocket.Port` | `8001` | WebSocket port for workers |
| `Heartbeat.IntervalMs` / `MaxFailures` | `2000` / `5` | Heartbeat cadence and failures before a worker is taken out of rotation |
| `Proxy.TimeoutMs` | `30000` | How long to wait for a worker before answering 408 |
| `Admin.ApiKeyHeader` / `ApiKeys` | `x-api-key` / `constellationadmin` | Admin API authentication |
| `Telemetry.*` | see TELEMETRY.md | Metrics, traces, and logs export |

## License

MIT. See [LICENSE.md](https://github.com/jchristn/constellation/blob/main/LICENSE.md).
