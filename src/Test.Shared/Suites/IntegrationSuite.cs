namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Controller;
    using Constellation.Core.Serialization;
    using Touchstone.Core;

    /// <summary>
    /// End-to-end integration tests that stand up a real controller and real workers over
    /// live HTTP and WebSocket sockets.  These migrate and expand the original Test project's
    /// resource-pinning, failover, load-distribution, and admin-API scenarios.  Each case runs
    /// on its own unique port pair so the suite is safe to run under any runner, in parallel.
    /// </summary>
    public static class IntegrationSuite
    {
        private const string SuiteId = "Integration";
        private static readonly Serializer _Serializer = new Serializer();

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("NoWorkersAvailable", "A request with no workers returns 502", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        HttpTestResponse resp = await Get(env, "/api/users");
                        Check.Equal(502, resp.StatusCode, "status code");
                        Check.Contains("No workers available", resp.Body, "no workers body");
                    }
                }),

                Case("SingleWorkerPinning", "A single worker handles all resources consistently", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        await env.AddWorkerAsync(1, 2000);
                        Check.True(await env.WaitForWorkerCountAsync(1), "worker connected");

                        for (int i = 0; i < 5; i++)
                        {
                            HttpTestResponse resp = await Get(env, "/api/users");
                            Check.Equal(200, resp.StatusCode, "status " + i);
                            Check.Equal("Response from worker 1", resp.Body, "body " + i);
                            Check.Equal("worker-1", resp.Header("X-Worker-Id"), "worker header " + i);
                        }

                        HttpTestResponse other = await Get(env, "/api/products");
                        Check.Equal(200, other.StatusCode, "other resource status");
                        Check.Equal("worker-1", other.Header("X-Worker-Id"), "other resource worker");
                    }
                }),

                Case("MultipleWorkerPinning", "Resources pin consistently across multiple workers", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        for (int i = 1; i <= 3; i++) await env.AddWorkerAsync(i, 1500);
                        Check.True(await env.WaitForWorkerCountAsync(3), "3 workers connected");

                        string[] resources = { "/api/users", "/api/products", "/api/orders", "/api/customers", "/api/inventory", "/api/reports" };
                        Dictionary<string, string> mapping = new Dictionary<string, string>();
                        foreach (string r in resources)
                        {
                            HttpTestResponse resp = await Get(env, r);
                            Check.Equal(200, resp.StatusCode, "status for " + r);
                            mapping[r] = resp.Header("X-Worker-Id");
                        }

                        foreach (string r in resources)
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                HttpTestResponse resp = await Get(env, r);
                                Check.Equal(mapping[r], resp.Header("X-Worker-Id"), $"consistency {r} #{i}");
                            }
                        }

                        int distinctWorkers = mapping.Values.Distinct().Count();
                        Check.True(distinctWorkers >= 2, "resources distributed across multiple workers");
                    }
                }),

                Case("RemapAfterWorkerFailure", "A resource remaps to a new worker after its worker fails", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        Dictionary<int, TestWorkerHarness> workers = new Dictionary<int, TestWorkerHarness>();
                        for (int i = 1; i <= 3; i++) workers[i] = await env.AddWorkerAsync(i, 1200);
                        Check.True(await env.WaitForWorkerCountAsync(3), "3 workers connected");

                        HttpTestResponse initial = await Get(env, "/api/users");
                        string ownerHeader = initial.Header("X-Worker-Id");
                        int ownerNode = int.Parse(ownerHeader.Split('-')[1]);

                        await env.RemoveWorkerAsync(workers[ownerNode], 1500);
                        Check.True(await env.WaitForWorkerCountAsync(2), "worker count dropped to 2");

                        HttpTestResponse remapped = await Get(env, "/api/users");
                        Check.Equal(200, remapped.StatusCode, "remapped status");
                        string newHeader = remapped.Header("X-Worker-Id");
                        Check.NotEqual(ownerHeader, newHeader, "remapped to different worker");

                        for (int i = 0; i < 3; i++)
                        {
                            HttpTestResponse resp = await Get(env, "/api/users");
                            Check.Equal(newHeader, resp.Header("X-Worker-Id"), "remap consistency " + i);
                        }
                    }
                }),

                Case("MultipleResourcesPerWorker", "Two workers both receive distinct resources", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        await env.AddWorkerAsync(1, 1200);
                        await env.AddWorkerAsync(2, 1200);
                        Check.True(await env.WaitForWorkerCountAsync(2), "2 workers connected");

                        Dictionary<string, int> counts = new Dictionary<string, int> { ["worker-1"] = 0, ["worker-2"] = 0 };
                        for (int i = 0; i < 10; i++)
                        {
                            HttpTestResponse resp = await Get(env, $"/api/resource{i}");
                            counts[resp.Header("X-Worker-Id")]++;
                        }

                        Check.True(counts["worker-1"] > 0 && counts["worker-2"] > 0, "both workers used");
                    }
                }),

                Case("WorkerRecoveryPersistence", "Adding a worker does not disturb existing mappings", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        await env.AddWorkerAsync(1, 1200);
                        await env.AddWorkerAsync(2, 1200);
                        Check.True(await env.WaitForWorkerCountAsync(2), "2 workers connected");

                        HttpTestResponse first = await Get(env, "/api/persistent");
                        string owner = first.Header("X-Worker-Id");

                        await env.AddWorkerAsync(3, 2000);
                        Check.True(await env.WaitForWorkerCountAsync(3), "3 workers connected");

                        HttpTestResponse after = await Get(env, "/api/persistent");
                        Check.Equal(owner, after.Header("X-Worker-Id"), "mapping preserved after scale-up");
                    }
                }),

                Case("ConcurrentSameResource", "Concurrent requests to one resource hit one worker", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        for (int i = 1; i <= 3; i++) await env.AddWorkerAsync(i, 1200);
                        Check.True(await env.WaitForWorkerCountAsync(3), "3 workers connected");

                        List<Task<HttpTestResponse>> tasks = new List<Task<HttpTestResponse>>();
                        for (int i = 0; i < 20; i++)
                        {
                            tasks.Add(HttpTestClient.SendAsync(HttpMethod.Post, env.BaseUrl + "/api/concurrent", "payload"));
                        }

                        HttpTestResponse[] responses = await Task.WhenAll(tasks);
                        List<string> distinct = responses.Select(r => r.Header("X-Worker-Id")).Distinct().ToList();
                        Check.Equal(1, distinct.Count, "all concurrent requests hit a single worker");
                    }
                }),

                Case("LoadDistribution", "Many resources distribute across all workers", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        for (int i = 1; i <= 4; i++) await env.AddWorkerAsync(i, 1000);
                        Check.True(await env.WaitForWorkerCountAsync(4), "4 workers connected");

                        HashSet<string> used = new HashSet<string>();
                        for (int i = 0; i < 40; i++)
                        {
                            HttpTestResponse resp = await Get(env, $"/api/load/{i}");
                            used.Add(resp.Header("X-Worker-Id"));
                        }

                        Check.True(used.Count >= 2, "load spread across multiple workers");
                    }
                }),

                Case("AdminWorkersValidKey", "GET /workers with a valid API key lists workers", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        await env.AddWorkerAsync(1, 1200);
                        await env.AddWorkerAsync(2, 1200);
                        Check.True(await env.WaitForWorkerCountAsync(2), "2 workers connected");

                        HttpTestResponse resp = await Get(env, "/workers", ApiKey("constellationadmin"));
                        Check.Equal(200, resp.StatusCode, "status code");
                        Check.Contains("application/json", resp.ContentType, "content type");

                        List<WorkerMetadata> workers = _Serializer.DeserializeJson<List<WorkerMetadata>>(resp.Body);
                        Check.Equal(2, workers.Count, "worker count in response");
                    }
                }),

                Case("AdminWorkersInvalidKey", "GET /workers with an invalid API key returns 401", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        HttpTestResponse resp = await Get(env, "/workers", ApiKey("wrong-key"));
                        Check.Equal(401, resp.StatusCode, "status code");
                        Check.Contains("Authorization", resp.Body, "authorization failure body");
                    }
                }),

                Case("AdminWorkersNoKey", "GET /workers without a key is treated as a proxy request", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        HttpTestResponse resp = await Get(env, "/workers");
                        Check.Equal(502, resp.StatusCode, "status code");
                        Check.Contains("No workers available", resp.Body, "no workers body");
                    }
                }),

                Case("AdminMapsValidKey", "GET /maps with a valid API key returns the resource map", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        await env.AddWorkerAsync(1, 1200);
                        await env.AddWorkerAsync(2, 1200);
                        Check.True(await env.WaitForWorkerCountAsync(2), "2 workers connected");

                        string[] resources = { "/api/users", "/api/products", "/api/orders" };
                        foreach (string r in resources)
                        {
                            HttpTestResponse mapResp = await Get(env, r);
                            Check.Equal(200, mapResp.StatusCode, "mapped " + r);
                        }

                        HttpTestResponse resp = await Get(env, "/maps", ApiKey("constellationadmin"));
                        Check.Equal(200, resp.StatusCode, "status code");
                        Check.Contains("application/json", resp.ContentType, "content type");

                        Dictionary<Guid, List<string>> map = _Serializer.DeserializeJson<Dictionary<Guid, List<string>>>(resp.Body);
                        int total = map.Values.Sum(v => v.Count);
                        Check.Equal(3, total, "total mapped resources");
                    }
                }),

                Case("AdminMapsInvalidKey", "GET /maps with an invalid API key returns 401", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        HttpTestResponse resp = await Get(env, "/maps", ApiKey("nope"));
                        Check.Equal(401, resp.StatusCode, "status code");
                        Check.Contains("Authorization", resp.Body, "authorization failure body");
                    }
                }),

                Case("AdminMapsNoKey", "GET /maps without a key is treated as a proxy request", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        HttpTestResponse resp = await Get(env, "/maps");
                        Check.Equal(502, resp.StatusCode, "status code");
                        Check.Contains("No workers available", resp.Body, "no workers body");
                    }
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Controller/Worker Integration", cases);
        }

        private static Task<HttpTestResponse> Get(IntegrationEnvironment env, string path, IDictionary<string, string> headers = null)
        {
            return HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + path, null, headers);
        }

        private static IDictionary<string, string> ApiKey(string key)
        {
            return new Dictionary<string, string> { ["x-api-key"] = key };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> execute)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, execute);
        }
    }
}
