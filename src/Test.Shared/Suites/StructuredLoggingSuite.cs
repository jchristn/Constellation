namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using Touchstone.Core;

    /// <summary>
    /// Proves the controller's structured <see cref="ILogger"/> integration (Microsoft.Extensions.Logging.Abstractions):
    /// the no-op default, null handling, worker lifecycle and proxy failure records with named template values, and
    /// that a throwing logger never affects request handling.
    /// </summary>
    public static class StructuredLoggingSuite
    {
        private const string SuiteId = "StructuredLogging";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("DefaultAndNull", "Logger defaults to NullLogger and null restores NullLogger", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        Check.True(env.Controller.Logger is NullLogger, "default is NullLogger");

                        CapturingLogger logger = new CapturingLogger();
                        env.Controller.Logger = logger;
                        Check.True(ReferenceEquals(logger, env.Controller.Logger), "custom logger retained");

                        env.Controller.Logger = null;
                        Check.True(env.Controller.Logger is NullLogger, "null restores NullLogger");
                    }
                }),

                Case("WorkerLifecycle", "Worker connect and disconnect are logged with WorkerId and WorkerCount values", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        CapturingLogger logger = new CapturingLogger();
                        env.Controller.Logger = logger;

                        TestWorkerHarness worker = await env.AddWorkerAsync(1, 1200);
                        Check.True(await env.WaitForWorkerCountAsync(1), "worker connected");

                        CapturedLogEntry connected = await logger.WaitForAsync(e => e.Template != null && e.Template.StartsWith("Worker {WorkerId} connected"));
                        Check.NotNull(connected, "connected entry logged");
                        Check.Equal(LogLevel.Information, connected.Level, "connected level");
                        Check.True(connected.Values.ContainsKey("WorkerId"), "connected carries WorkerId");
                        Check.Equal("1", Convert.ToString(connected.Values["WorkerCount"]), "connected carries WorkerCount");
                        Check.Contains("connected; total workers 1", connected.Message, "connected message rendered");

                        await env.RemoveWorkerAsync(worker, 1500);
                        Check.True(await env.WaitForWorkerCountAsync(0), "worker disconnected");

                        CapturedLogEntry disconnected = await logger.WaitForAsync(e => e.Template != null && e.Template.StartsWith("Worker {WorkerId} disconnected"));
                        Check.NotNull(disconnected, "disconnected entry logged");
                        Check.Equal(LogLevel.Information, disconnected.Level, "disconnected level");
                        Check.Equal(Convert.ToString(connected.Values["WorkerId"]), Convert.ToString(disconnected.Values["WorkerId"]), "same WorkerId");
                        Check.True(disconnected.Values.ContainsKey("SessionSeconds"), "disconnected carries SessionSeconds");
                    }
                }),

                Case("NoWorkerWarning", "A request with no workers logs a warning carrying the resource", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        CapturingLogger logger = new CapturingLogger();
                        env.Controller.Logger = logger;

                        HttpTestResponse resp = await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/api/unlogged");
                        Check.Equal(502, resp.StatusCode, "status");

                        CapturedLogEntry warning = await logger.WaitForAsync(e => e.Template != null && e.Template.StartsWith("No healthy worker available"));
                        Check.NotNull(warning, "warning logged");
                        Check.Equal(LogLevel.Warning, warning.Level, "warning level");
                        Check.Equal("/api/unlogged", Convert.ToString(warning.Values["Resource"]), "resource value");
                        Check.True(warning.Values.ContainsKey("Decision"), "decision value");
                    }
                }),

                Case("ThrowingLoggerContained", "A logger that throws never affects request handling or worker registration", async ct =>
                {
                    using (IntegrationEnvironment env = await IntegrationEnvironment.CreateAsync())
                    {
                        CapturingLogger logger = new CapturingLogger { ThrowOnLog = true };
                        env.Controller.Logger = logger;

                        HttpTestResponse noWorker = await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/api/throwing");
                        Check.Equal(502, noWorker.StatusCode, "no-worker response unaffected");

                        await env.AddWorkerAsync(1, 1200);
                        Check.True(await env.WaitForWorkerCountAsync(1), "worker registered despite throwing logger");

                        HttpTestResponse proxied = await HttpTestClient.SendAsync(HttpMethod.Get, env.BaseUrl + "/api/throwing");
                        Check.Equal(200, proxied.StatusCode, "proxied response unaffected");
                        Check.True(logger.Entries.Count >= 2, "entries were attempted");
                    }
                })
            };

            return new TestSuiteDescriptor(SuiteId, "Structured Logging", cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> execute)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, execute);
        }
    }
}
