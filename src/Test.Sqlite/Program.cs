namespace Test.Sqlite
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Data;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Constellation.Controller;
    using Constellation.Core;
    using Constellation.Worker;
    using SyslogLogging;
    using WatsonWebserver.Core;
    using WatsonWebsocket;

    public static class Program
    {
        #region Private-Members

        private static int _NumWorkers = 3;
        private static LoggingModule _Logging;
        private static TestController _Controller;
        private static List<TestWorker> _Workers;
        private static CancellationTokenSource _TokenSource;
        private static string _DatabaseDirectory = "./databases";

        #endregion

        #region Public-Methods

        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== Constellation SQLite Test Service ===");
            Console.WriteLine($"Starting controller and {_NumWorkers} workers...");
            Console.WriteLine("Controller: REST on port 8000, WebSocket on port 8100");
            Console.WriteLine("Press CTRL+C to exit");
            Console.WriteLine();

            PrintApiExamples();

            // Setup cancellation
            _TokenSource = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\nShutdown initiated...");
                _TokenSource.Cancel();
            };

            // Initialize logging
            _Logging = new LoggingModule();
            _Logging.Settings.EnableConsole = true;
            _Logging.Settings.MinimumSeverity = Severity.Debug;

            try
            {
                // Ensure database directory exists
                if (!Directory.Exists(_DatabaseDirectory))
                {
                    Directory.CreateDirectory(_DatabaseDirectory);
                    Console.WriteLine($"Created database directory: {_DatabaseDirectory}");
                }

                // Start controller
                await StartController();

                // Give controller time to fully initialize
                await Task.Delay(1000);

                // Start workers
                await StartWorkers();

                Console.WriteLine("\n=== Service Ready ===");
                Console.WriteLine("You can now send requests to http://localhost:8000/db/{database}");
                Console.WriteLine();

                // Keep running until cancelled
                try
                {
                    await Task.Delay(Timeout.Infinite, _TokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelled
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                await Cleanup();
            }
        }

        #endregion

        #region Private-Methods

        private static async Task StartController()
        {
            Console.WriteLine("Starting controller...");

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
                    Port = 8100,
                    Ssl = false
                },
                Heartbeat = new HeartbeatSettings
                {
                    IntervalMs = 2000,
                    MaxFailures = 3
                },
                Proxy = new ProxySettings
                {
                    TimeoutMs = 30000,
                    ResponseRetentionMs = 30000
                }
            };

            _Controller = new TestController(settings, _Logging, _TokenSource);
            await _Controller.Start();

            Console.WriteLine("Controller started successfully");
            Console.WriteLine("  REST API: http://localhost:8000");
            Console.WriteLine("  WebSocket: ws://localhost:8100");
        }

        private static async Task StartWorkers()
        {
            Console.WriteLine($"\nStarting {_NumWorkers} workers...");

            _Workers = new List<TestWorker>();

            for (int i = 1; i <= _NumWorkers; i++)
            {
                // Create a linked token source for each worker
                var workerToken = CancellationTokenSource.CreateLinkedTokenSource(_TokenSource.Token);

                var worker = new TestWorker(
                    _Logging,
                    "localhost",
                    8100,
                    false,
                    i,
                    workerToken,
                    _DatabaseDirectory
                );

                _Workers.Add(worker);
                await worker.Start();

                // Give each worker time to connect
                await Task.Delay(500);

                Console.WriteLine($"  Worker {i} started and connected");
            }

            Console.WriteLine($"All {_NumWorkers} workers started successfully");
        }

        private static async Task Cleanup()
        {
            Console.WriteLine("\nCleaning up resources...");

            // Stop and dispose workers first
            if (_Workers != null)
            {
                foreach (var worker in _Workers)
                {
                    try
                    {
                        worker?.Dispose();
                        Console.WriteLine($"  Worker {worker.NodeNumber} disposed");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Error disposing worker: {ex.Message}");
                    }
                }
            }

            // Give workers time to disconnect
            await Task.Delay(1000);

            // Stop and dispose controller
            try
            {
                await _Controller?.Stop();
                _Controller?.Dispose();
                Console.WriteLine("  Controller disposed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error disposing controller: {ex.Message}");
            }

            // Clean up token source
            _TokenSource?.Dispose();

            Console.WriteLine("Cleanup completed");
        }

        private static void PrintApiExamples()
        {
            Console.WriteLine("=== API Examples ===");
            Console.WriteLine();

            Console.WriteLine("Simple Query Examples:");
            Console.WriteLine("-".PadRight(50, '-'));

            Console.WriteLine("1. Create database and query:");
            Console.WriteLine("   curl -X POST http://localhost:8000/db/customers \\");
            Console.WriteLine("     -H 'Content-Type: application/json' \\");
            Console.WriteLine("     -d '{\"query\":\"SELECT datetime(\\\"now\\\")\"}'");
            Console.WriteLine();

            Console.WriteLine("2. Insert data:");
            Console.WriteLine("   curl -X POST http://localhost:8000/db/customers \\");
            Console.WriteLine("     -H 'Content-Type: application/json' \\");
            Console.WriteLine("     -d '{\"query\":\"INSERT INTO customers (name, email) VALUES (\\\"Alice\\\", \\\"alice@example.com\\\")\"}'");
            Console.WriteLine();

            Console.WriteLine("3. Query data:");
            Console.WriteLine("   curl -X POST http://localhost:8000/db/customers \\");
            Console.WriteLine("     -H 'Content-Type: application/json' \\");
            Console.WriteLine("     -d '{\"query\":\"SELECT * FROM customers\"}'");
            Console.WriteLine();

            Console.WriteLine("4. Different database (may go to different worker):");
            Console.WriteLine("   curl -X POST http://localhost:8000/db/orders \\");
            Console.WriteLine("     -H 'Content-Type: application/json' \\");
            Console.WriteLine("     -d '{\"query\":\"SELECT 1+1 as result\"}'");
            Console.WriteLine();

            Console.WriteLine("PowerShell Examples:");
            Console.WriteLine("-".PadRight(50, '-'));
            Console.WriteLine("Invoke-RestMethod -Uri 'http://localhost:8000/db/test' -Method POST `");
            Console.WriteLine("  -ContentType 'application/json' `");
            Console.WriteLine("  -Body '{\"query\":\"SELECT * FROM customers LIMIT 5\"}'");
            Console.WriteLine();

            Console.WriteLine("Note: Database files are created in ./databases/worker{N}/ directories");
            Console.WriteLine("Each database URL is pinned to a specific worker for exclusive access");
            Console.WriteLine("-".PadRight(50, '-'));
        }

        #endregion
    }

    #region Test-Controller

    public class TestController : ConstellationControllerBase
    {
        private readonly string _Header = "[Controller] ";
        private int _connectionCount = 0;

        public TestController(Settings settings, LoggingModule logging, CancellationTokenSource tokenSource)
            : base(settings, logging, tokenSource)
        {
        }

        public override async Task OnConnection(Guid guid, string ipAddress, int port)
        {
            _connectionCount++;
            Console.WriteLine($"{_Header}Worker connected: {guid} from {ipAddress}:{port} (Total: {_connectionCount})");
            await Task.CompletedTask;
        }

        public override async Task OnDisconnection(Guid guid, string ipAddress, int port)
        {
            _connectionCount--;
            Console.WriteLine($"{_Header}Worker disconnected: {guid} from {ipAddress}:{port} (Remaining: {_connectionCount})");
            await Task.CompletedTask;
        }
    }

    #endregion

    #region Test-Worker

    public class TestWorker : ConstellationWorkerBase
    {
        #region Private-Members

        private readonly string _Header;
        private readonly string _DatabaseDirectory;
        private readonly ConcurrentDictionary<string, SqliteConnection> _DatabaseConnections;
        private readonly object _dbLock = new object();
        public int NodeNumber { get; private set; }

        #endregion

        #region Constructors-and-Factories

        public TestWorker(
            LoggingModule logging,
            string hostname,
            int port,
            bool ssl,
            int nodeNumber,
            CancellationTokenSource tokenSource,
            string databaseDirectory)
            : base(logging, hostname, port, ssl, tokenSource)
        {
            NodeNumber = nodeNumber;
            _Header = $"[Worker{NodeNumber}] ";
            _DatabaseDirectory = Path.Combine(databaseDirectory, $"worker{nodeNumber}");
            _DatabaseConnections = new ConcurrentDictionary<string, SqliteConnection>();

            // Ensure worker-specific directory exists
            if (!Directory.Exists(_DatabaseDirectory))
            {
                Directory.CreateDirectory(_DatabaseDirectory);
            }
        }

        #endregion

        #region Public-Methods

        public override async Task OnConnection(Guid guid)
        {
            Console.WriteLine($"{_Header}Connected to controller (Session: {guid})");
            await Task.CompletedTask;
        }

        public override async Task OnDisconnection(Guid guid)
        {
            Console.WriteLine($"{_Header}Disconnected from controller (Session: {guid})");
            await CleanupDatabases();
        }

        public override async Task<WebsocketMessage> OnRequestReceived(WebsocketMessage req)
        {
            // Handle heartbeat
            if (req.Type.Equals(WebsocketMessageTypeEnum.Heartbeat))
            {
                return null; // Controller handles heartbeat responses
            }

            try
            {
                Console.WriteLine($"{_Header}Processing {req.Method} {req.Url.Path}");

                // Parse URL to get database name: /db/{database}
                var pathSegments = req.Url.Path.Trim('/').Split('/');
                if (pathSegments.Length < 2 || pathSegments[0] != "db")
                {
                    return CreateErrorResponse(req.GUID, 400, "Invalid path. Use /db/{database}");
                }

                string dbName = pathSegments[1];

                // Parse query from request body
                QueryRequest queryRequest = null;
                if (req.Data != null && req.Data.Length > 0)
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(req.Data);
                        queryRequest = JsonSerializer.Deserialize<QueryRequest>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    catch
                    {
                        // If not JSON, treat as plain SQL
                        queryRequest = new QueryRequest { Query = Encoding.UTF8.GetString(req.Data) };
                    }
                }

                // Default query if none provided
                if (queryRequest == null || string.IsNullOrWhiteSpace(queryRequest.Query))
                {
                    queryRequest = new QueryRequest { Query = "SELECT datetime('now') as current_time" };
                }

                // Execute the query
                var result = await ExecuteDatabaseQuery(dbName, queryRequest.Query);

                return CreateSuccessResponse(req.GUID, result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{_Header}Error processing request: {ex.Message}");
                return CreateErrorResponse(req.GUID, 500, ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Console.WriteLine($"{_Header}Disposing worker...");
                var cleanupTask = Task.Run(async () => await CleanupDatabases());
                cleanupTask.Wait(TimeSpan.FromSeconds(5));
            }

            base.Dispose(disposing);
        }

        #endregion

        #region Private-Methods

        private async Task<object> ExecuteDatabaseQuery(string dbName, string query)
        {
            var connection = GetOrCreateConnection(dbName);

            // Determine if this is a SELECT query or a command
            var trimmedQuery = query.Trim().ToUpper();
            bool isSelect = trimmedQuery.StartsWith("SELECT") ||
                           trimmedQuery.StartsWith("PRAGMA") ||
                           trimmedQuery.StartsWith("EXPLAIN");

            if (isSelect)
            {
                // Execute query and return results
                var results = new List<Dictionary<string, object>>();

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = query;

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var value = reader.GetValue(i);
                                row[reader.GetName(i)] = value == DBNull.Value ? null : value;
                            }
                            results.Add(row);
                        }
                    }
                }

                return new
                {
                    worker = NodeNumber,
                    database = dbName,
                    results = results,
                    rowCount = results.Count,
                    query = query
                };
            }
            else
            {
                // Execute command and return affected rows
                int affectedRows;

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = query;
                    affectedRows = await cmd.ExecuteNonQueryAsync();
                }

                return new
                {
                    worker = NodeNumber,
                    database = dbName,
                    affectedRows = affectedRows,
                    query = query,
                    success = true
                };
            }
        }

        private SqliteConnection GetOrCreateConnection(string dbName)
        {
            return _DatabaseConnections.GetOrAdd(dbName, name =>
            {
                lock (_dbLock)
                {
                    var dbPath = Path.Combine(_DatabaseDirectory, $"{name}.db");
                    bool isNew = !File.Exists(dbPath);

                    var connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;";
                    var connection = new SqliteConnection(connectionString);
                    connection.Open();

                    if (isNew)
                    {
                        Console.WriteLine($"{_Header}Created new database: {dbPath}");

                        // Create the customers table as shown in README
                        using (var cmd = connection.CreateCommand())
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

                        Console.WriteLine($"{_Header}Initialized customers table in {name}.db");
                    }
                    else
                    {
                        Console.WriteLine($"{_Header}Opened existing database: {dbPath}");
                    }

                    return connection;
                }
            });
        }

        private WebsocketMessage CreateSuccessResponse(Guid requestId, object data)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            return new WebsocketMessage
            {
                GUID = requestId,
                Type = WebsocketMessageTypeEnum.Response,
                StatusCode = 200,
                ContentType = "application/json",
                Data = Encoding.UTF8.GetBytes(json)
            };
        }

        private WebsocketMessage CreateErrorResponse(Guid requestId, int statusCode, string message)
        {
            var error = new
            {
                error = message,
                worker = NodeNumber,
                timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(error, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            return new WebsocketMessage
            {
                GUID = requestId,
                Type = WebsocketMessageTypeEnum.Response,
                StatusCode = statusCode,
                ContentType = "application/json",
                Data = Encoding.UTF8.GetBytes(json)
            };
        }

        private async Task CleanupDatabases()
        {
            Console.WriteLine($"{_Header}Closing all database connections...");

            foreach (var kvp in _DatabaseConnections)
            {
                try
                {
                    if (kvp.Value != null)
                    {
                        await kvp.Value.CloseAsync();
                        kvp.Value.Dispose();
                        Console.WriteLine($"{_Header}Closed database: {kvp.Key}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{_Header}Error closing database {kvp.Key}: {ex.Message}");
                }
            }

            _DatabaseConnections.Clear();
        }

        #endregion
    }

    #endregion

    #region Data-Models

    public class QueryRequest
    {
        public string Query { get; set; }
    }

    #endregion
}