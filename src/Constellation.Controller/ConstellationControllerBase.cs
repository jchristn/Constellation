namespace Constellation.Controller
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Controller.Services;
    using Constellation.Core;
    using Constellation.Core.Serialization;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebsocket;

    using ConnectionEventArgs = WatsonWebsocket.ConnectionEventArgs;
    using UrlDetails = Constellation.Core.UrlDetails;

    /// <summary>
    /// Constellation controller base class.
    /// </summary>
    public abstract class ConstellationControllerBase : IDisposable
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        /// <summary>
        /// Settings.
        /// </summary>
        public Settings Settings
        {
            get => _Settings;
            private set => _Settings = (value != null ? value : throw new ArgumentNullException(nameof(Settings)));
        }

        /// <summary>
        /// List of workers.
        /// </summary>
        public List<WorkerMetadata> Workers
        {
            get
            {
                return _WorkerService.Workers;
            }
        }

        /// <summary>
        /// Webserver.
        /// </summary>
        public Webserver Webserver { get; private set; } = null;

        /// <summary>
        /// Websocket server.
        /// </summary>
        public WatsonWsServer Websocket { get; private set; } = null;

        private string _Header = "[ConstellationController] ";
        private Settings _Settings = null;
        private LoggingModule _Logging = null;
        private Guid _GUID = Guid.NewGuid();
        private Serializer _Serializer = new Serializer();

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private WorkerService _WorkerService = null;
        private ResponseService _ResponseService = null;

        private Task _WebserverTask = null;
        private Task _WebsocketTask = null;

        private bool _Disposed = false;

        /// <summary>
        /// Constellation controller base class.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="logging">Logging.</param>
        /// <param name="tokenSource">Cancellation token source.</param>
        public ConstellationControllerBase(Settings settings, LoggingModule logging, CancellationTokenSource tokenSource = null)
        {
            _Settings = settings;
            _Logging = logging ?? new LoggingModule();

            if (tokenSource != null) _TokenSource = tokenSource;

            _WorkerService = new WorkerService(_Settings, _Logging);
            _ResponseService = new ResponseService(_Settings, _Logging);

            Webserver = new Webserver(_Settings.Webserver, DefaultRoute);
            Webserver.Routes.PreRouting = PreRoutingRoute;

            Websocket = new WatsonWsServer(_Settings.Websocket.Hostnames, _Settings.Websocket.Port, _Settings.Websocket.Ssl);
            Websocket.ClientConnected += WebsocketClientConnected;
            Websocket.ClientDisconnected += WebsocketClientDisconnected;
            Websocket.MessageReceived += WebsocketMessageReceived;
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

                    Webserver?.Dispose();
                    Websocket?.Dispose();
                }

                Webserver = null;
                Websocket = null;
                _WorkerService = null;
                _ResponseService = null;
                _Disposed = true;
            }
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) throw new ObjectDisposedException(nameof(ConstellationControllerBase));
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private async Task PreRoutingRoute(HttpContextBase ctx)
        {
            ctx.Response.ContentType = Constants.JsonContentType;
        }

        private async Task DefaultRoute(HttpContextBase ctx)
        {
            Guid requestGuid = Guid.NewGuid();

            ctx.Response.Headers.Add(Constants.RequestGuidHeader, requestGuid.ToString());

            try
            {
                #region Healthcheck-and-Favicon

                if (ctx.Request.Method == HttpMethod.HEAD
                    && (ctx.Request.Url.RawWithoutQuery.Equals("/")))
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = Constants.HtmlContentType;
                    await ctx.Response.Send(_TokenSource.Token).ConfigureAwait(false);
                    return;
                }

                if (ctx.Request.Method == HttpMethod.GET
                    && (ctx.Request.Url.RawWithoutQuery.Equals("/")))
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = Constants.HtmlContentType;
                    await ctx.Response.Send(Constants.HtmlHomepage, _TokenSource.Token).ConfigureAwait(false);
                    return;
                }

                if (ctx.Request.Method == HttpMethod.HEAD
                    && (ctx.Request.Url.RawWithoutQuery.Equals("/favicon.ico")))
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = Constants.FaviconContentType;
                    await ctx.Response.Send(File.ReadAllBytes(Constants.FaviconFilename), _TokenSource.Token).ConfigureAwait(false);
                }

                if (ctx.Request.Method == HttpMethod.GET
                    && (ctx.Request.Url.RawWithoutQuery.Equals("/favicon.ico")))
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = Constants.FaviconContentType;
                    await ctx.Response.Send(_TokenSource.Token).ConfigureAwait(false);
                }

                #endregion

                #region Find-Worker

                // Use the full raw URL (without query) as the resource identifier for pinning
                string resource = ctx.Request.Url.RawWithoutQuery;
                WorkerMetadata worker = _WorkerService.GetByResource(resource);
                if (worker == null)
                {
                    _Logging.Warn(_Header + "no worker found for resource " + resource);
                    ctx.Response.StatusCode = 502;
                    await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.BadGateway, null, "No workers available for resource " + resource + "."), true));
                    return;
                }

                _Logging.Debug(_Header + $"routing request for {resource} to worker {worker.GUID}");

                ctx.Response.Headers.Add(Constants.WorkerNameHeader, worker.GUID.ToString());

                #endregion

                #region Proxy-Request

                ctx.Request.Headers.Add(Constants.ForwardedForHeader, (ctx.Request.Source.IpAddress + ":" + ctx.Request.Source.Port));

                WebsocketMessage msg = new WebsocketMessage
                {
                    Type = WebsocketMessageTypeEnum.Request,
                    Method = ctx.Request.MethodRaw,
                    Url = new UrlDetails
                    {
                        Uri = new Uri(ctx.Request.Url.Full)
                    },
                    Headers = ctx.Request.Headers,
                    Data = ctx.Request.DataAsBytes
                };

                msg.Headers.Add(Constants.ForwardedForHeader, ctx.Request.Source.IpAddress);

                string msgJson = _Serializer.SerializeJson(msg, false);
                bool success = await Websocket.SendAsync(worker.GUID, Encoding.UTF8.GetBytes(msgJson), WebSocketMessageType.Binary, _TokenSource.Token).ConfigureAwait(false);
                if (!success)
                {
                    _Logging.Warn(_Header + "unable to proxy request " + ctx.Request.Method.ToString() + " " + resource + " to worker " + worker.GUID);
                    ctx.Response.StatusCode = 502;
                    await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.BadGateway, null, "Unable to proxy request for resource " + resource + "."), true));
                    return;
                }

                #endregion

                #region Wait-for-Response

                try
                {
                    WebsocketMessage resp = await _ResponseService.WaitForResponse(msg.GUID, _Settings.Proxy.TimeoutMs, true, _TokenSource.Token);
                    if (resp == null)
                    {
                        _Logging.Warn(_Header + "no response received for message " + msg.GUID);
                        ctx.Response.StatusCode = 500;
                        await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.InternalError, null, "No response received."), true));
                        return;
                    }

                    ctx.Response.StatusCode = resp.StatusCode != null ? resp.StatusCode.Value : 200;
                    ctx.Response.Headers = resp.Headers;

                    if (!String.IsNullOrEmpty(resp.ContentType)) ctx.Response.ContentType = resp.ContentType;

                    if (resp.Data != null && resp.Data.Length > 0)
                    {
                        await ctx.Response.Send(resp.Data, _TokenSource.Token).ConfigureAwait(false);
                        return;
                    }
                    else
                    {
                        await ctx.Response.Send(_TokenSource.Token).ConfigureAwait(false);
                        return;
                    }
                }
                catch (TimeoutException)
                {
                    _Logging.Warn(_Header + "timeout waiting for response to message " + msg.GUID);
                    ctx.Response.StatusCode = 408;
                    await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.Timeout), true));
                    return;
                }

                #endregion
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "default route exception for " + ctx.Request.Method.ToString() + " " + ctx.Request.Url.RawWithQuery + ":" + Environment.NewLine + e.ToString());
                ctx.Response.StatusCode = 500;
                await ctx.Response.Send(
                    _Serializer.SerializeJson(
                        new ApiErrorResponse(
                            ApiErrorEnum.InternalError,
                            null,
                            e.Message)));
            }
            finally
            {
                ctx.Timestamp.End = DateTime.UtcNow;

                _Logging.Debug(
                    _Header +
                    "completed request " + ctx.Request.Method.ToString() + " " + ctx.Request.Url.RawWithQuery + ": " +
                    ctx.Response.StatusCode + " (" + ctx.Timestamp.TotalMs.Value.ToString("F2") + "ms)");
            }
        }

        /// <summary>
        /// Start the controller.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task Start()
        {
            if (Webserver != null && !Webserver.IsListening)
            {
                _WebserverTask = Task.Run(() => Webserver.StartAsync(_TokenSource.Token), _TokenSource.Token);
                _Logging.Debug(_Header + "started webserver");
            }

            if (Websocket != null && !Websocket.IsListening)
            {
                _WebsocketTask = Task.Run(() => Websocket.StartAsync(_TokenSource.Token), _TokenSource.Token);
                _Logging.Debug(_Header + "started websocket server");
            }
        }

        /// <summary>
        /// Stop the controller.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task Stop()
        {
            if (Webserver != null && Webserver.IsListening) Webserver.Stop();
            if (Websocket != null && Websocket.IsListening) Websocket.Stop();
        }

        /// <summary>
        /// Method to fire on worker connection.
        /// </summary>
        /// <param name="guid">GUID of the worker.</param>
        /// <param name="ipAddress">IP address of the worker.</param>
        /// <param name="port">Port of the worker.</param>
        /// <returns>Task.</returns>
        public abstract Task OnConnection(Guid guid, string ipAddress, int port);

        /// <summary>
        /// Method to fire on worker disconnection.
        /// </summary>
        /// <param name="guid">GUID of the worker.</param>
        /// <param name="ipAddress">IP address of the worker.</param>
        /// <param name="port">Port of the worker.</param>
        /// <returns>Task.</returns>
        public abstract Task OnDisconnection(Guid guid, string ipAddress, int port);

        private void WebsocketClientDisconnected(object sender, DisconnectionEventArgs e)
        {
            if (OnDisconnection != null)
                OnDisconnection(e.Client.Guid, e.Client.Ip, e.Client.Port).Wait();

            WorkerMetadata worker = _WorkerService.GetByGuid(e.Client.Guid);
            if (worker != null)
            {
                _Logging.Debug(_Header + "canceling operations for worker " + e.Client.Guid);
                if (!worker.TokenSource.IsCancellationRequested) worker.TokenSource.Cancel();
                _WorkerService.RemoveWorker(e.Client.Guid);
            }
        }

        private void WebsocketClientConnected(object sender, ConnectionEventArgs e)
        {
            if (OnConnection != null)
                OnConnection(e.Client.Guid, e.Client.Ip, e.Client.Port).Wait();

            CancellationTokenSource newTokenSource = new CancellationTokenSource();
            CancellationTokenSource workerTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                _TokenSource.Token,
                newTokenSource.Token);

            WorkerMetadata worker = new WorkerMetadata(_Settings, Websocket, _Logging, e.Client.Guid, e.Client.Ip, e.Client.Port, workerTokenSource);
            _WorkerService.AddWorker(worker);

            _Logging.Debug(_Header + "registered client " + e.Client.Guid + " " + e.Client.Ip + ":" + e.Client.Port);
        }

        private void WebsocketMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                WorkerMetadata worker = _WorkerService.GetByGuid(e.Client.Guid);
                if (worker != null)
                {
                    byte[] data = (e.Data != null ? e.Data.ToArray() : new byte[0]);
                    string json = Encoding.UTF8.GetString(data);
                    WebsocketMessage msg = _Serializer.DeserializeJson<WebsocketMessage>(json);

                    if (msg.Type == WebsocketMessageTypeEnum.Heartbeat) return; // do nothing

                    _Logging.Debug(_Header + "received message of type " + msg.Type + " from worker " + worker.GUID + " (" + data.Length + " bytes)");
                    if (msg.Type.Equals(WebsocketMessageTypeEnum.Response)) _ResponseService.AddResponse(msg);
                }
                else
                {
                    _Logging.Warn(_Header + "unsolicited message from unknown worker " + e.Client.Guid + ", discarding");
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "exception processing message from worker " + e.Client.Guid + Environment.NewLine + ex.ToString());
            }
        }

#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    }
}