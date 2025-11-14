namespace Test
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Controller;
    using Constellation.Core;
    using Constellation.Core.Serialization;
    using Constellation.Worker;
    using RestWrapper;
    using SyslogLogging;
    using WatsonWebserver.Core;
    using WatsonWebsocket;

    using HttpMethod = System.Net.Http.HttpMethod;

    public static class Program
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        private static Serializer _Serializer = new Serializer();
        private static int _TestsPassed = 0;
        private static int _TestsFailed = 0;
        private static List<TestResult> _TestResults = new List<TestResult>();
        private static int _CurrentTestStartAssertions = 0;

        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== Constellation Test Suite with Resource Pinning ===\n");

            // Run each test with its own controller instance to ensure clean state
            await RunTest("Test 1: No Workers Available", TestNoWorkersAvailable);
            await RunTest("Test 2: Single Worker Resource Pinning", TestSingleWorkerResourcePinning);
            await RunTest("Test 3: Multiple Workers with Resource Pinning", TestMultipleWorkersResourcePinning);
            await RunTest("Test 4: Resource Remapping After Worker Failure", TestResourceRemappingAfterWorkerFailure);
            await RunTest("Test 5: Multiple Resources Per Worker", TestMultipleResourcesPerWorker);
            await RunTest("Test 6: Worker Recovery and Resource Persistence", TestWorkerRecoveryResourcePersistence);
            await RunTest("Test 7: Concurrent Requests to Same Resource", TestConcurrentRequestsSameResource);
            await RunTest("Test 8: Load Distribution Across Workers", TestLoadDistributionAcrossWorkers);
            await RunTest("Test 9: Admin API - Workers Endpoint with Valid Key", TestAdminApiWorkersWithValidKey);
            await RunTest("Test 10: Admin API - Workers Endpoint with Invalid Key", TestAdminApiWorkersWithInvalidKey);
            await RunTest("Test 11: Admin API - Workers Endpoint without Key", TestAdminApiWorkersWithoutKey);
            await RunTest("Test 12: Admin API - Maps Endpoint with Valid Key", TestAdminApiMapsWithValidKey);
            await RunTest("Test 13: Admin API - Maps Endpoint with Invalid Key", TestAdminApiMapsWithInvalidKey);
            await RunTest("Test 14: Admin API - Maps Endpoint without Key", TestAdminApiMapsWithoutKey);

            // Print Summary
            PrintTestSummary();

            Console.WriteLine("\nPress ENTER to exit");
            Console.ReadLine();
        }

        private static async Task RunTest(string testName, Func<Task> testFunc)
        {
            Console.WriteLine($"\n--- {testName} ---");

            int startPassedCount = _TestsPassed;
            int startFailedCount = _TestsFailed;
            _CurrentTestStartAssertions = _TestsPassed + _TestsFailed;
            bool testPassed = false;
            string errorMessage = null;

            try
            {
                await testFunc();
                await Task.Delay(2000); // Clean delay between tests

                // Test passes if no new failures occurred
                testPassed = (_TestsFailed == startFailedCount);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Test failed with exception: {ex.Message}");
                _TestsFailed++;
                testPassed = false;
                errorMessage = ex.Message;
            }

            _TestResults.Add(new TestResult
            {
                Name = testName,
                Passed = testPassed,
                ErrorMessage = errorMessage,
                AssertionsPassed = _TestsPassed - startPassedCount,
                AssertionsFailed = _TestsFailed - startFailedCount
            });
        }

        private static async Task<TestController> CreateController()
        {
            Settings settings = new Settings
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
                    IntervalMs = 2000,
                    MaxFailures = 3
                },
                Proxy = new ProxySettings
                {
                    TimeoutMs = 5000
                }
            };

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = true;
            logging.Settings.MinimumSeverity = Severity.Info; // Reduce noise

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            var controller = new TestController(settings, logging, tokenSource);
            await controller.Start();
            await Task.Delay(1000); // Let controller fully initialize

            return controller;
        }

        private static async Task<bool> WaitForWorkerCount(TestController controller, int expectedCount, int timeoutMs = 5000)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                int currentCount = controller.Workers.Count;
                if (currentCount == expectedCount)
                {
                    return true;
                }
                await Task.Delay(100);
            }
            return false;
        }

        private static async Task TestNoWorkersAvailable()
        {
            using (var controller = await CreateController())
            {
                using (RestRequest req = new RestRequest("http://localhost:8000/api/users", HttpMethod.Get))
                {
                    using (RestResponse resp = await req.SendAsync())
                    {
                        AssertEquals("Status Code", 502, (int)resp.StatusCode);
                        AssertContains("Response Body", "No workers available", resp.DataAsString);
                    }
                }
            }
        }

        private static async Task TestSingleWorkerResourcePinning()
        {
            using (var controller = await CreateController())
            {
                var workerToken = new CancellationTokenSource();

                try
                {
                    using (TestWorker worker = new TestWorker(controller.Logging, "localhost", 8001, false, 1, workerToken))
                    {
                        await worker.Start();
                        await Task.Delay(2000); // Wait for connection and initial heartbeat

                        AssertTrue("Worker connected", controller.Workers.Count == 1);

                        // Send multiple requests to same resource
                        string resource = "/api/users";
                        for (int i = 0; i < 5; i++)
                        {
                            using (RestRequest req = new RestRequest($"http://localhost:8000{resource}", HttpMethod.Get))
                            {
                                using (RestResponse resp = await req.SendAsync())
                                {
                                    AssertEquals($"Request {i} Status", 200, (int)resp.StatusCode);
                                    AssertEquals($"Request {i} Response", "Response from worker 1", resp.DataAsString);
                                    AssertContains($"Request {i} Worker Header", "worker-1", resp.Headers["X-Worker-Id"]);
                                }
                            }
                        }

                        // Try different resource
                        string resource2 = "/api/products";
                        using (RestRequest req = new RestRequest($"http://localhost:8000{resource2}", HttpMethod.Get))
                        {
                            using (RestResponse resp = await req.SendAsync())
                            {
                                AssertEquals("Different Resource Status", 200, (int)resp.StatusCode);
                                AssertEquals("Different Resource Response", "Response from worker 1", resp.DataAsString);
                            }
                        }

                        // Cancel token to trigger disconnection
                        workerToken.Cancel();
                    } // Worker disposed here

                    // Wait for worker to be removed
                    await WaitForWorkerCount(controller, 0);
                }
                finally
                {
                    // Dispose token after worker is fully disposed
                    workerToken.Dispose();
                }
            }
        }

        private static async Task TestMultipleWorkersResourcePinning()
        {
            using (var controller = await CreateController())
            {
                List<TestWorker> workers = new List<TestWorker>();
                List<CancellationTokenSource> workerTokens = new List<CancellationTokenSource>();

                try
                {
                    // Start workers one by one with independent tokens
                    for (int i = 1; i <= 3; i++)
                    {
                        var workerToken = new CancellationTokenSource();
                        workerTokens.Add(workerToken);
                        var worker = new TestWorker(controller.Logging, "localhost", 8001, false, i, workerToken);
                        workers.Add(worker);
                        await worker.Start();
                        await Task.Delay(1500); // Wait between worker starts
                    }

                    // Wait for all workers to be registered
                    await Task.Delay(2000);
                    AssertEquals("Worker count", 3, controller.Workers.Count);

                    // Test different resources get mapped to different workers
                    Dictionary<string, string> resourceToWorker = new Dictionary<string, string>();
                    string[] resources = { "/api/users", "/api/products", "/api/orders", "/api/customers", "/api/inventory", "/api/reports" };

                    foreach (string resource in resources)
                    {
                        using (RestRequest req = new RestRequest($"http://localhost:8000{resource}", HttpMethod.Get))
                        {
                            using (RestResponse resp = await req.SendAsync())
                            {
                                AssertEquals($"Resource {resource} Status", 200, (int)resp.StatusCode);
                                string workerId = resp.Headers["X-Worker-Id"];
                                resourceToWorker[resource] = workerId;
                                Console.WriteLine($"  Resource {resource} -> {workerId}");
                            }
                        }
                    }

                    // Verify each resource consistently maps to same worker
                    Console.WriteLine("  Verifying resource pinning consistency...");
                    foreach (string resource in resources)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            using (RestRequest req = new RestRequest($"http://localhost:8000{resource}", HttpMethod.Get))
                            {
                                using (RestResponse resp = await req.SendAsync())
                                {
                                    string workerId = resp.Headers["X-Worker-Id"];
                                    AssertEquals($"Resource {resource} consistency check {i}", resourceToWorker[resource], workerId);
                                }
                            }
                        }
                    }

                    // Verify load is distributed across workers
                    var workerCounts = resourceToWorker.Values.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
                    Console.WriteLine("  Resource distribution:");
                    foreach (var kvp in workerCounts)
                    {
                        Console.WriteLine($"    {kvp.Key}: {kvp.Value} resources");
                    }
                    AssertTrue("All workers have resources", workerCounts.Count == 3);
                }
                finally
                {
                    // Cleanup with independent tokens
                    for (int i = 0; i < workers.Count; i++)
                    {
                        try
                        {
                            workerTokens[i]?.Cancel();
                            workers[i]?.Dispose();
                        }
                        catch { }
                    }

                    foreach (var token in workerTokens)
                    {
                        token?.Dispose();
                    }
                }
            }
        }

        private static async Task TestResourceRemappingAfterWorkerFailure()
        {
            using (var controller = await CreateController())
            {
                List<TestWorker> workers = new List<TestWorker>();
                List<CancellationTokenSource> workerTokens = new List<CancellationTokenSource>();

                try
                {
                    // Start 3 workers with independent cancellation tokens
                    for (int i = 1; i <= 3; i++)
                    {
                        var workerToken = new CancellationTokenSource();
                        workerTokens.Add(workerToken);
                        var worker = new TestWorker(controller.Logging, "localhost", 8001, false, i, workerToken);
                        workers.Add(worker);
                        await worker.Start();
                        await Task.Delay(1000);
                    }

                    await Task.Delay(2000);
                    AssertEquals("Initial worker count", 3, controller.Workers.Count);

                    // Map resources to workers
                    string[] resources = { "/api/users", "/api/products", "/api/orders" };
                    Dictionary<string, string> originalMapping = new Dictionary<string, string>();

                    foreach (string resource in resources)
                    {
                        using (RestRequest req = new RestRequest($"http://localhost:8000{resource}", HttpMethod.Get))
                        {
                            using (RestResponse resp = await req.SendAsync())
                            {
                                originalMapping[resource] = resp.Headers["X-Worker-Id"];
                                Console.WriteLine($"  Initial: {resource} -> {originalMapping[resource]}");
                            }
                        }
                    }

                    // Find which worker owns /api/users and remove it
                    string targetResource = "/api/users";
                    string targetWorkerId = originalMapping[targetResource];
                    int workerIndex = int.Parse(targetWorkerId.Split('-')[1]) - 1;

                    Console.WriteLine($"  Removing {targetWorkerId} which owns {targetResource}");

                    // Cancel only this worker's token
                    workerTokens[workerIndex].Cancel();
                    workers[workerIndex].Dispose();
                    await Task.Delay(1000); // Wait for disconnection

                    // Clean up references but don't dispose token yet
                    workers[workerIndex] = null;

                    await Task.Delay(1000); // Give time for removal
                    AssertEquals("Worker count after removal", 2, controller.Workers.Count);

                    // Verify the resource gets remapped
                    using (RestRequest req = new RestRequest($"http://localhost:8000{targetResource}", HttpMethod.Get))
                    {
                        using (RestResponse resp = await req.SendAsync())
                        {
                            AssertEquals("Remapped request status", 200, (int)resp.StatusCode);
                            string newWorkerId = resp.Headers["X-Worker-Id"];
                            AssertNotEquals("Resource remapped to different worker", targetWorkerId, newWorkerId);
                            Console.WriteLine($"  Remapped: {targetResource} -> {newWorkerId}");

                            // Verify it stays pinned to the new worker
                            for (int i = 0; i < 3; i++)
                            {
                                using (RestRequest req2 = new RestRequest($"http://localhost:8000{targetResource}", HttpMethod.Get))
                                {
                                    using (RestResponse resp2 = await req2.SendAsync())
                                    {
                                        AssertEquals($"Remapped consistency {i}", newWorkerId, resp2.Headers["X-Worker-Id"]);
                                    }
                                }
                            }
                        }
                    }

                    // Clean up remaining workers before controller disposal
                    Console.WriteLine("  Cleaning up remaining workers...");
                    for (int i = 0; i < workers.Count; i++)
                    {
                        if (workers[i] != null && workerTokens[i] != null)
                        {
                            try
                            {
                                if (!workerTokens[i].Token.IsCancellationRequested)
                                    workerTokens[i].Cancel();
                                workers[i].Dispose();
                                await Task.Delay(500);
                            }
                            catch { }
                        }
                    }

                    // Wait for all workers to disconnect
                    await WaitForWorkerCount(controller, 0);
                    Console.WriteLine("  All workers cleaned up");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Test error: {ex.Message}");
                    // Emergency cleanup
                    for (int i = 0; i < workers.Count; i++)
                    {
                        if (workers[i] != null && workerTokens[i] != null)
                        {
                            try
                            {
                                if (!workerTokens[i].Token.IsCancellationRequested)
                                    workerTokens[i].Cancel();
                                workers[i].Dispose();
                            }
                            catch { }
                        }
                    }
                    throw;
                }
                finally
                {
                    // Dispose all token sources after workers are disposed
                    await Task.Delay(1000); // Ensure workers are fully cleaned up
                    foreach (var token in workerTokens)
                    {
                        token?.Dispose();
                    }
                }
            }
        }

        private static async Task TestMultipleResourcesPerWorker()
        {
            using (var controller = await CreateController())
            {
                var worker1Token = new CancellationTokenSource();
                var worker2Token = new CancellationTokenSource();

                try
                {
                    using (TestWorker worker1 = new TestWorker(controller.Logging, "localhost", 8001, false, 1, worker1Token))
                    using (TestWorker worker2 = new TestWorker(controller.Logging, "localhost", 8001, false, 2, worker2Token))
                    {
                        await worker1.Start();
                        await Task.Delay(1000);
                        await worker2.Start();
                        await Task.Delay(2000);

                        AssertEquals("Worker count", 2, controller.Workers.Count);

                        // Send many different resources
                        Dictionary<string, int> workerResourceCount = new Dictionary<string, int> { ["worker-1"] = 0, ["worker-2"] = 0 };

                        for (int i = 0; i < 10; i++)
                        {
                            string resource = $"/api/resource{i}";
                            using (RestRequest req = new RestRequest($"http://localhost:8000{resource}", HttpMethod.Get))
                            {
                                using (RestResponse resp = await req.SendAsync())
                                {
                                    string workerId = resp.Headers["X-Worker-Id"];
                                    workerResourceCount[workerId]++;
                                }
                            }
                        }

                        Console.WriteLine($"  Worker 1: {workerResourceCount["worker-1"]} resources");
                        Console.WriteLine($"  Worker 2: {workerResourceCount["worker-2"]} resources");

                        AssertTrue("Both workers have resources",
                            workerResourceCount["worker-1"] > 0 && workerResourceCount["worker-2"] > 0);

                        // Cancel tokens before disposal
                        worker1Token.Cancel();
                        worker2Token.Cancel();
                    } // Workers disposed here

                    await Task.Delay(500);
                }
                finally
                {
                    // Dispose tokens after workers are disposed
                    worker1Token.Dispose();
                    worker2Token.Dispose();
                }
            }
        }

        private static async Task TestWorkerRecoveryResourcePersistence()
        {
            using (var controller = await CreateController())
            {
                List<TestWorker> workers = new List<TestWorker>();
                List<CancellationTokenSource> workerTokens = new List<CancellationTokenSource>();

                try
                {
                    // Start with 2 workers
                    for (int i = 1; i <= 2; i++)
                    {
                        var workerToken = new CancellationTokenSource();
                        workerTokens.Add(workerToken);
                        var worker = new TestWorker(controller.Logging, "localhost", 8001, false, i, workerToken);
                        workers.Add(worker);
                        await worker.Start();
                        await Task.Delay(1000);
                    }

                    await Task.Delay(1000);
                    AssertEquals("Initial worker count", 2, controller.Workers.Count);

                    // Map a resource
                    string resource = "/api/persistent";
                    string originalWorker;
                    using (RestRequest req = new RestRequest($"http://localhost:8000{resource}", HttpMethod.Get))
                    {
                        using (RestResponse resp = await req.SendAsync())
                        {
                            originalWorker = resp.Headers["X-Worker-Id"];
                            Console.WriteLine($"  Resource initially mapped to {originalWorker}");
                        }
                    }

                    // Add a third worker with its own token
                    var worker3Token = new CancellationTokenSource();
                    workerTokens.Add(worker3Token);
                    var worker3 = new TestWorker(controller.Logging, "localhost", 8001, false, 3, worker3Token);
                    workers.Add(worker3);
                    await worker3.Start();
                    await Task.Delay(2000);

                    AssertEquals("Worker count after addition", 3, controller.Workers.Count);

                    // Verify resource still maps to original worker
                    using (RestRequest req = new RestRequest($"http://localhost:8000{resource}", HttpMethod.Get))
                    {
                        using (RestResponse resp = await req.SendAsync())
                        {
                            AssertEquals("Resource still on original worker", originalWorker, resp.Headers["X-Worker-Id"]);
                        }
                    }
                }
                finally
                {
                    for (int i = 0; i < workers.Count; i++)
                    {
                        try
                        {
                            workerTokens[i]?.Cancel();
                            workers[i]?.Dispose();
                        }
                        catch { }
                    }

                    foreach (var token in workerTokens)
                    {
                        token?.Dispose();
                    }
                }
            }
        }

        private static async Task TestConcurrentRequestsSameResource()
        {
            using (var controller = await CreateController())
            {
                List<TestWorker> workers = new List<TestWorker>();
                List<CancellationTokenSource> workerTokens = new List<CancellationTokenSource>();

                try
                {
                    for (int i = 1; i <= 3; i++)
                    {
                        var workerToken = new CancellationTokenSource();
                        workerTokens.Add(workerToken);
                        var worker = new TestWorker(controller.Logging, "localhost", 8001, false, i, workerToken);
                        workers.Add(worker);
                        await worker.Start();
                        await Task.Delay(1000);
                    }

                    await Task.Delay(1000);
                    AssertEquals("Worker count", 3, controller.Workers.Count);

                    string resource = "/api/concurrent-test";
                    int concurrentRequests = 20;
                    var tasks = new List<Task<string>>();

                    // Send concurrent requests to the same resource
                    for (int i = 0; i < concurrentRequests; i++)
                    {
                        int requestId = i;
                        tasks.Add(Task.Run(async () =>
                        {
                            using (RestRequest req = new RestRequest($"http://localhost:8000{resource}", HttpMethod.Post))
                            {
                                using (RestResponse resp = await req.SendAsync($"Concurrent {requestId}"))
                                {
                                    return resp.Headers["X-Worker-Id"];
                                }
                            }
                        }));
                    }

                    var workerIds = await Task.WhenAll(tasks);

                    // All requests should go to the same worker
                    var uniqueWorkers = workerIds.Distinct().ToList();
                    AssertEquals("All requests to same worker", 1, uniqueWorkers.Count);
                    Console.WriteLine($"  All {concurrentRequests} concurrent requests handled by {uniqueWorkers[0]}");
                }
                finally
                {
                    for (int i = 0; i < workers.Count; i++)
                    {
                        try
                        {
                            workerTokens[i]?.Cancel();
                            workers[i]?.Dispose();
                        }
                        catch { }
                    }

                    foreach (var token in workerTokens)
                    {
                        token?.Dispose();
                    }
                }
            }
        }

        private static async Task TestLoadDistributionAcrossWorkers()
        {
            using (var controller = await CreateController())
            {
                List<TestWorker> workers = new List<TestWorker>();
                List<CancellationTokenSource> workerTokens = new List<CancellationTokenSource>();

                try
                {
                    for (int i = 1; i <= 4; i++)
                    {
                        var workerToken = new CancellationTokenSource();
                        workerTokens.Add(workerToken);
                        var worker = new TestWorker(controller.Logging, "localhost", 8001, false, i, workerToken);
                        workers.Add(worker);
                        await worker.Start();
                        await Task.Delay(1000);
                    }

                    await Task.Delay(1000);
                    AssertEquals("Worker count", 4, controller.Workers.Count);

                    // Create many unique resources
                    Dictionary<string, int> workerLoad = new Dictionary<string, int>();
                    int totalResources = 40;

                    for (int i = 0; i < totalResources; i++)
                    {
                        string resource = $"/api/load-test/resource-{i}";
                        using (RestRequest req = new RestRequest($"http://localhost:8000{resource}", HttpMethod.Get))
                        {
                            using (RestResponse resp = await req.SendAsync())
                            {
                                string workerId = resp.Headers["X-Worker-Id"];
                                if (!workerLoad.ContainsKey(workerId))
                                    workerLoad[workerId] = 0;
                                workerLoad[workerId]++;
                            }
                        }
                    }

                    // Display distribution
                    Console.WriteLine($"  Resource distribution across {workers.Count} workers:");
                    foreach (var kvp in workerLoad.OrderBy(k => k.Key))
                    {
                        double percentage = (kvp.Value * 100.0) / totalResources;
                        Console.WriteLine($"    {kvp.Key}: {kvp.Value} resources ({percentage:F1}%)");
                    }
                }
                finally
                {
                    for (int i = 0; i < workers.Count; i++)
                    {
                        try
                        {
                            workerTokens[i]?.Cancel();
                            workers[i]?.Dispose();
                        }
                        catch { }
                    }

                    foreach (var token in workerTokens)
                    {
                        token?.Dispose();
                    }
                }
            }
        }

        private static async Task TestAdminApiWorkersWithValidKey()
        {
            using (TestController controller = await CreateController())
            {
                List<TestWorker> workers = new List<TestWorker>();
                List<CancellationTokenSource> workerTokens = new List<CancellationTokenSource>();

                try
                {
                    // Start 2 workers
                    for (int i = 1; i <= 2; i++)
                    {
                        CancellationTokenSource workerToken = new CancellationTokenSource();
                        workerTokens.Add(workerToken);
                        TestWorker worker = new TestWorker(controller.Logging, "localhost", 8001, false, i, workerToken);
                        workers.Add(worker);
                        await worker.Start();
                        await Task.Delay(1000);
                    }

                    await Task.Delay(1000);
                    AssertEquals("Worker count", 2, controller.Workers.Count);

                    // Test with valid API key
                    using (RestRequest req = new RestRequest("http://localhost:8000/workers", HttpMethod.Get))
                    {
                        req.Headers.Add("x-api-key", "constellationadmin");
                        using (RestResponse resp = await req.SendAsync())
                        {
                            AssertEquals("Status Code with valid key", 200, (int)resp.StatusCode);
                            AssertContains("Content-Type", "application/json", resp.ContentType);

                            List<WorkerMetadata> workerList = _Serializer.DeserializeJson<List<WorkerMetadata>>(resp.DataAsString);
                            AssertEquals("Worker count in response", 2, workerList.Count);
                            Console.WriteLine($"  Retrieved {workerList.Count} workers via admin API");
                        }
                    }
                }
                finally
                {
                    for (int i = 0; i < workers.Count; i++)
                    {
                        try
                        {
                            workerTokens[i]?.Cancel();
                            workers[i]?.Dispose();
                        }
                        catch { }
                    }

                    foreach (CancellationTokenSource token in workerTokens)
                    {
                        token?.Dispose();
                    }
                }
            }
        }

        private static async Task TestAdminApiWorkersWithInvalidKey()
        {
            using (TestController controller = await CreateController())
            {
                // Test with invalid API key
                using (RestRequest req = new RestRequest("http://localhost:8000/workers", HttpMethod.Get))
                {
                    req.Headers.Add("x-api-key", "invalid-key-12345");
                    using (RestResponse resp = await req.SendAsync())
                    {
                        // Should return 401 Unauthorized
                        AssertEquals("Status Code with invalid key", 401, (int)resp.StatusCode);
                        AssertContains("Response Body", "Authorization", resp.DataAsString);
                    }
                }
            }
        }

        private static async Task TestAdminApiWorkersWithoutKey()
        {
            using (TestController controller = await CreateController())
            {
                // Test without API key - should be treated as normal proxy request
                using (RestRequest req = new RestRequest("http://localhost:8000/workers", HttpMethod.Get))
                {
                    using (RestResponse resp = await req.SendAsync())
                    {
                        // Should return 502 because no workers are available
                        AssertEquals("Status Code without key (no workers)", 502, (int)resp.StatusCode);
                        AssertContains("Response Body", "No workers available", resp.DataAsString);
                    }
                }
            }
        }

        private static async Task TestAdminApiMapsWithValidKey()
        {
            using (TestController controller = await CreateController())
            {
                List<TestWorker> workers = new List<TestWorker>();
                List<CancellationTokenSource> workerTokens = new List<CancellationTokenSource>();

                try
                {
                    // Start 2 workers
                    for (int i = 1; i <= 2; i++)
                    {
                        CancellationTokenSource workerToken = new CancellationTokenSource();
                        workerTokens.Add(workerToken);
                        TestWorker worker = new TestWorker(controller.Logging, "localhost", 8001, false, i, workerToken);
                        workers.Add(worker);
                        await worker.Start();
                        await Task.Delay(1000);
                    }

                    await Task.Delay(1000);

                    // Create some resource mappings
                    string[] resources = { "/api/users", "/api/products", "/api/orders" };
                    foreach (string resource in resources)
                    {
                        using (RestRequest req = new RestRequest($"http://localhost:8000{resource}", HttpMethod.Get))
                        {
                            using (RestResponse resp = await req.SendAsync())
                            {
                                AssertEquals($"Resource {resource} mapped", 200, (int)resp.StatusCode);
                            }
                        }
                    }

                    // Test /maps endpoint with valid API key
                    using (RestRequest req = new RestRequest("http://localhost:8000/maps", HttpMethod.Get))
                    {
                        req.Headers.Add("x-api-key", "constellationadmin");
                        using (RestResponse resp = await req.SendAsync())
                        {
                            AssertEquals("Status Code with valid key", 200, (int)resp.StatusCode);
                            AssertContains("Content-Type", "application/json", resp.ContentType);

                            Dictionary<Guid, List<string>> resourceMap = _Serializer.DeserializeJson<Dictionary<Guid, List<string>>>(resp.DataAsString);
                            AssertTrue("Resource map not empty", resourceMap.Count > 0);

                            int totalResources = 0;
                            foreach (KeyValuePair<Guid, List<string>> kvp in resourceMap)
                            {
                                totalResources += kvp.Value.Count;
                                Console.WriteLine($"  Worker {kvp.Key}: {kvp.Value.Count} resource(s)");
                            }

                            AssertEquals("Total resources mapped", 3, totalResources);
                        }
                    }
                }
                finally
                {
                    for (int i = 0; i < workers.Count; i++)
                    {
                        try
                        {
                            workerTokens[i]?.Cancel();
                            workers[i]?.Dispose();
                        }
                        catch { }
                    }

                    foreach (CancellationTokenSource token in workerTokens)
                    {
                        token?.Dispose();
                    }
                }
            }
        }

        private static async Task TestAdminApiMapsWithInvalidKey()
        {
            using (TestController controller = await CreateController())
            {
                // Test with invalid API key
                using (RestRequest req = new RestRequest("http://localhost:8000/maps", HttpMethod.Get))
                {
                    req.Headers.Add("x-api-key", "wrong-api-key-999");
                    using (RestResponse resp = await req.SendAsync())
                    {
                        // Should return 401 Unauthorized
                        AssertEquals("Status Code with invalid key", 401, (int)resp.StatusCode);
                        AssertContains("Response Body", "Authorization", resp.DataAsString);
                    }
                }
            }
        }

        private static async Task TestAdminApiMapsWithoutKey()
        {
            using (TestController controller = await CreateController())
            {
                // Test without API key - should be treated as normal proxy request
                using (RestRequest req = new RestRequest("http://localhost:8000/maps", HttpMethod.Get))
                {
                    using (RestResponse resp = await req.SendAsync())
                    {
                        // Should return 502 because no workers are available
                        AssertEquals("Status Code without key (no workers)", 502, (int)resp.StatusCode);
                        AssertContains("Response Body", "No workers available", resp.DataAsString);
                    }
                }
            }
        }

        private static void AssertEquals<T>(string name, T expected, T actual)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual))
            {
                Console.WriteLine($"  ✓ {name}: {actual}");
                _TestsPassed++;
            }
            else
            {
                Console.WriteLine($"  ✗ {name}: Expected {expected}, got {actual}");
                _TestsFailed++;
            }
        }

        private static void AssertNotEquals<T>(string name, T notExpected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(notExpected, actual))
            {
                Console.WriteLine($"  ✓ {name}: {actual} != {notExpected}");
                _TestsPassed++;
            }
            else
            {
                Console.WriteLine($"  ✗ {name}: Expected different from {notExpected}, got {actual}");
                _TestsFailed++;
            }
        }

        private static void AssertTrue(string name, bool condition)
        {
            if (condition)
            {
                Console.WriteLine($"  ✓ {name}");
                _TestsPassed++;
            }
            else
            {
                Console.WriteLine($"  ✗ {name}: Expected true, got false");
                _TestsFailed++;
            }
        }

        private static void AssertContains(string name, string substring, string text)
        {
            if (text != null && text.Contains(substring))
            {
                Console.WriteLine($"  ✓ {name} contains '{substring}'");
                _TestsPassed++;
            }
            else
            {
                Console.WriteLine($"  ✗ {name}: Expected to contain '{substring}', got '{text}'");
                _TestsFailed++;
            }
        }

        private static void PrintTestSummary()
        {
            Console.WriteLine("\n" + new string('=', 80));
            Console.WriteLine("=== TEST SUMMARY ===");
            Console.WriteLine(new string('=', 80));

            int testNumber = 1;
            int testsPassedCount = 0;
            int testsFailedCount = 0;

            foreach (TestResult result in _TestResults)
            {
                string status = result.Passed ? "PASS" : "FAIL";
                string statusSymbol = result.Passed ? "✓" : "✗";

                Console.WriteLine($"{statusSymbol} Test {testNumber}: {result.Name} - {status}");

                if (!result.Passed && result.ErrorMessage != null)
                {
                    Console.WriteLine($"    Error: {result.ErrorMessage}");
                }

                if (result.AssertionsFailed > 0)
                {
                    Console.WriteLine($"    Assertions: {result.AssertionsPassed} passed, {result.AssertionsFailed} failed");
                }

                if (result.Passed)
                    testsPassedCount++;
                else
                    testsFailedCount++;

                testNumber++;
            }

            Console.WriteLine(new string('-', 80));
            Console.WriteLine($"Total Tests: {_TestResults.Count}");
            Console.WriteLine($"Tests Passed: {testsPassedCount}");
            Console.WriteLine($"Tests Failed: {testsFailedCount}");
            Console.WriteLine($"Total Assertions: {_TestsPassed + _TestsFailed} ({_TestsPassed} passed, {_TestsFailed} failed)");
            Console.WriteLine(new string('=', 80));

            if (testsFailedCount == 0)
            {
                Console.WriteLine("OVERALL RESULT: PASS ✓");
            }
            else
            {
                Console.WriteLine("OVERALL RESULT: FAIL ✗");
            }

            Console.WriteLine(new string('=', 80));
        }
    }

    // Test Result
    public class TestResult
    {
        public string Name { get; set; }
        public bool Passed { get; set; }
        public string ErrorMessage { get; set; }
        public int AssertionsPassed { get; set; }
        public int AssertionsFailed { get; set; }
    }

    // Test Controller
    public class TestController : ConstellationControllerBase
    {
        public LoggingModule Logging { get; private set; }
        public CancellationTokenSource TokenSource { get; private set; }
        private bool _isDisposing = false;

        public override async Task OnConnection(Guid guid, string ipAddress, int port)
        {
            if (!_isDisposing)
                Console.WriteLine($"  [Controller] Worker connected: {guid}");
        }

        public override async Task OnDisconnection(Guid guid, string ipAddress, int port)
        {
            if (!_isDisposing)
                Console.WriteLine($"  [Controller] Worker disconnected: {guid}");
        }

        public TestController(Settings settings, LoggingModule logging, CancellationTokenSource tokenSource)
            : base(settings, logging, tokenSource)
        {
            Logging = logging;
            TokenSource = tokenSource;
        }

        protected override void Dispose(bool disposing)
        {
            _isDisposing = true;
            Console.WriteLine("  [Controller] Shutting down...");

            // Stop accepting new connections first
            if (disposing)
            {
                try
                {
                    this.Stop().Wait(5000);
                }
                catch { }
            }

            base.Dispose(disposing);
            Console.WriteLine("  [Controller] Shutdown complete");
        }
    }

    // Simplified Test Worker
    public class TestWorker : ConstellationWorkerBase
    {
        private int _NodeNumber;
        private string _Header;
        private bool _disposed = false;

        public override async Task OnConnection(Guid guid)
        {
            if (!_disposed)
                Console.WriteLine($"  {_Header}Connected");
        }

        public override async Task OnDisconnection(Guid guid)
        {
            if (!_disposed)
                Console.WriteLine($"  {_Header}Disconnected");
        }

        public override async Task<WebsocketMessage> OnRequestReceived(WebsocketMessage req)
        {
            if (req.Type.Equals(WebsocketMessageTypeEnum.Heartbeat)) return null;

            var resp = new WebsocketMessage
            {
                GUID = req.GUID,
                Type = WebsocketMessageTypeEnum.Response,
                ContentType = Constants.TextContentType,
                Headers = new System.Collections.Specialized.NameValueCollection(),
                Data = Encoding.UTF8.GetBytes($"Response from worker {_NodeNumber}")
            };

            resp.Headers.Add("X-Worker-Id", $"worker-{_NodeNumber}");
            return resp;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                Console.WriteLine($"  {_Header}Disposing...");
                try
                {
                    base.Dispose(disposing);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  {_Header}Disposal error (expected): {ex.GetType().Name}");
                }
            }
        }

        public TestWorker(LoggingModule logging, string hostname, int port, bool ssl, int nodeNumber, CancellationTokenSource tokenSource)
            : base(logging, hostname, port, ssl, tokenSource)
        {
            _NodeNumber = nodeNumber;
            _Header = $"[Worker{_NodeNumber}] ";
        }

#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    }
}