namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Controller;
    using SyslogLogging;
    using WatsonWebserver.Core;
    using WatsonWebsocket;

    /// <summary>
    /// Helper that stands up a controller and a set of workers on a unique port pair and
    /// provides lifecycle management and readiness helpers for integration tests.  Each
    /// environment is fully isolated from every other environment because it uses its own
    /// TCP ports, allocated via <see cref="PortAllocator"/>.
    /// </summary>
    public sealed class IntegrationEnvironment : IDisposable
    {
        /// <summary>
        /// The controller under test.
        /// </summary>
        public TestControllerHarness Controller { get; private set; }

        /// <summary>
        /// HTTP port used for client (REST) requests.
        /// </summary>
        public int HttpPort { get; private set; }

        /// <summary>
        /// WebSocket port used for worker connections.
        /// </summary>
        public int WebsocketPort { get; private set; }

        /// <summary>
        /// Base URL for client (REST) requests.
        /// </summary>
        public string BaseUrl
        {
            get => $"http://localhost:{HttpPort}";
        }

        private readonly LoggingModule _Logging;
        private readonly CancellationTokenSource _ControllerToken;
        private readonly List<TestWorkerHarness> _Workers = new List<TestWorkerHarness>();
        private readonly List<CancellationTokenSource> _WorkerTokens = new List<CancellationTokenSource>();
        private readonly object _Lock = new object();
        private bool _Disposed = false;

        private IntegrationEnvironment(PortPair ports, LoggingModule logging, CancellationTokenSource token, TestControllerHarness controller)
        {
            HttpPort = ports.Http;
            WebsocketPort = ports.Websocket;
            _Logging = logging;
            _ControllerToken = token;
            Controller = controller;
        }

        /// <summary>
        /// Create and start a controller on a freshly allocated port pair.
        /// </summary>
        /// <param name="heartbeatIntervalMs">Heartbeat interval in milliseconds.</param>
        /// <param name="maxFailures">Maximum heartbeat failures before a worker is unhealthy.</param>
        /// <param name="proxyTimeoutMs">Proxy request timeout in milliseconds.</param>
        /// <returns>Integration environment.</returns>
        public static async Task<IntegrationEnvironment> CreateAsync(
            int heartbeatIntervalMs = 2000,
            int maxFailures = 3,
            int proxyTimeoutMs = 5000)
        {
            PortPair ports = PortAllocator.Next();

            Settings settings = new Settings
            {
                Webserver = new WebserverSettings
                {
                    Hostname = "localhost",
                    Port = ports.Http
                },
                Websocket = new WebsocketSettings
                {
                    Hostnames = new List<string> { "localhost" },
                    Port = ports.Websocket
                },
                Heartbeat = new HeartbeatSettings
                {
                    IntervalMs = heartbeatIntervalMs,
                    MaxFailures = maxFailures
                },
                Proxy = new ProxySettings
                {
                    TimeoutMs = proxyTimeoutMs,
                    ResponseRetentionMs = 30000
                }
            };

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            CancellationTokenSource token = new CancellationTokenSource();
            TestControllerHarness controller = new TestControllerHarness(settings, logging, token);
            await controller.Start().ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false);

            return new IntegrationEnvironment(ports, logging, token, controller);
        }

        /// <summary>
        /// Start and connect a new worker with the given node number.
        /// </summary>
        /// <param name="nodeNumber">Node number.</param>
        /// <param name="settleMs">Delay after starting the worker, in milliseconds.</param>
        /// <returns>The started worker.</returns>
        public async Task<TestWorkerHarness> AddWorkerAsync(int nodeNumber, int settleMs = 1000)
        {
            CancellationTokenSource workerToken = new CancellationTokenSource();
            TestWorkerHarness worker = new TestWorkerHarness(_Logging, "localhost", WebsocketPort, false, nodeNumber, workerToken);

            lock (_Lock)
            {
                _Workers.Add(worker);
                _WorkerTokens.Add(workerToken);
            }

            await worker.Start().ConfigureAwait(false);
            if (settleMs > 0) await Task.Delay(settleMs).ConfigureAwait(false);
            return worker;
        }

        /// <summary>
        /// Stop and dispose a worker previously added with <see cref="AddWorkerAsync"/>.
        /// </summary>
        /// <param name="worker">Worker to remove.</param>
        /// <param name="settleMs">Delay after removing the worker, in milliseconds.</param>
        /// <returns>Task.</returns>
        public async Task RemoveWorkerAsync(TestWorkerHarness worker, int settleMs = 1000)
        {
            if (worker == null) return;

            CancellationTokenSource token = null;
            lock (_Lock)
            {
                int idx = _Workers.IndexOf(worker);
                if (idx >= 0)
                {
                    token = _WorkerTokens[idx];
                    _Workers.RemoveAt(idx);
                    _WorkerTokens.RemoveAt(idx);
                }
            }

            try
            {
                token?.Cancel();
                worker.Dispose();
            }
            catch
            {
            }

            if (settleMs > 0) await Task.Delay(settleMs).ConfigureAwait(false);
            token?.Dispose();
        }

        /// <summary>
        /// Wait until the controller reports the expected number of connected workers.
        /// </summary>
        /// <param name="expectedCount">Expected worker count.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>True if the count was reached within the timeout.</returns>
        public async Task<bool> WaitForWorkerCountAsync(int expectedCount, int timeoutMs = 8000)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (Controller.Workers.Count == expectedCount) return true;
                await Task.Delay(100).ConfigureAwait(false);
            }

            return Controller.Workers.Count == expectedCount;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            List<TestWorkerHarness> workers;
            List<CancellationTokenSource> tokens;
            lock (_Lock)
            {
                workers = new List<TestWorkerHarness>(_Workers);
                tokens = new List<CancellationTokenSource>(_WorkerTokens);
                _Workers.Clear();
                _WorkerTokens.Clear();
            }

            for (int i = 0; i < workers.Count; i++)
            {
                try
                {
                    tokens[i]?.Cancel();
                    workers[i]?.Dispose();
                }
                catch
                {
                }
            }

            try
            {
                Controller?.Dispose();
            }
            catch
            {
            }

            foreach (CancellationTokenSource token in tokens)
            {
                try { token?.Dispose(); } catch { }
            }

            try { _ControllerToken?.Dispose(); } catch { }
        }
    }
}
