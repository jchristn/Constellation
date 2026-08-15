namespace Test.Shared
{
    using System;
    using System.Collections.Specialized;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Core;
    using Constellation.Worker;
    using SyslogLogging;

    /// <summary>
    /// Minimal echo worker used by the integration test suite.  Each response includes an
    /// "X-Worker-Id" header of the form "worker-{nodeNumber}" so that tests can verify
    /// which worker handled a request.
    /// </summary>
    public class TestWorkerHarness : ConstellationWorkerBase
    {
        private readonly int _NodeNumber;
        private bool _Disposed = false;

        /// <summary>
        /// Node number assigned to this worker.
        /// </summary>
        public int NodeNumber
        {
            get => _NodeNumber;
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="hostname">Controller hostname.</param>
        /// <param name="port">Controller WebSocket port.</param>
        /// <param name="ssl">Enable or disable SSL.</param>
        /// <param name="nodeNumber">Node number.</param>
        /// <param name="tokenSource">Cancellation token source.</param>
        public TestWorkerHarness(LoggingModule logging, string hostname, int port, bool ssl, int nodeNumber, CancellationTokenSource tokenSource)
            : base(logging, hostname, port, ssl, tokenSource)
        {
            _NodeNumber = nodeNumber;
        }

        /// <inheritdoc />
        public override Task OnConnection(Guid guid)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task OnDisconnection(Guid guid)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task<WebsocketMessage> OnRequestReceived(WebsocketMessage req)
        {
            if (req.Type.Equals(WebsocketMessageTypeEnum.Heartbeat)) return Task.FromResult<WebsocketMessage>(null);

            WebsocketMessage resp = new WebsocketMessage
            {
                GUID = req.GUID,
                Type = WebsocketMessageTypeEnum.Response,
                StatusCode = 200,
                ContentType = Constants.TextContentType,
                Headers = new NameValueCollection(),
                Data = Encoding.UTF8.GetBytes($"Response from worker {_NodeNumber}")
            };

            resp.Headers.Add("X-Worker-Id", $"worker-{_NodeNumber}");
            return Task.FromResult(resp);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                _Disposed = true;
                try
                {
                    base.Dispose(disposing);
                }
                catch
                {
                }
            }
        }
    }
}
