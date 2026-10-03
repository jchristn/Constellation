namespace Constellation.Worker
{
    using System;
    using System.Diagnostics;
    using System.Net.WebSockets;
    using System.Reflection.Metadata.Ecma335;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Core;
    using Constellation.Core.Serialization;
    using Constellation.Core.Telemetry;
    using SyslogLogging;
    using WatsonWebsocket;

    /// <summary>
    /// Constellation worker base class.
    /// </summary>
    public abstract class ConstellationWorkerBase : IDisposable
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        /// <summary>
        /// Server hostname.
        /// </summary>
        public string ServerHostname
        {
            get => _ServerHostname;
            private set => _ServerHostname = (!String.IsNullOrEmpty(value) ? value : throw new ArgumentNullException(nameof(ServerHostname)));
        }

        /// <summary>
        /// Server port.
        /// </summary>
        public int ServerPort
        {
            get => _ServerPort;
            private set => _ServerPort = (value >= 0 && value < 65536 ? value : throw new ArgumentOutOfRangeException(nameof(ServerPort)));
        }

        /// <summary>
        /// Enable or disable SSL when connecting to the server.
        /// </summary>
        public bool ServerSsl
        {
            get => _ServerSsl;
            private set => _ServerSsl = value;
        }

        /// <summary>
        /// Controller URL.
        /// </summary>
        public string ControllerUrl
        {
            get => (_ServerSsl ? "https://" : "http://") + _ServerHostname + ":" + _ServerPort;
        }

        /// <summary>
        /// GUID of the worker.
        /// </summary>
        public Guid GUID
        {
            get => _GUID;
        }

        /// <summary>
        /// Boolean indicating if the worker is connected.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                return _Websocket != null && _Websocket.Connected;
            }
        }

        /// <summary>
        /// Frequency with which the connection is checked, in milliseconds.  Minimum is 1000.  Default is 5000.
        /// </summary>
        public int ConnectionCheckIntervalMs
        {
            get => _ConnectionCheckIntervalMs;
            set => _ConnectionCheckIntervalMs = (value >= 1000 ? value : throw new ArgumentOutOfRangeException(nameof(ConnectionCheckIntervalMs)));
        }

        private string _Header = "[ConstellationWorker] ";
        private LoggingModule _Logging = null;
        private string _ServerHostname = "localhost";
        private int _ServerPort = 8000;
        private bool _ServerSsl = false;
        private Guid _GUID = Guid.NewGuid();
        private WatsonWsClient _Websocket = null;
        private int _ConnectionCheckIntervalMs = 5000;
        private Task _MaintainConnection = null;
        private Serializer _Serializer = new Serializer();

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private WorkerTelemetryState _TelemetryState = null;
        private bool _Disposed = false;

        /// <summary>
        /// Constellation worker base class.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="serverHostname">Server hostname.</param>
        /// <param name="serverPort">Server port.</param>
        /// <param name="serverSsl">Enable or disable SSL when connecting to the server.</param>
        /// <param name="tokenSource">Cancellation token source.</param>
        public ConstellationWorkerBase(LoggingModule logging, string serverHostname, int serverPort, bool serverSsl, CancellationTokenSource tokenSource = null)
        {
            _Logging = logging ?? new LoggingModule();
            _ServerHostname = serverHostname;
            _ServerPort = serverPort;
            _ServerSsl = serverSsl;

            if (tokenSource != null) _TokenSource = tokenSource;

            _Websocket = new WatsonWsClient(_ServerHostname, _ServerPort, _ServerSsl, _GUID);
            _Websocket.ServerConnected += ServerConnected;
            _Websocket.ServerDisconnected += ServerDisconnected;
            _Websocket.MessageReceived += ServerMessageReceived;

            _TelemetryState = new WorkerTelemetryState(() => IsConnected);
            ConstellationTelemetry.RegisterWorker(_TelemetryState);
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        /// <param name="disposing">Disposing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing)
                {
                    if (!_TokenSource.Token.IsCancellationRequested) _TokenSource.Cancel();

                    // Wait for the connection-maintenance loop to finish.  It may not have been
                    // started (null) and it may complete via cancellation; both are expected.
                    try
                    {
                        _MaintainConnection?.Wait(TimeSpan.FromSeconds(5));
                    }
                    catch
                    {
                    }

                    // Disposing the underlying WebSocket client can surface a TaskCanceledException
                    // when its internal receive loop is torn down; that is expected during shutdown
                    // and must not escape (this method previously ran as 'async void', which allowed
                    // such exceptions to crash the host process).
                    try
                    {
                        _Websocket?.Dispose();
                    }
                    catch
                    {
                    }
                }

                ConstellationTelemetry.UnregisterWorker(_TelemetryState);
                _Websocket = null;
                _Disposed = true;
            }
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) throw new ObjectDisposedException(nameof(ConstellationWorkerBase));
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Start the worker.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task Start()
        {
            if (_Disposed) throw new ObjectDisposedException(nameof(ConstellationWorkerBase));
            if (IsConnected) return;
            if (_MaintainConnection != null) return;
            _Logging.Debug(_Header + "starting maintain connection task");
            _MaintainConnection = Task.Run(() => MaintainConnection(), _TokenSource.Token);
        }

        /// <summary>
        /// Stop the worker.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task Stop()
        {
            if (_Disposed) throw new ObjectDisposedException(nameof(ConstellationWorkerBase));
            _Logging.Info(_Header + "websocket connection close requested");
            _Websocket?.StopAsync(WebSocketCloseStatus.NormalClosure, "The websocket connection was closed by the administrator.");
        }

        /// <summary>
        /// Method to invoke when a request is received.
        /// </summary>
        /// <param name="req">Websocket message.</param>
        /// <returns>Websocket message.</returns>
        public abstract Task<WebsocketMessage> OnRequestReceived(WebsocketMessage req);

        /// <summary>
        /// Method to invoke when connected to the server.
        /// </summary>
        /// <param name="guid">GUID.</param>
        /// <returns>Task.</returns>
        public abstract Task OnConnection(Guid guid);

        /// <summary>
        /// Method to invoke when disconnected from the server.
        /// </summary>
        /// <param name="guid">GUID.</param>
        /// <returns>Task.</returns>
        public abstract Task OnDisconnection(Guid guid);

        private void ServerDisconnected(object sender, EventArgs e)
        {
            ConstellationTelemetry.Add(ConstellationTelemetry.WorkerConnectionEvents, 1, new TagList { { TelemetryConstants.LabelEvent, TelemetryConstants.EventDisconnected } });
            if (OnDisconnection != null) InvokeLifecycle(() => OnDisconnection(GUID));
        }

        private void ServerConnected(object sender, EventArgs e)
        {
            ConstellationTelemetry.Add(ConstellationTelemetry.WorkerConnectionEvents, 1, new TagList { { TelemetryConstants.LabelEvent, TelemetryConstants.EventConnected } });
            if (OnConnection != null) InvokeLifecycle(() => OnConnection(GUID));
        }

        private void InvokeLifecycle(Func<Task> callback)
        {
            try
            {
                callback().Wait();
            }
            catch (Exception e)
            {
                ConstellationTelemetry.RecordError(TelemetryConstants.ComponentWorker, TelemetryConstants.OperationLifecycle, e is AggregateException && e.InnerException != null ? e.InnerException : e);
                throw;
            }
        }

        private async void ServerMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            int size = e.Data != null ? e.Data.Count : 0;
            long start = ConstellationTelemetry.StartTimestamp();
            Activity span = null;
            string outcome = TelemetryConstants.OutcomeError;
            bool isRequest = false;

            try
            {
                if (OnRequestReceived == null) throw new NotImplementedException("The request handler has not been implemented.");
                byte[] data = (e.Data != null ? e.Data.ToArray() : new byte[0]);
                string json = Encoding.UTF8.GetString(data);

                WebsocketMessage request = _Serializer.DeserializeJson<WebsocketMessage>(json);
                WebsocketMessage response = null;

                ConstellationTelemetry.RecordWebsocketMessage(TelemetryConstants.ComponentWorker, TelemetryConstants.DirectionReceived, request.Type, TelemetryConstants.OutcomeSuccess, size);

                if (request.Type == WebsocketMessageTypeEnum.Heartbeat)
                {
                    _Logging.Debug(_Header + "heartbeat received");

                    response = new WebsocketMessage
                    {
                        GUID = request.GUID,
                        Type = WebsocketMessageTypeEnum.Heartbeat
                    };
                }
                else
                {
                    isRequest = true;
                    span = ConstellationTelemetry.StartActivityFromTraceParent(TelemetryConstants.SpanWorkerHandle, ActivityKind.Server, request.TraceParent, request.TraceState);
                    ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrMessageId, request.GUID.ToString());
                    ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrWorkerId, GUID.ToString());
                    ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrMessagingSystem, "websocket");
                    if (!String.IsNullOrEmpty(request.Method)) ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrHttpMethod, request.Method);
                    if (request.Url != null && request.Url.Uri != null) ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrResource, request.Url.Uri.AbsolutePath);

                    _Logging.Debug(_Header + "received message type " + request.Type + " (" + data.Length + " bytes)");

                    long handlerStart = ConstellationTelemetry.StartTimestamp();
                    Activity handlerSpan = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanStagePrefix + TelemetryConstants.StageHandler, ActivityKind.Internal);
                    string handlerOutcome = TelemetryConstants.OutcomeError;
                    try
                    {
                        response = await OnRequestReceived(request);
                        handlerOutcome = response != null ? TelemetryConstants.OutcomeSuccess : TelemetryConstants.OutcomeNoResponse;
                        if (response != null) ConstellationTelemetry.SetOk(handlerSpan);
                        else ConstellationTelemetry.SetError(handlerSpan, handlerOutcome, "Request handler returned no response.");
                    }
                    catch (Exception ex)
                    {
                        ConstellationTelemetry.SetException(handlerSpan, ex);
                        throw;
                    }
                    finally
                    {
                        ConstellationTelemetry.Record(
                            ConstellationTelemetry.WorkerHandlerDuration,
                            ConstellationTelemetry.ElapsedSeconds(handlerStart),
                            new TagList { { TelemetryConstants.LabelOutcome, handlerOutcome } });
                        ConstellationTelemetry.Stop(handlerSpan);
                    }

                    if (response == null)
                    {
                        _Logging.Warn(_Header + "no response message received from message handler");
                        outcome = TelemetryConstants.OutcomeNoResponse;
                        ConstellationTelemetry.SetError(span, outcome, "Request handler returned no response.");
                        return;
                    }

                    response.GUID = request.GUID;
                    if (response.StatusCode != null) ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrHttpStatusCode, response.StatusCode.Value);
                }

                Activity replySpan = isRequest
                    ? ConstellationTelemetry.StartActivity(TelemetryConstants.SpanStagePrefix + TelemetryConstants.StageReply, ActivityKind.Producer)
                    : null;

                try
                {
                    // Continue the trace back on the controller's response-receipt span.
                    if (isRequest) ConstellationTelemetry.Inject(replySpan ?? span, response);

                    json = _Serializer.SerializeJson(response, false);
                    data = Encoding.UTF8.GetBytes(json);
                    bool sent = await _Websocket.SendAsync(data, WebSocketMessageType.Binary, _TokenSource.Token).ConfigureAwait(false);

                    ConstellationTelemetry.RecordWebsocketMessage(
                        TelemetryConstants.ComponentWorker,
                        TelemetryConstants.DirectionSent,
                        response.Type,
                        sent ? TelemetryConstants.OutcomeSuccess : TelemetryConstants.OutcomeFailure,
                        data.Length);

                    outcome = sent ? TelemetryConstants.OutcomeSuccess : TelemetryConstants.OutcomeSendFailed;
                    if (sent) ConstellationTelemetry.SetOk(replySpan);
                    else ConstellationTelemetry.SetError(replySpan, outcome, "WebSocket send to controller failed.");
                }
                catch (Exception ex)
                {
                    ConstellationTelemetry.SetException(replySpan, ex);
                    throw;
                }
                finally
                {
                    ConstellationTelemetry.Stop(replySpan);
                }

                if (isRequest)
                {
                    if (outcome == TelemetryConstants.OutcomeSuccess) ConstellationTelemetry.SetOk(span);
                    else ConstellationTelemetry.SetError(span, outcome, "WebSocket send to controller failed.");
                }
            }
            catch (Exception ex)
            {
                // An exception escaping an 'async void' handler would crash the host process; record it instead.
                outcome = ex is OperationCanceledException ? TelemetryConstants.OutcomeCanceled : TelemetryConstants.OutcomeError;
                _Logging.Warn(_Header + "exception handling message from controller:" + Environment.NewLine + ex.ToString());
                ConstellationTelemetry.RecordError(TelemetryConstants.ComponentWorker, TelemetryConstants.OperationHandle, ex);
                if (!isRequest)
                    ConstellationTelemetry.RecordWebsocketMessage(TelemetryConstants.ComponentWorker, TelemetryConstants.DirectionReceived, WebsocketMessageTypeEnum.Unknown, TelemetryConstants.OutcomeError, size);
                ConstellationTelemetry.SetException(span, ex, outcome);
            }
            finally
            {
                if (isRequest)
                {
                    TagList tags = new TagList { { TelemetryConstants.LabelOutcome, outcome } };
                    ConstellationTelemetry.Add(ConstellationTelemetry.WorkerRequests, 1, tags);
                    ConstellationTelemetry.Record(ConstellationTelemetry.WorkerRequestDuration, ConstellationTelemetry.ElapsedSeconds(start), tags);
                }

                ConstellationTelemetry.Stop(span);
            }
        }

        private async Task MaintainConnection()
        {
            bool firstRun = true;

            while (!_TokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    #region Check-Cancellation-Token

                    if (_TokenSource.Token.IsCancellationRequested) break;

                    #endregion

                    #region Wait

                    if (firstRun) firstRun = false;
                    else await Task.Delay(_ConnectionCheckIntervalMs, _TokenSource.Token).ConfigureAwait(false);

                    #endregion

                    #region Check-Connection

                    if (IsConnected) continue;

                    Activity connectSpan = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanWorkerConnect, ActivityKind.Client, default(ActivityContext));
                    ConstellationTelemetry.SetTag(connectSpan, TelemetryConstants.AttrWorkerId, _GUID.ToString());
                    ConstellationTelemetry.SetTag(connectSpan, TelemetryConstants.AttrServerAddress, _ServerHostname);
                    ConstellationTelemetry.SetTag(connectSpan, TelemetryConstants.AttrServerPort, _ServerPort);
                    string connectOutcome = TelemetryConstants.OutcomeError;

                    try
                    {
                        _Logging.Debug(_Header + "worker is not connected, attempting reconnection");
                        
                        // Dispose old websocket and create new one for reconnection
                        _Websocket?.Dispose();
                        _Websocket = new WatsonWsClient(_ServerHostname, _ServerPort, _ServerSsl, _GUID);
                        _Websocket.ServerConnected += ServerConnected;
                        _Websocket.ServerDisconnected += ServerDisconnected;
                        _Websocket.MessageReceived += ServerMessageReceived;
                        
                        await _Websocket.StartAsync();

                        if (IsConnected)
                        {
                            _Logging.Info(_Header + "websocket connected to " + ControllerUrl);
                            connectOutcome = TelemetryConstants.OutcomeSuccess;
                            ConstellationTelemetry.SetOk(connectSpan);
                        }
                        else
                        {
                            _Logging.Warn(_Header + "websocket connection failed to " + ControllerUrl);
                            connectOutcome = TelemetryConstants.OutcomeFailure;
                            ConstellationTelemetry.SetError(connectSpan, connectOutcome, "WebSocket connection to controller failed.");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        connectOutcome = TelemetryConstants.OutcomeCanceled;
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ConstellationTelemetry.SetException(connectSpan, ex);
                        ConstellationTelemetry.RecordError(TelemetryConstants.ComponentWorker, TelemetryConstants.OperationConnect, ex);
                        throw;
                    }
                    finally
                    {
                        ConstellationTelemetry.Add(ConstellationTelemetry.WorkerConnectionAttempts, 1, new TagList { { TelemetryConstants.LabelOutcome, connectOutcome } });
                        ConstellationTelemetry.Stop(connectSpan);
                    }

                    #endregion
                }
                catch (TaskCanceledException)
                {
                    _Logging.Debug(_Header + "maintain connection task canceled");
                    break;
                }
                catch (OperationCanceledException)
                {
                    _Logging.Debug(_Header + "maintain connection operation canceled");
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "maintain connection task exception:" + Environment.NewLine + e.ToString());
                }
            }

            _Logging.Debug(_Header + "connection management task terminated");
        }

#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    }
}