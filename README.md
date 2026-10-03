<div align="center">
  <img src="https://raw.githubusercontent.com/jchristn/constellation/main/assets/logo.png" width="256" height="256">
</div>

# Constellation

**RESTful workload placement and virtualization for exactly-one resource ownership patterns**

<p align="center"><div style="display: flex; justify-content: center;">
<div>
  <table>
    <thead>
      <tr>
        <th>Project</th>
        <th>Package</th>
        <th>Downloads</th>
        <th>License</th>
      </tr>
    </thead>
    <tbody>
      <tr>
        <td>Controller</td>
        <td>
          <a href="https://www.nuget.org/packages/Constellation.Controller">
            <img src="https://img.shields.io/nuget/v/Constellation.Controller.svg" alt="NuGet Version">
          </a>
        </td>
        <td>
          <a href="https://www.nuget.org/packages/Constellation.Controller">
            <img src="https://img.shields.io/nuget/dt/Constellation.Controller.svg" alt="NuGet Downloads">
          </a>
        </td>
        <td>
          <a href="https://github.com/jchristn/constellation/blob/main/LICENSE">
            <img src="https://img.shields.io/github/license/jchristn/constellation" alt="License">
          </a>
        </td>
      </tr>
      <tr>
        <td>Worker</td>
        <td>
          <a href="https://www.nuget.org/packages/Constellation.Worker">
            <img src="https://img.shields.io/nuget/v/Constellation.Worker.svg" alt="NuGet Version">
          </a>
        </td>
        <td>
          <a href="https://www.nuget.org/packages/Constellation.Worker">
            <img src="https://img.shields.io/nuget/dt/Constellation.Worker.svg" alt="NuGet Downloads">
          </a>
        </td>
        <td>
          <a href="https://github.com/jchristn/constellation/blob/main/LICENSE">
            <img src="https://img.shields.io/github/license/jchristn/constellation" alt="License">
          </a>
        </td>
      </tr>
      <tr>
        <td>Core</td>
        <td>
          <a href="https://www.nuget.org/packages/Constellation.Core">
            <img src="https://img.shields.io/nuget/v/Constellation.Core.svg" alt="NuGet Version">
          </a>
        </td>
        <td>
          <a href="https://www.nuget.org/packages/Constellation.Core">
            <img src="https://img.shields.io/nuget/dt/Constellation.Core.svg" alt="NuGet Downloads">
          </a>
        </td>
        <td>
          <a href="https://github.com/jchristn/constellation/blob/main/LICENSE">
            <img src="https://img.shields.io/github/license/jchristn/constellation" alt="License">
          </a>
        </td>
      </tr>
    </tbody>
  </table>
</div>
</p>

## Why Constellation?

Modern distributed systems often need to ensure that certain resources are owned by exactly one process at a time. Whether it's a SQLite database, a machine learning model, a game world, or a hardware device - some things simply can't be shared. 

Constellation solves this fundamental distributed systems challenge by providing intelligent workload routing with sticky resource assignments, automatic failover, and seamless scaling.

### Real-World Use Cases

- **SQLite Databases**: Scale SQLite databases across multiple nodes while maintaining exclusive file locks
- **Machine Learning Models**: Efficiently distribute customer-specific models across workers without memory duplication
- **Game Servers**: Ensure each game world has exactly one authoritative server
- **Media Processing**: Prevent duplicate processing of video files during transcoding
- **IoT Device Management**: Maintain single WebSocket connections per device across your fleet
- **Blockchain Wallets**: Ensure exclusive access to wallet files to prevent double-spending
- **Hardware Access**: Scale services that need exclusive access to USB/serial or other hardware devices

## How It Works

Constellation uses a controller-worker architecture with intelligent resource routing:

1. **Controller**: Routes requests to appropriate workers, maintains resource-to-worker mappings
2. **Workers**: Handle actual workloads, maintain exclusive ownership of assigned resources  
3. **Resource Pinning**: The raw URL path becomes the resource key - requests to the same URL are routed to the same worker
4. **Automatic Failover**: When workers fail, resources are seamlessly reassigned
5. **Round-Robin Distribution**: New resources are distributed evenly across healthy workers

### Important: Resource Key Behavior

The raw URL (without query parameters) becomes the resource key for pinning. For example:
- `/databases/users.db` - All requests to this exact path go to the same worker
- `/databases/orders.db` - May go to a different worker
- `/games/world-123` and `/games/world-456` - May be on same or different workers

## Installation

```bash
dotnet add package Constellation.Controller   # controller base class
dotnet add package Constellation.Worker       # worker base class
```

