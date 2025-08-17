<div align="center">
  <img src="https://github.com/jchristn/constellation/blob/main/assets/logo.png" width="256" height="256">
</div>

# Constellation

<p align="center">
  Constellation.Controller | <a href="https://www.nuget.org/packages/Constellation.Controller"><img src="https://img.shields.io/nuget/v/Constellation.Controller.svg" alt="NuGet Version"></a> | <a href="https://www.nuget.org/packages/Constellation.Controller"><img src="https://img.shields.io/nuget/dt/Constellation.Controller.svg" alt="NuGet Downloads"></a> | <a href="https://github.com/jchristn/constellation/blob/main/LICENSE"><img src="https://img.shields.io/github/license/jchristn/constellation" alt="License"></a><br />
  
  Constellation.Worker | <a href="https://www.nuget.org/packages/Constellation.Worker"><img src="https://img.shields.io/nuget/v/Constellation.Worker.svg" alt="NuGet Version"></a> | <a href="https://www.nuget.org/packages/Constellation.Worker"><img src="https://img.shields.io/nuget/dt/Constellation.Worker.svg" alt="NuGet Downloads"></a> | <a href="https://github.com/jchristn/constellation/blob/main/LICENSE"><img src="https://img.shields.io/github/license/jchristn/constellation" alt="License"></a>
</p>

**RESTful workload placement and virtualization for exactly-one resource ownership patterns**

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
dotnet add package Constellation
```

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
        MaxFailures = 3         // Mark unhealthy after 6 seconds (2000ms * 3)
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

## Docker Image

The official Docker image for the controller is available at: [`jchristn/constellation`](https://hub.docker.com/r/jchristn/constellation).  Refer to the `docker` directory for assets useful for running in Docker and Docker Compose.  

- For Windows: `run.bat v1.0.0` or `docker compose -f compose.yaml up`
- For Linux/macOS: `./run.sh v1.0.0` or `docker compose -f compose.yaml up`

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
        MaxFailures = 3           // Worker marked unhealthy after 6 seconds (2000ms * 3)
    },
    Proxy = new ProxySettings
    {
        TimeoutMs = 30000,        // Request timeout
        ResponseRetentionMs = 30000
    }
};
```

### Health Check

Workers are considered unhealthy when they fail to respond to heartbeats for:
**IntervalMs × MaxFailures** milliseconds

Example: With IntervalMs=2000 and MaxFailures=3, a worker is marked unhealthy after 6 seconds of no response.

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
Worker (Port 8001) processes request with exclusive resource access
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

© 2025 Joel Christner