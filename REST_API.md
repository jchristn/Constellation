# Constellation REST API

This document describes the HTTP surface of the Constellation controller exactly as it is implemented in `ConstellationControllerBase` (`src/Constellation.Controller/ConstellationControllerBase.cs`). Everything the controller does over HTTP happens in a single default route, so the "API" is really an ordered set of checks applied to every request: a health page, a favicon, two administrative endpoints protected by an API key, and a catch-all reverse proxy that forwards everything else to the worker that owns the requested resource.

A ready-to-import Postman collection is available at [`assets/postman/Constellation.postman_collection.json`](assets/postman/Constellation.postman_collection.json).

## Contents

1. [Ports and base URL](#ports-and-base-url)
2. [Request evaluation order](#request-evaluation-order)
3. [Conventions](#conventions)
4. [Authentication](#authentication)
5. [Errors](#errors)
6. [Health endpoints](#health-endpoints)
7. [Administrative endpoints](#administrative-endpoints)
8. [Proxy (catch-all)](#proxy-catch-all)
9. [Tracing and metrics](#tracing-and-metrics)
10. [Configuration reference](#configuration-reference)
11. [Known behaviors and caveats](#known-behaviors-and-caveats)

## Ports and base URL

| Port | Protocol | Purpose | Configured by |
| --- | --- | --- | --- |
| `8000` | HTTP | Client requests: the REST surface described here | `Webserver.Hostname`, `Webserver.Port` |
| `8001` | WebSocket | Worker connections (workers dial in; this is not a client API) | `Websocket.Hostnames`, `Websocket.Port`, `Websocket.Ssl` |
| `9464` | HTTP | Prometheus scrape endpoint served by the controller server's telemetry host (separate listener, not part of this API) | `Telemetry.PrometheusHostname`, `Telemetry.PrometheusPort` |

The ports above are the values in `docker/constellation.json` and the ones published by `docker/compose.yaml` (`8000:8000`, `8001:8001`; `9464` is reachable only inside the compose network). All examples below use `http://localhost:8000`. When `Webserver.Ssl.Enable` is `true`, use `https`.

## Request evaluation order

Every request, whatever its method or path, enters the same handler. The handler first adds an `x-request` response header containing a newly generated GUID, then evaluates these checks in order:

| Step | Condition | Result |
| --- | --- | --- |
| 1 | `HEAD /` | `200`, `text/html`, empty body. Done. |
| 2 | `GET /` | `200`, `text/html`, HTML health page. Done. |
| 3 | `HEAD /favicon.ico` | `200`, `image/png`, no body. Done. |
| 4 | `GET /favicon.ico` | `200`, `image/png`, the contents of `assets/favicon.png` if that file exists, otherwise an empty body. Done. |
| 5 | The API key header (default `x-api-key`) is present and its value is **not** one of `Admin.ApiKeys` | `401` with `AuthorizationFailed`, **on any path and any method**. Done. |
| 6 | The API key header is present and valid, the method is `GET`, and the path is exactly `/workers` | Worker list. Done. |
| 7 | The API key header is present and valid, the method is `GET`, and the path is exactly `/maps` | Resource map. Done. |
| 8 | Anything else | Proxied to a worker (see [Proxy](#proxy-catch-all)). |

Path comparisons use the raw URL path without the query string (`RawWithoutQuery`) and are exact, case-sensitive string matches. `/workers/` and `/Workers` are not admin paths and are proxied. Admin paths reached without an API key, or with a valid key but a method other than `GET` (for example `POST /workers`), fall through to the proxy.

## Conventions

### Content types

| Content type | Used for |
| --- | --- |
| `application/json` | Default for every response. A pre-routing handler sets it before the default route runs, so error bodies, admin responses, and proxied responses whose worker did not set a content type are all labeled `application/json`. |
| `text/html` | `HEAD /` and `GET /` |
| `image/png` | `HEAD /favicon.ico` and `GET /favicon.ico` |
| Worker-defined | Proxied responses where the worker set `ContentType` on its response message |

### JSON serialization

Controller-generated JSON (admin responses and error bodies) is produced by `Constellation.Core.Serialization.Serializer`:

- Property names are PascalCase, exactly as declared on the C# classes (`GUID`, `Ip`, `StatusCode`).
- Properties whose value is `null` are omitted.
- Enums are written as strings (`"BadGateway"`, not `3`).
- `DateTime` values are written as `yyyy-MM-ddTHH:mm:ss.ffffffZ` (for example `2026-10-03T14:27:36.905899Z`).
- Bodies are indented with two spaces, except the generic `500` body produced by an unhandled exception, which is written on a single line.

### Response headers

| Header | Value | When |
| --- | --- | --- |
| `x-request` | A new GUID per request | Added at the start of every request. Present on every response: health, admin, error, and proxied. |
| `x-worker` | GUID of the worker selected for the resource | Added once a worker is selected. Present on every response after selection: successful proxied responses and the `502`, `408`, and `500` proxy errors that occur after selection. Not present on health, admin, `401`, or no-worker `502` responses. |
| Configured defaults | Whatever `Webserver.Headers.DefaultHeaders` contains | Every response. `docker/constellation.json` configures permissive CORS headers (`Access-Control-Allow-Origin: *`, `Access-Control-Allow-Methods`, `Access-Control-Allow-Headers: *`, `Access-Control-Expose-Headers`), `Cache-Control: no-cache`, `Connection: close`, and also `Accept`, `Accept-Language`, `Accept-Charset`, and `Host` (these last four are emitted as response headers because they are listed in the defaults). |

When a worker answers, the controller merges the headers from the worker's response message onto its own response. A worker header replaces a controller header of the same name, except `x-request` and `x-worker`: those are always the controller's values, and any worker-supplied value for them is ignored. The client therefore sees the configured default headers, `x-request`, `x-worker`, and the worker's headers.

## Authentication

Only the two administrative endpoints require authentication. The health endpoints and the proxy are anonymous at the controller (workers may implement their own authentication by inspecting forwarded headers).

| Setting | Default | Notes |
| --- | --- | --- |
| `Admin.ApiKeyHeader` | `x-api-key` | Name of the request header that carries the key. Cannot be null or empty. |
| `Admin.ApiKeys` | `[ "constellationadmin" ]` | List of accepted keys. At least one key is required. |

`docker/constellation.json` includes an explicit `Admin` block with these defaults:

```json
"Admin": {
  "ApiKeyHeader": "x-api-key",
  "ApiKeys": [ "constellationadmin" ]
}
```

Change the key before exposing the controller.

How the header is evaluated:

| API key header | Method and path | Result |
| --- | --- | --- |
| Absent | any | Not an admin request. `GET /workers` and `GET /maps` are proxied like any other path. |
| Present, invalid | **any** (including paths that would otherwise be proxied) | `401 AuthorizationFailed` |
| Present, valid | `GET /workers` or `GET /maps` | Admin response |
| Present, valid | anything else | Proxied. The API key header is forwarded to the worker along with every other request header. |

## Errors

### ApiErrorResponse

Errors generated by the controller (as opposed to errors returned by a worker) use `Constellation.Core.ApiErrorResponse`:

| Property | Type | Notes |
| --- | --- | --- |
| `Error` | string (`ApiErrorEnum`) | Error code. |
| `Message` | string | Read-only, derived from `Error`. |
| `StatusCode` | integer | Read-only, derived from `Error`. |
| `Context` | object | Optional. Omitted when null. The controller never sets it. |
| `Description` | string | Optional. Omitted when null. Request-specific detail. |

```json
{
  "Error": "BadGateway",
  "Message": "Your request is unable to be serviced as there are no origin servers available.",
  "StatusCode": 502,
  "Description": "No workers available for resource /my/resource."
}
```

The HTTP status of the response is set explicitly by the controller and matches the `StatusCode` property in every case the controller produces.

### ApiErrorEnum

All values of `ApiErrorEnum`, with the `Message` and `StatusCode` they produce. The controller's REST surface emits only the four values marked in the last column; the rest exist in `Constellation.Core` for use by hosts and workers.

| `Error` | `StatusCode` | `Message` | Emitted by controller |
| --- | --- | --- | --- |
| `AuthenticationFailed` | 401 | Your authentication material was not accepted. | |
| `AuthorizationFailed` | 401 | Your authentication material was accepted, but you are not authorized to perform this request. | Yes (invalid API key) |
| `BadGateway` | 502 | Your request is unable to be serviced as there are no origin servers available. | Yes (no worker, or send to worker failed) |
| `BadRequest` | 400 | We were unable to discern your request.  Please check your URL, query, and request body. | |
| `Conflict` | 409 | Operation failed as it would create a conflict with an existing resource. | |
| `DeserializationError` | 400 | Your request body was invalid and could not be deserialized. | |
| `Inactive` | 401 | Your account, credentials, or the requested resource are marked as inactive. | |
| `InternalError` | 500 | An internal error has been encountered. | Yes (no response, unhandled exception) |
| `InvalidEmail` | 400 | An invalid email address has been supplied. | |
| `InvalidRange` | 400 | An invalid range has been supplied and cannot be fulfilled. | |
| `InUse` | 409 | The requested resource is in use. | |
| `NotEmpty` | 400 | The requested resource is not empty. | |
| `NotFound` | 404 | The requested resource was not found. | |
| `RequestBodyMissing` | 400 | A request body is required for this operation. | |
| `RequiredPropertiesMissing` | 400 | A required property was missing from the request. | |
| `Timeout` | 408 | The request was not completed within the specified timeout interval. | Yes (worker did not answer in time) |
| `TokenExpired` | 401 | Your authentication token has expired. | |
| `TooLarge` | 413 | The size of your request exceeds the maximum allowed by this server. | |

### Error responses produced by the controller

| Status | `Error` | `Description` | Cause |
| --- | --- | --- | --- |
| 401 | `AuthorizationFailed` | (none) | API key header present with a value not in `Admin.ApiKeys` |
| 502 | `BadGateway` | `No workers available for resource {resource}.` | No workers registered, or none healthy |
| 502 | `BadGateway` | `Unable to proxy request for resource {resource}.` | The WebSocket send to the selected worker failed |
| 408 | `Timeout` | (none) | The worker did not answer within `Proxy.TimeoutMs` |
| 500 | `InternalError` | `No response received.` | The response wait completed without a message (defensive; the current response store throws a timeout instead of returning nothing, so this path is not normally reached) |
| 500 | `InternalError` | The exception message | Any unhandled exception in the handler. Body is single-line JSON. |

Example bodies:

```json
{
  "Error": "AuthorizationFailed",
  "Message": "Your authentication material was accepted, but you are not authorized to perform this request.",
  "StatusCode": 401
}
```

```json
{
  "Error": "BadGateway",
  "Message": "Your request is unable to be serviced as there are no origin servers available.",
  "StatusCode": 502,
  "Description": "Unable to proxy request for resource /databases/users.db."
}
```

```json
{
  "Error": "Timeout",
  "Message": "The request was not completed within the specified timeout interval.",
  "StatusCode": 408
}
```

```json
{
  "Error": "InternalError",
  "Message": "An internal error has been encountered.",
  "StatusCode": 500,
  "Description": "No response received."
}
```

An unhandled exception produces single-line JSON whose `Description` is the exception message (the message below is illustrative):

```json
{"Error":"InternalError","Message":"An internal error has been encountered.","StatusCode":500,"Description":"Object reference not set to an instance of an object."}
```

Responses that a worker returns with a 4xx or 5xx status are passed through unchanged; their bodies are whatever the worker wrote and are not `ApiErrorResponse` objects unless the worker chose to use that class.

## Health endpoints

### HEAD /

Lightweight liveness check. Always answered by the controller; never proxied.

| | |
| --- | --- |
| Status | `200` |
| Content type | `text/html` |
| Body | none |
| Headers | `x-request` |

```bash
curl -I http://localhost:8000/
```

### GET /

Returns a static HTML page titled "Node is Operational". This is the endpoint used by the compose healthcheck (`curl -f http://127.0.0.1:8000/`). Query strings are ignored for matching, so `GET /?probe=1` is also the health page.

| | |
| --- | --- |
| Status | `200` |
| Content type | `text/html` |
| Body | The `Constants.HtmlHomepage` page (ASCII-art logo, "Your node is operational") |
| Headers | `x-request` |

```bash
curl http://localhost:8000/
```

### HEAD /favicon.ico

Always answered by the controller; never proxied or pinned. The file is not read.

| | |
| --- | --- |
| Status | `200` |
| Content type | `image/png` |
| Body | none |
| Headers | `x-request` |

```bash
curl -I http://localhost:8000/favicon.ico
```

### GET /favicon.ico

Always answered by the controller; never proxied or pinned.

| | |
| --- | --- |
| Status | `200` |
| Content type | `image/png` |
| Body | The contents of `assets/favicon.png`, resolved relative to the controller's working directory, when that file exists; otherwise empty. The file is not shipped in the repository or the Docker image, so a default installation returns an empty body. |
| Headers | `x-request` |

```bash
curl -i http://localhost:8000/favicon.ico
```

## Administrative endpoints

Both endpoints require the API key header (see [Authentication](#authentication)) and the `GET` method. Both respond `200 application/json` with an indented body and the `x-request` header.

### GET /workers

Lists every registered worker, healthy or not, in registration order. The body is a JSON array of `WorkerMetadata` (the `TokenSource` field is excluded from serialization).

| Property | Type | Description |
| --- | --- | --- |
| `GUID` | string (GUID) | Worker identifier assigned by the WebSocket server on connection. Matches the keys in `GET /maps`. |
| `Ip` | string | Remote IP address of the worker's WebSocket connection (for example `::1` for a local IPv6 loopback connection). |
| `Port` | integer | Remote (ephemeral) port of the worker's WebSocket connection, not a port the worker listens on. |
| `Healthy` | boolean | `true` on connection. Set to `false` when consecutive heartbeat send failures exceed `Heartbeat.MaxFailures`. Unhealthy workers stay in the list until they disconnect, and are skipped for new placements. |
| `AddedUtc` | string (timestamp) | When the worker connected. |
| `LastMessageUtc` | string (timestamp) | Initialized to the connection time, then updated every time the controller receives any message from the worker (a proxied response or a heartbeat reply). With the default 2000 ms heartbeat interval it is normally no more than a few seconds old for a connected worker. |

```bash
curl -H "x-api-key: constellationadmin" http://localhost:8000/workers
```

```json
[
  {
    "GUID": "b1574b90-bc2e-47f4-bc57-6cb3f5226902",
    "Ip": "::1",
    "Port": 61512,
    "Healthy": true,
    "AddedUtc": "2026-10-03T14:27:36.905899Z",
    "LastMessageUtc": "2026-10-03T14:31:12.408117Z"
  },
  {
    "GUID": "ce2a87d3-3389-4136-bdf7-ecccdb3afba3",
    "Ip": "10.0.0.12",
    "Port": 53214,
    "Healthy": false,
    "AddedUtc": "2026-10-03T14:28:02.114233Z",
    "LastMessageUtc": "2026-10-03T14:29:40.552906Z"
  }
]
```

With no workers connected the body is `[]`.

### GET /maps

Returns the resource map: a `Dictionary<Guid, List<string>>` serialized as a JSON object whose keys are worker GUIDs and whose values are the resource keys (raw URL paths) currently pinned to that worker. Workers with no pinned resources do not appear. With no mappings the body is `{}`.

```bash
curl -H "x-api-key: constellationadmin" http://localhost:8000/maps
```

```json
{
  "b1574b90-bc2e-47f4-bc57-6cb3f5226902": [
    "/databases/users.db",
    "/games/world-123"
  ],
  "ce2a87d3-3389-4136-bdf7-ecccdb3afba3": [
    "/databases/orders.db"
  ]
}
```

### Admin error cases

```bash
# Wrong key: 401 AuthorizationFailed
curl -i -H "x-api-key: wrong" http://localhost:8000/workers

# No key: not an admin request; proxied as resource /workers (502 if no workers)
curl -i http://localhost:8000/workers

# Valid key, wrong method: not an admin request; proxied as resource /workers
curl -i -X POST -H "x-api-key: constellationadmin" http://localhost:8000/workers
```

## Proxy (catch-all)

Any request not consumed by the steps above is forwarded to a worker over the WebSocket connection, and the worker's answer is returned to the caller. This covers every method (`GET`, `POST`, `PUT`, `DELETE`, `PATCH`, `OPTIONS`, `HEAD` on paths other than `/` and `/favicon.ico`, and any other method Watson accepts) and every path.

### Resource key and pinning

The **resource key** is the raw request path without the query string (`ctx.Request.Url.RawWithoutQuery`). It is compared as an exact, case-sensitive string, without decoding or normalization:

| Request | Resource key |
| --- | --- |
| `GET /databases/users.db` | `/databases/users.db` |
| `GET /databases/users.db?limit=10` | `/databases/users.db` (same resource) |
| `POST /databases/users.db` | `/databases/users.db` (method is not part of the key) |
| `GET /databases/users.db/` | `/databases/users.db/` (different resource) |
| `GET /Databases/users.db` | `/Databases/users.db` (different resource) |

Worker selection for a resource:

1. **Pinned.** If the resource is already mapped to a worker and that worker is still registered and healthy, that worker is used.
2. **Reassigned.** If the resource is mapped to a worker that is gone or unhealthy, the mapping is removed and a new worker is chosen as in step 3.
3. **Assigned.** Otherwise the next healthy worker is chosen round-robin over the registered workers, and the resource is pinned to it.
4. **No worker.** If no workers are registered, or none are healthy, the controller answers `502 BadGateway` (`No workers available for resource {resource}.`). Only the `x-request` header is present.

All mappings for a worker are removed when the worker disconnects. Mappings are otherwise kept for the life of the controller process, including for resources that are never requested again.

### What the worker receives

The controller builds a `WebsocketMessage` of type `Request` and sends it to the selected worker:

| Field | Value |
| --- | --- |
| `GUID` | New message GUID, used to correlate the worker's response |
| `Method` | The raw request method (`ctx.Request.MethodRaw`) |
| `Url.Uri` | Absolute URI built from the request scheme, the caller's `Host` header, and the raw path plus query string. If no usable `Host` header is present it falls back to the listener URL, then to `http://localhost:{port}`. |
| `Headers` | All request headers, including any `x-api-key`, `Authorization`, `traceparent`, and `Content-Type` the caller sent, with `x-forwarded-for` set as described below |
| `ContentType` | The caller's `Content-Type` (null when the caller sent none) |
| `Data` | The full request body as bytes (empty when there is none) |
| `TraceParent` / `TraceState` | W3C trace context of the controller's `worker request` span, when tracing is active |

The controller sets a single `x-forwarded-for` value on the forwarded headers, the way standard proxies extend the chain:

| Caller sent `x-forwarded-for` | Worker receives |
| --- | --- |
| No | `{client-ip}` (for example `192.0.2.10`; no port) |
| Yes, for example `203.0.113.7` | `{existing}, {client-ip}` (for example `203.0.113.7, 192.0.2.10`) |

`{client-ip}` is the address of the TCP peer that connected to the controller.

### What the caller receives

When the worker answers (a message of type `Response` with the same GUID) within `Proxy.TimeoutMs`:

| Part | Value |
| --- | --- |
| Status | The worker's `StatusCode`, or `200` if the worker left it null |
| Headers | The configured default headers and the controller's `x-request` and `x-worker`, with the worker's `Headers` merged on top (a worker header overrides a same-named header, except `x-request` and `x-worker`, which always keep the controller's values) |
| Content type | The worker's `ContentType` when non-empty, otherwise `application/json` |
| Body | The worker's `Data`, or no body when it is empty |

A worker that answers with a 4xx or 5xx is still a successful proxy from the controller's point of view; the status and body are passed through untouched.

### Proxy status codes

| Status | Source | Body |
| --- | --- | --- |
| Worker-defined (default `200`) | The worker answered | Worker-defined |
| `502` | No registered or healthy worker for the resource | `ApiErrorResponse` `BadGateway`, `No workers available for resource {resource}.` |
| `502` | The WebSocket send to the selected worker failed | `ApiErrorResponse` `BadGateway`, `Unable to proxy request for resource {resource}.` |
| `408` | No response within `Proxy.TimeoutMs` (default 30000 ms) | `ApiErrorResponse` `Timeout` |
| `500` | No response message (defensive path) | `ApiErrorResponse` `InternalError`, `No response received.` |
| `500` | Unhandled exception | `ApiErrorResponse` `InternalError`, description is the exception message (single-line JSON) |

A worker whose `OnRequestReceived` returns `null`, throws, or returns a message whose `Type` is not `Response` produces no usable reply, so the caller gets `408` after `Proxy.TimeoutMs`. A response that arrives after the timeout is held in the response store until `Proxy.ResponseRetentionMs` elapses and is then discarded.

### Examples

```bash
# Simple GET; first request pins /databases/users.db to a worker
curl -i http://localhost:8000/databases/users.db

# Query strings do not change the resource key: same worker as above
curl -i "http://localhost:8000/databases/users.db?limit=10"

# POST with a JSON body
curl -i -X POST http://localhost:8000/db/customers \
  -H "Content-Type: application/json" \
  -d '{"query":"SELECT * FROM customers"}'

# PUT and DELETE are forwarded the same way
curl -i -X PUT http://localhost:8000/games/world-123 \
  -H "Content-Type: application/json" \
  -d '{"state":"running"}'
curl -i -X DELETE http://localhost:8000/games/world-123

# Continue an existing distributed trace (W3C trace context)
curl -i http://localhost:8000/databases/users.db \
  -H "traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"

# See which worker owns which resource
curl -H "x-api-key: constellationadmin" http://localhost:8000/maps
```

Example successful proxied response (the worker set `X-Worker-Id` and a JSON body):

```
HTTP/1.1 200 OK
Content-Type: application/json
x-request: 9c1d4e52-0b7a-4f3e-8d21-6a5f0e3b7c90
x-worker: b1574b90-bc2e-47f4-bc57-6cb3f5226902
X-Worker-Id: worker-1

{"status":"ok"}
```

Example `502` when no workers are connected:

```
HTTP/1.1 502 Bad Gateway
Content-Type: application/json
x-request: 3f6c2a1e-6c1b-4d7e-9a43-2f1d8b0c5e77

{
  "Error": "BadGateway",
  "Message": "Your request is unable to be serviced as there are no origin servers available.",
  "StatusCode": 502,
  "Description": "No workers available for resource /databases/users.db."
}
```

## Tracing and metrics

- **Incoming trace context.** The controller's Watson listener accepts a W3C `traceparent` (and `tracestate`) header on any incoming request and starts its HTTP server span as a child of it (`Webserver.Telemetry.PropagateContext`, on by default). The Constellation spans for the request (`constellation proxy`, the stage spans, `worker request`, and the worker's `worker handle`) all join that trace. Without the header, the Watson span is a new root. Admin requests produce `admin list_workers` or `admin list_maps` spans under the same server span.
- **The header is also forwarded.** Because all request headers are forwarded, the worker also receives the caller's original `traceparent` in `Headers`; the trace context it should continue is the one in the message's `TraceParent` property.
- **Metrics are not served on this port.** The Prometheus scrape endpoint is a separate listener on port `9464` (`Telemetry.PrometheusPort`), hosted by the controller server, and it does not require the admin API key. Keep it internal.

See [TELEMETRY.md](TELEMETRY.md) for the metric and span catalogs, outcome values, configuration keys, and dashboards.

## Configuration reference

Settings in `constellation.json` that change the REST surface:

| Key | Default | Effect |
| --- | --- | --- |
| `Webserver.Hostname` | `*` in `docker/constellation.json` | Listener hostname for the REST API |
| `Webserver.Port` | `8000` in `docker/constellation.json` | Listener port for the REST API |
| `Webserver.Ssl.Enable` | `false` | Serve HTTPS instead of HTTP |
| `Webserver.Headers.DefaultHeaders` | CORS and cache headers (see [Response headers](#response-headers)) | Added to every response |
| `Webserver.IO.MaxRequests`, `Webserver.IO.ReadTimeoutMs` | `1024`, `10000` in `docker/constellation.json` | Watson listener limits |
| `Websocket.Port` | `8001` in `docker/constellation.json` | Port workers connect to |
| `Admin.ApiKeyHeader` | `x-api-key` | Header carrying the admin API key |
| `Admin.ApiKeys` | `[ "constellationadmin" ]` | Accepted admin API keys |
| `Proxy.TimeoutMs` | `30000` (minimum `1000`) | How long the controller waits for a worker before answering `408` |
| `Proxy.ResponseRetentionMs` | `30000` (minimum `1000`) | How long an unclaimed (late) worker response is kept before cleanup |
| `Heartbeat.IntervalMs` | `2000` (minimum `1000`) | Heartbeat interval per worker |
| `Heartbeat.MaxFailures` | `5` (minimum `1`) | A worker is marked unhealthy once consecutive heartbeat send failures exceed this value |
| `Telemetry.PrometheusPort` | `9464` | Metrics endpoint port (see [TELEMETRY.md](TELEMETRY.md)) |

## Known behaviors and caveats

These follow directly from the current implementation and are worth knowing when building clients:

- **An invalid API key is rejected everywhere.** Sending the API key header with a wrong value returns `401` even on paths that would otherwise be proxied. Clients that talk to workers through the controller should not send the admin header at all unless it is valid.
- **A valid admin key is forwarded to workers.** On proxied requests the admin key header is included in the forwarded headers.
- **The resource map only grows.** Mappings are removed when a worker disconnects or fails over, never because a resource went idle.