Both depend on `Constellation.Core`, which is installed automatically.

## Quick Start

### Step 1: Create Your Controller

The controller can be run as-is - it's a complete application that routes requests to workers.

```csharp
using Constellation.Controller;

var settings = new Settings
{
    Webserver = new WebserverSettings
    {
        Hostname = "localhost",
        Port = 8000
    },
    Websocket = new WebsocketSettings
    {
        Hostnames = new List<string> { "localhost" },
        Port = 8001
    },
    Heartbeat = new HeartbeatSettings
    {
        IntervalMs = 2000,      // Check worker health every 2 seconds
        MaxFailures = 3         // Out of rotation after more than 3 consecutive undeliverable heartbeats
    }
};

var controller = new MyController(settings, logging);
await controller.Start();

public class MyController : ConstellationControllerBase
{
    public override async Task OnConnection(Guid guid, string ip, int port)
    {
        // Worker connected
    }

    public override async Task OnDisconnection(Guid guid, string ip, int port)
    {
        // Worker disconnected
    }
}
```

### Step 2: Implement Your Worker

Workers must be implemented by you - they contain your business logic.

```csharp
using Constellation.Worker;

public class MyWorker : ConstellationWorkerBase
{
    public override async Task<WebsocketMessage> OnRequestReceived(WebsocketMessage req)
    {
        // Skip heartbeat messages
        if (req.Type.Equals(WebsocketMessageTypeEnum.Heartbeat))
            return null;

        // YOUR CODE GOES HERE
        // You have exclusive ownership of this resource!
        // Process the request and return a response
        
        return new WebsocketMessage
        {
            GUID = req.GUID,
            Type = WebsocketMessageTypeEnum.Response,
            StatusCode = 200,
            ContentType = "application/json",
            Data = Encoding.UTF8.GetBytes("{\"result\":\"success\"}")
        };
    }
    
    public override async Task OnConnection(Guid guid)
    {
        // Connected to controller
    }

    public override async Task OnDisconnection(Guid guid)
    {
        // Disconnected from controller
    }
}

// Start your worker
var worker = new MyWorker(logging, "localhost", 8001, ssl: false, tokenSource);
await worker.Start();
```

### Step 3: Test Your Setup

```bash
# Request to /databases/users.db will be routed to a worker
curl http://localhost:8000/databases/users.db

# Subsequent requests to same path go to same worker
curl http://localhost:8000/databases/users.db  # Same worker

# Different path may go to different worker
curl http://localhost:8000/databases/orders.db  # Possibly different worker
```

## Complete Example: SQLite Service

Here's a simple but complete SQLite service that automatically creates databases on first access:

