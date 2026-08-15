namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Touchstone.Core;

    /// <summary>
    /// Tests for ConstellationWorkerBase property behavior via the concrete TestWorkerHarness.
    /// These tests construct workers but never start them, so no network activity occurs.
    /// </summary>
    public static class WorkerBaseSuite
    {
        private const string SuiteId = "WorkerBase";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("Construction", "A constructed worker exposes expected properties", ct =>
                {
                    LoggingModule logging = QuietLogging();
                    CancellationTokenSource cts = new CancellationTokenSource();
                    TestWorkerHarness worker = new TestWorkerHarness(logging, "localhost", 8123, false, 1, cts);

                    Check.NotEqual(Guid.Empty, worker.GUID, "GUID non-empty");
                    Check.False(worker.IsConnected, "not connected before start");
                    Check.Equal("localhost", worker.ServerHostname, "hostname");
                    Check.Equal(8123, worker.ServerPort, "port");
                    Check.False(worker.ServerSsl, "ssl false");
                    Check.Equal("http://localhost:8123", worker.ControllerUrl, "controller URL");
                    Check.Equal(1, worker.NodeNumber, "node number");

                    cts.Dispose();
                    return Task.CompletedTask;
                }),

                Case("SslUrl", "An SSL worker produces an https controller URL", ct =>
                {
                    LoggingModule logging = QuietLogging();
                    CancellationTokenSource cts = new CancellationTokenSource();
                    TestWorkerHarness worker = new TestWorkerHarness(logging, "example.com", 443, true, 9, cts);

                    Check.True(worker.ServerSsl, "ssl true");
                    Check.Contains("https://", worker.ControllerUrl, "https URL scheme");
                    Check.Equal("https://example.com:443", worker.ControllerUrl, "full https URL");

                    cts.Dispose();
                    return Task.CompletedTask;
                }),

                Case("ConnectionCheckIntervalDefault", "ConnectionCheckIntervalMs defaults to 5000", ct =>
                {
                    LoggingModule logging = QuietLogging();
                    CancellationTokenSource cts = new CancellationTokenSource();
                    TestWorkerHarness worker = new TestWorkerHarness(logging, "localhost", 8123, false, 1, cts);
                    Check.Equal(5000, worker.ConnectionCheckIntervalMs, "default interval");
                    cts.Dispose();
                    return Task.CompletedTask;
                }),

                Case("ConnectionCheckIntervalGuard", "ConnectionCheckIntervalMs rejects values below 1000", ct =>
                {
                    LoggingModule logging = QuietLogging();
                    CancellationTokenSource cts = new CancellationTokenSource();
                    TestWorkerHarness worker = new TestWorkerHarness(logging, "localhost", 8123, false, 1, cts);
                    Check.Throws<ArgumentOutOfRangeException>(() => worker.ConnectionCheckIntervalMs = 999, "interval < 1000 rejected");
                    worker.ConnectionCheckIntervalMs = 2000;
                    Check.Equal(2000, worker.ConnectionCheckIntervalMs, "interval 2000 accepted");
                    cts.Dispose();
                    return Task.CompletedTask;
                }),

                Case("NullLoggingTolerated", "A worker constructed with null logging still initializes", ct =>
                {
                    CancellationTokenSource cts = new CancellationTokenSource();
                    TestWorkerHarness worker = new TestWorkerHarness(null, "localhost", 8123, false, 2, cts);
                    Check.NotEqual(Guid.Empty, worker.GUID, "GUID non-empty with null logging");
                    cts.Dispose();
                    return Task.CompletedTask;
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Worker Base Class", cases);
        }

        private static LoggingModule QuietLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> execute)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, execute);
        }
    }
}
