namespace Constellation.Worker
{
    using System;
    using System.Net.WebSockets;
    using System.Reflection.Metadata.Ecma335;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Core;
    using Constellation.Core.Serialization;
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
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        /// <param name="disposing">Disposing.</param>
        protected virtual async void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing)
                {
                    if (!_TokenSource.Token.IsCancellationRequested) _TokenSource.Cancel();

                    try
                    {
                        await _MaintainConnection.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (TimeoutException)
                    {
                    }

                    _Websocket?.Dispose();
                }

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
            if (OnDisconnection != null) OnDisconnection(GUID).Wait();
        }

        private void ServerConnected(object sender, EventArgs e)
        {
            if (OnConnection != null) OnConnection(GUID).Wait();
        }

        private async void ServerMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (OnRequestReceived == null) throw new NotImplementedException("The request handler has not been implemented.");
            byte[] data = (e.Data != null ? e.Data.ToArray() : new byte[0]);
            string json = Encoding.UTF8.GetString(data);
            WebsocketMessage request = _Serializer.DeserializeJson<WebsocketMessage>(json);
            _Logging.Debug(_Header + "received message of type " + request.Type + " (" + data.Length + " bytes)");
            WebsocketMessage response = await OnRequestReceived(request);
            if (response != null)
            {
                response.GUID = request.GUID;
                json = _Serializer.SerializeJson(response, false);
                data = Encoding.UTF8.GetBytes(json);
                await _Websocket.SendAsync(data, WebSocketMessageType.Binary, _TokenSource.Token).ConfigureAwait(false);
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
                    else
                    {
                        _Logging.Debug(_Header + "worker is not connected, attempting reconnection");
                        
                        // Dispose old websocket and create new one for reconnection
                        _Websocket?.Dispose();
                        _Websocket = new WatsonWsClient(_ServerHostname, _ServerPort, _ServerSsl, _GUID);
                        _Websocket.ServerConnected += ServerConnected;
                        _Websocket.ServerDisconnected += ServerDisconnected;
                        _Websocket.MessageReceived += ServerMessageReceived;
                        
                        await _Websocket.StartAsync();
                    }

                    if (IsConnected)
                        _Logging.Info(_Header + "websocket connected to " + ControllerUrl);
                    else
                        _Logging.Warn(_Header + "websocket connection failed to " + ControllerUrl);

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