```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Constellation.Controller;
using Constellation.Core;
using Constellation.Worker;
using SyslogLogging;

// Program.cs - Run this complete example
class Program
{
    static async Task Main(string[] args)
    {
        var logging = new LoggingModule();
        var cts = new CancellationTokenSource();

        // Start controller
        var controller = new SQLiteController(
            new Settings
            {
                Webserver = new WebserverSettings { Hostname = "localhost", Port = 8000 },
                Websocket = new WebsocketSettings { 
                    Hostnames = new List<string> { "localhost" }, 
                    Port = 8001 
                }
            },
            logging,
            cts
        );
        await controller.Start();

        // Start 3 workers
        for (int i = 1; i <= 3; i++)
        {
            var worker = new SQLiteWorker(logging, "localhost", 8001, false, i, cts);
            await worker.Start();
        }

        Console.WriteLine("SQLite Service running on http://localhost:8000");
        Console.WriteLine("Try: curl -X POST http://localhost:8000/db/customers -d '{\"query\":\"SELECT * FROM customers\"}'");
        Console.ReadLine();
    }
}

// Controller - just routes requests
public class SQLiteController : ConstellationControllerBase
{
    public SQLiteController(Settings settings, LoggingModule logging, CancellationTokenSource tokenSource)
        : base(settings, logging, tokenSource) { }

    public override async Task OnConnection(Guid guid, string ip, int port)
        => Console.WriteLine($"Worker {guid} connected");

    public override async Task OnDisconnection(Guid guid, string ip, int port)
        => Console.WriteLine($"Worker {guid} disconnected");
}

// Worker - handles SQLite operations
public class SQLiteWorker : ConstellationWorkerBase
{
    private readonly Dictionary<string, SQLiteConnection> _databases = new();
    private readonly int _workerId;

    public SQLiteWorker(LoggingModule logging, string hostname, int port, bool ssl, 
                       int workerId, CancellationTokenSource tokenSource)
        : base(logging, hostname, port, ssl, tokenSource)
    {
        _workerId = workerId;
    }

    public override async Task<WebsocketMessage> OnRequestReceived(WebsocketMessage req)
    {
        if (req.Type.Equals(WebsocketMessageTypeEnum.Heartbeat))
            return null;

        try
        {
            // Extract database name from URL: /db/customers -> customers
            var dbName = req.Url.Path.Split('/')[2];
            
            // Get or create database connection
            if (!_databases.ContainsKey(dbName))
            {
                var conn = new SQLiteConnection($"Data Source={dbName}.db");
                conn.Open();
                
                // Create table if it doesn't exist
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS customers (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            name TEXT NOT NULL,
                            email TEXT,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                        )";
                    cmd.ExecuteNonQuery();
                }
                
                _databases[dbName] = conn;
                Console.WriteLine($"Worker {_workerId}: Created database '{dbName}.db'");
            }

            // Parse query from request body
            var request = JsonSerializer.Deserialize<QueryRequest>(
                Encoding.UTF8.GetString(req.Data ?? new byte[0])
            );
            
            // Execute query
            var results = new List<Dictionary<string, object>>();
            using (var cmd = _databases[dbName].CreateCommand())
            {
                cmd.CommandText = request?.Query ?? "SELECT datetime('now')";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.GetValue(i);
                        }
                        results.Add(row);
                    }
                }
            }

            // Return response
            var response = new { worker = _workerId, database = dbName, results };
            return new WebsocketMessage
            {
                GUID = req.GUID,
                Type = WebsocketMessageTypeEnum.Response,
                StatusCode = 200,
                ContentType = "application/json",
                Data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response))
            };
        }
        catch (Exception ex)
        {
            return new WebsocketMessage
            {
                GUID = req.GUID,
                Type = WebsocketMessageTypeEnum.Response,
                StatusCode = 500,
                ContentType = "application/json",
                Data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = ex.Message }))
            };
        }
    }

    public override async Task OnConnection(Guid guid)
        => Console.WriteLine($"Worker {_workerId} connected");

    public override async Task OnDisconnection(Guid guid)
    {
        foreach (var db in _databases.Values)
            db.Dispose();
    }

    public class QueryRequest
    {
        public string Query { get; set; }
    }
}
```

### Testing the SQLite Service

```bash
# Create and query the customers database (auto-creates table on first access)
curl -X POST http://localhost:8000/db/customers \
  -H "Content-Type: application/json" \
  -d '{"query":"INSERT INTO customers (name, email) VALUES (\"Alice\", \"alice@example.com\")"}'

# Query the data (will always go to the same worker)
curl -X POST http://localhost:8000/db/customers \
  -H "Content-Type: application/json" \
  -d '{"query":"SELECT * FROM customers"}'

# Different database may go to different worker
curl -X POST http://localhost:8000/db/orders \
  -H "Content-Type: application/json" \
  -d '{"query":"SELECT datetime(\"now\")"}'
```

## Testing with the Included Projects

The repository includes a ready-to-run controller server and a test worker that you can use to quickly verify your setup without writing any code.

### Step 1: Start the Controller Server

```bash
cd src/Constellation.ControllerServer
dotnet run
```

This starts the controller listening on port `8000` (HTTP) and port `8001` (WebSocket) using the default configuration in `constellation.json`.

### Step 2: Start the Test Worker

In a separate terminal:

```bash
cd src/Test.ConstellationWorker
dotnet run
```

The test worker connects to the controller via WebSocket on `localhost:8001`. It logs every incoming request (method, path, content type, and body) to the console and returns a `200 OK` JSON response.

You can also pass a custom hostname, port, and SSL flag as positional arguments (`hostname port ssl`):

```bash
dotnet run -- 192.168.1.100 9001 true
```

### Step 3: Send Requests

```bash
# Health check
curl http://localhost:8000

# Send a request (will be routed to the test worker)
curl http://localhost:8000/my/resource

# POST with a body
curl -X POST http://localhost:8000/my/resource \
  -H "Content-Type: application/json" \
  -d '{"hello":"world"}'
```

You should see each request logged in the test worker's console output. You can run multiple instances of `Test.ConstellationWorker` to see round-robin resource distribution in action.

## Dashboard

Constellation includes a web-based dashboard for monitoring and managing your controller, workers, and resource assignments. The dashboard is a React application built with Vite.

### Features

