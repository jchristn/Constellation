namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Controller;
    using SyslogLogging;
    using Touchstone.Core;

    /// <summary>
    /// Unit tests for WorkerService: worker pool management, resource pinning, round-robin
    /// selection, health-aware routing, and resource-map cleanup.  These tests exercise the
    /// service directly without any network activity.
    /// </summary>
    public static class WorkerServiceSuite
    {
        private const string SuiteId = "WorkerService";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("AddWorkerNullThrows", "AddWorker rejects null", ct =>
                {
                    WorkerService svc = NewService();
                    Check.Throws<ArgumentNullException>(() => svc.AddWorker(null), "null worker rejected");
                    return Task.CompletedTask;
                }),

                Case("GetByResourceNullThrows", "GetByResource rejects null/empty resource", ct =>
                {
                    WorkerService svc = NewService();
                    Check.Throws<ArgumentNullException>(() => svc.GetByResource(null), "null resource rejected");
                    Check.Throws<ArgumentNullException>(() => svc.GetByResource(string.Empty), "empty resource rejected");
                    return Task.CompletedTask;
                }),

                Case("NoWorkersReturnsNull", "GetByResource returns null when the pool is empty", ct =>
                {
                    WorkerService svc = NewService();
                    Check.Null(svc.GetByResource("/api/users"), "no worker for resource");
                    return Task.CompletedTask;
                }),

                Case("PinningConsistency", "A resource pins to the same worker across calls", ct =>
                {
                    WorkerService svc = NewService();
                    svc.AddWorker(NewWorker());
                    svc.AddWorker(NewWorker());

                    WorkerMetadata first = svc.GetByResource("/api/users");
                    Check.NotNull(first, "first mapping not null");
                    for (int i = 0; i < 5; i++)
                    {
                        WorkerMetadata again = svc.GetByResource("/api/users");
                        Check.Equal(first.GUID, again.GUID, "pinned to same worker on call " + i);
                    }
                    return Task.CompletedTask;
                }),

                Case("RoundRobinDistribution", "Distinct resources distribute across all workers", ct =>
                {
                    WorkerService svc = NewService();
                    for (int i = 0; i < 3; i++) svc.AddWorker(NewWorker());

                    HashSet<Guid> used = new HashSet<Guid>();
                    string[] resources = { "/a", "/b", "/c", "/d", "/e", "/f" };
                    foreach (string r in resources)
                    {
                        WorkerMetadata w = svc.GetByResource(r);
                        Check.NotNull(w, "worker for " + r);
                        used.Add(w.GUID);
                    }

                    Check.Equal(3, used.Count, "all three workers received resources");
                    return Task.CompletedTask;
                }),

                Case("UnhealthyWorkerSkipped", "GetByResource never selects an unhealthy worker", ct =>
                {
                    WorkerService svc = NewService();
                    WorkerMetadata healthy = NewWorker();
                    WorkerMetadata unhealthy = NewWorker();
                    unhealthy.Healthy = false;
                    svc.AddWorker(healthy);
                    svc.AddWorker(unhealthy);

                    for (int i = 0; i < 6; i++)
                    {
                        WorkerMetadata w = svc.GetByResource("/api/resource" + i);
                        Check.Equal(healthy.GUID, w.GUID, "only healthy worker selected");
                    }
                    return Task.CompletedTask;
                }),

                Case("RemapAfterWorkerUnhealthy", "A pinned resource remaps when its worker becomes unhealthy", ct =>
                {
                    WorkerService svc = NewService();
                    WorkerMetadata a = NewWorker();
                    WorkerMetadata b = NewWorker();
                    svc.AddWorker(a);
                    svc.AddWorker(b);

                    WorkerMetadata original = svc.GetByResource("/api/pinned");
                    Check.NotNull(original, "original mapping present");

                    // Mark the owning worker unhealthy.
                    original.Healthy = false;

                    WorkerMetadata remapped = svc.GetByResource("/api/pinned");
                    Check.NotNull(remapped, "remapped mapping present");
                    Check.NotEqual(original.GUID, remapped.GUID, "remapped to a different worker");
                    Check.True(remapped.Healthy, "remapped worker is healthy");
                    return Task.CompletedTask;
                }),

                Case("RemoveWorkerClearsMappings", "Removing a worker clears its resource mappings", ct =>
                {
                    WorkerService svc = NewService();
                    WorkerMetadata a = NewWorker();
                    svc.AddWorker(a);

                    WorkerMetadata w = svc.GetByResource("/api/thing");
                    Check.Equal(a.GUID, w.GUID, "mapped to a");
                    Check.True(svc.ResourceMap.ContainsKey(a.GUID), "resource map contains a");

                    bool removed = svc.RemoveWorker(a.GUID);
                    Check.True(removed, "worker removed");
                    Check.False(svc.ResourceMap.ContainsKey(a.GUID), "resource map cleared for a");
                    return Task.CompletedTask;
                }),

                Case("RemoveUnknownWorkerReturnsFalse", "Removing an unknown worker returns false", ct =>
                {
                    WorkerService svc = NewService();
                    Check.False(svc.RemoveWorker(Guid.NewGuid()), "unknown removal returns false");
                    return Task.CompletedTask;
                }),

                Case("GetByGuid", "GetByGuid returns the matching worker or null", ct =>
                {
                    WorkerService svc = NewService();
                    WorkerMetadata a = NewWorker();
                    svc.AddWorker(a);
                    Check.Equal(a.GUID, svc.GetByGuid(a.GUID).GUID, "found by guid");
                    Check.Null(svc.GetByGuid(Guid.NewGuid()), "unknown guid returns null");
                    return Task.CompletedTask;
                }),

                Case("ClearResourceMapping", "ClearResourceMapping removes a specific resource", ct =>
                {
                    WorkerService svc = NewService();
                    WorkerMetadata a = NewWorker();
                    svc.AddWorker(a);
                    svc.GetByResource("/api/one");

                    svc.ClearResourceMapping("/api/one");
                    bool stillMapped = svc.ResourceMap.Any(kvp => kvp.Value.Contains("/api/one"));
                    Check.False(stillMapped, "resource mapping cleared");
                    return Task.CompletedTask;
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Worker Service (Routing)", cases);
        }

        private static WorkerService NewService()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new WorkerService(new Settings(), logging);
        }

        private static WorkerMetadata NewWorker()
        {
            return new WorkerMetadata
            {
                GUID = Guid.NewGuid(),
                Ip = "127.0.0.1",
                Port = 0,
                Healthy = true
            };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> execute)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, execute);
        }
    }
}