- **System overview** with controller health, worker count, and resource count
- **Workers view** with sortable/filterable table, health status, resource counts, and detail drill-down
- **Resource map** showing which resources are pinned to which workers
- **Connection and health settings** at a glance
- **Light/dark theme** with automatic persistence
- **Auto-refresh** every 10 seconds
- **Copy-to-clipboard** for GUIDs and resource paths
- **External Services** card linking to Grafana, Prometheus, Tempo, and Loki with their URLs and default credentials

### Running the Dashboard

**Development mode:**

```bash
cd dashboard
npm install
npm run dev
```

The dashboard will be available at `http://localhost:8080`. Enter your controller URL (e.g. `http://localhost:8000`) and admin API key to connect.

**Production build:**

```bash
cd dashboard
npm install
npm run build
```

The built files will be in `dashboard/dist/` and can be served by any static file server.

## Docker

The official Docker image for the controller is available at: [`jchristn77/constellation`](https://hub.docker.com/r/jchristn77/constellation). Refer to the `docker` directory for assets useful for running in Docker and Docker Compose.

### Controller Only

```bash
# Using the run script
cd docker
run.bat v1.0.0        # Windows
./run.sh v1.0.0       # Linux/macOS

# Or using Docker directly
docker run -d \
  --name constellation-controller \
  --network host \
  -v ./constellation.json:/app/constellation.json \
  jchristn77/constellation:v1.0.0
```

### Controller + Dashboard + Observability (Docker Compose)

The `docker/compose.yaml` file runs the controller, the dashboard, and a complete observability stack together:

```bash
cd docker
docker compose up -d
```

This starts:
- **Controller** on port `8000` (HTTP) and port `8001` (WebSocket)
- **Dashboard** on port `8080`
- **Grafana** on port `3000` (`admin` / `admin` locally) with a provisioned **Constellation** dashboard folder
- **Prometheus** on port `9090`, **Tempo** on port `3200`, **Loki** on port `3100`
- **OpenTelemetry Collector** on ports `4317` (gRPC) and `4318` (HTTP) for OTLP from the controller and from any workers you run

Open `http://localhost:8080` in your browser and connect to `http://localhost:8000` with your admin API key. The dashboard's home page links to the observability tools. For any shared deployment, set `GRAFANA_ADMIN_USER` and `GRAFANA_ADMIN_PASSWORD` before starting the stack. To pull newer images and recreate the stack without losing data, run `docker/update.bat` or `docker/update.sh`.

### Building Docker Images

```bash
# Build and push controller and dashboard images (multi-arch, via the cloud builder)
build-all.bat v1.0.0        # Windows
./build-all.sh v1.0.0       # Linux/macOS

# Or individually
build-server.bat v1.0.0     # or ./build-server.sh v1.0.0
build-dashboard.bat v1.0.0  # or ./build-dashboard.sh v1.0.0
```

## Observability

Constellation ships with metrics, traces, and logs built in, so an operator can see from Grafana alone where time went and what failed.

- **Libraries emit, applications host.** `Constellation.Core`, `Constellation.Controller`, and `Constellation.Worker` record into a BCL `Meter` and `ActivitySource` named `Constellation`, with no exporter dependency. With nothing subscribed, recording is effectively free.
- **Watson 7.2 covers HTTP.** The controller's Watson listener emits the standard `http.server.*` metrics and one server span per request.
- **Constellation covers everything behind the route:** placement decisions (`pinned`, `assigned`, `reassigned` on failover, `no_workers`, `no_healthy_workers`), every proxy stage (`placement`, `dispatch`, `await_response`, `respond`) with outcomes (`success`, `no_worker`, `send_failed`, `timeout`, `no_response`, `error`), worker pool and heartbeat health, the response correlation store, WebSocket messaging, admin API usage, caught errors by type, build info, and configuration.
- **One trace per request, across processes.** Trace context travels inside the WebSocket message, so the controller's spans and the worker's `worker handle` span (and anything your handler calls) form a single trace.
- **The controller server exports out of the box** through [Radiant](https://github.com/jchristn/Radiant): a Prometheus scrape endpoint plus OTLP to a collector, Tempo, or Loki. Configure it under `Telemetry` in `constellation.json`.

Subscribe your own controller or worker host by name:

```csharp
RadiantSettings settings = new RadiantSettings("my-worker");
settings.Sources.AddMeter("Constellation");
settings.Sources.AddActivitySource("Constellation");
using RadiantHost host = RadiantHost.Start(settings);
```

See [TELEMETRY.md](TELEMETRY.md) for the full metric and span catalog, configuration keys, the dashboard map, and recommended alerts.

## REST API

The controller answers `GET`/`HEAD /` (health) and `/favicon.ico` itself, serves two admin endpoints, and proxies everything else to the worker that owns the resource:

- `GET /workers`: the worker pool, with health and timestamps. Requires the admin API key header.
- `GET /maps`: worker GUID to pinned resources. Requires the admin API key header.
- Proxied responses carry `x-request` (request GUID) and `x-worker` (owning worker GUID) alongside the worker's own headers.

The admin header defaults to `x-api-key` and the key to `constellationadmin` (`Settings.Admin`, and the `Admin` block in `docker/constellation.json`). Change the key before exposing the controller. A request that sends the header with a wrong key is rejected with `401` on any path.

See [REST_API.md](REST_API.md) for every endpoint, status code, and error body, and import [assets/postman/Constellation.postman_collection.json](assets/postman/Constellation.postman_collection.json) into Postman to try them.

## Configuration

### Controller Settings

```csharp
var settings = new Settings
{
    Webserver = new WebserverSettings
    {
        Hostname = "0.0.0.0",     // Listen on all interfaces
        Port = 8000               // HTTP port for incoming requests
    },
    Websocket = new WebsocketSettings
    {
        Hostnames = new List<string> { "0.0.0.0" },
        Port = 8001,              // WebSocket port for worker connections
        Ssl = false
    },
    Heartbeat = new HeartbeatSettings
    {
        IntervalMs = 2000,        // How often to ping workers
        MaxFailures = 3           // Out of rotation after more than 3 consecutive undeliverable heartbeats (default 5)
    },
    Proxy = new ProxySettings
    {
        TimeoutMs = 30000,        // Request timeout
        ResponseRetentionMs = 30000
    },
    Telemetry = new TelemetrySettings
    {
        OtlpEndpoint = "http://127.0.0.1:4317",   // used by Constellation.ControllerServer's Radiant host
        PrometheusEnable = true,                  // scrape at http://127.0.0.1:9464/metrics
        PrometheusPort = 9464
    }
};
```

The `Telemetry` section is read by the controller server (`Constellation.ControllerServer` and the Docker image). If you host the controller yourself, subscribe your own collector to the `Constellation` and `Watson` sources instead; see [Observability](#observability).

### Health Check

The controller sends each connected worker a heartbeat every `IntervalMs` (default 2000). A worker is marked unhealthy, and its heartbeat loop stops, when **more than `MaxFailures` consecutive heartbeats** (default 5) cannot be delivered, which takes roughly `IntervalMs × (MaxFailures + 1)`.

- An unhealthy worker receives no new placements. Resources pinned to it move to a healthy worker on their next request.
- A worker that disconnects is removed from the pool immediately, along with its resource mappings.
- A worker that reconnects joins the pool as a new worker.

Example: with IntervalMs=2000 and MaxFailures=3, a worker whose connection stops accepting heartbeats is taken out of rotation after about 8 seconds.

## Best Practices

### Resource Naming

Remember that the raw URL becomes the resource key. Design your URLs carefully:

```
Good patterns for databases:
/db/customers         -> All customer DB operations on same worker
/db/orders           -> May be on different worker
/db/inventory        -> May be on different worker

Good patterns for game servers:
/games/world-123        -> All operations for world-123 on same worker
/games/world-456        -> May be on different worker

Good patterns for ML models:
/models/customer-abc/sentiment   -> All requests for this model on same worker
/models/customer-xyz/sentiment   -> May be on different worker
```

### Worker Implementation

1. Always handle the Heartbeat message type
2. Implement proper cleanup in OnDisconnection
3. Return appropriate HTTP status codes in responses
4. Handle exceptions gracefully

### High Availability

For production deployments:
- Run multiple controllers behind a load balancer (nginx, HAProxy, etc.)
- Use the load balancer for SSL termination
- Deploy workers across multiple machines
- Monitor worker health and resource distribution

## Data Flow

```
Clients send HTTP requests to Controller
                ↓
Controller (Port 8000) receives requests
                ↓
Controller looks up which Worker owns the resource (URL path)
                ↓
Controller forwards request via WebSocket to Worker
                ↓
Worker (connected to the controller's WebSocket port 8001) processes request with exclusive resource access
                ↓
Worker sends response to Controller
                ↓
Controller returns response to Client
```

## Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

## License

Constellation is licensed under the MIT License. See [LICENSE](LICENSE.md) for details.

## Acknowledgments

Built with:
- [WatsonWebserver](https://github.com/jchristn/watsonwebserver) - Web server
- [WatsonWebsocket](https://github.com/jchristn/watsonwebsocket) - WebSocket implementation
- [SyslogLogging](https://github.com/jchristn/sysloglogging) - Logging

---

© 2026 Joel Christner