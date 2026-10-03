namespace Constellation.Controller
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Controller.Services;
    using Constellation.Core;
    using Constellation.Core.Serialization;
    using Constellation.Core.Telemetry;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebsocket;

    using ApiErrorResponse = Constellation.Core.ApiErrorResponse;
    using ConnectionEventArgs = WatsonWebsocket.ConnectionEventArgs;
    using ILogger = Microsoft.Extensions.Logging.ILogger;
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

        /// <summary>
        /// Optional structured logger for operational events (worker lifecycle, eviction, proxy failures, cleanup).
        /// Records written inside a request or background span carry its trace and span ids, so a host that exports
        /// logs (for example Radiant to Loki) gets log-to-trace correlation.  Default is a no-op logger; setting null
        /// restores the no-op logger.  Set before calling <see cref="Start"/>; worker sessions capture the logger
        /// in effect when they register.  This is in addition to, not a replacement for, the SyslogLogging module.
        /// </summary>
        public ILogger Logger
        {
            get => _StructuredLogger;
            set
            {
                _StructuredLogger = value ?? NullLogger.Instance;
                _ResponseService?.SetStructuredLogger(_StructuredLogger);
            }
        }

        private string _Header = "[ConstellationController] ";
        private ILogger _StructuredLogger = NullLogger.Instance;
        private ControllerTelemetryState _TelemetryState = null;
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

            _TelemetryState = new ControllerTelemetryState(_Settings, () => _WorkerService, () => _ResponseService);
            ConstellationTelemetry.RegisterController(_TelemetryState);
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
                    _ResponseService?.Dispose();
                }

                ConstellationTelemetry.UnregisterController(_TelemetryState);

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

            bool proxying = false;
            long proxyStart = 0;
            string proxyOutcome = TelemetryConstants.OutcomeError;
            string method = ConstellationTelemetry.NormalizeMethod(ctx.Request.MethodRaw);
            Activity proxySpan = null;

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
                    await ctx.Response.Send(_TokenSource.Token).ConfigureAwait(false);
                    return;
                }

                if (ctx.Request.Method == HttpMethod.GET
                    && (ctx.Request.Url.RawWithoutQuery.Equals("/favicon.ico")))
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = Constants.FaviconContentType;
                    if (File.Exists(Constants.FaviconFilename))
                        await ctx.Response.Send(File.ReadAllBytes(Constants.FaviconFilename), _TokenSource.Token).ConfigureAwait(false);
                    else
                        await ctx.Response.Send(_TokenSource.Token).ConfigureAwait(false);
                    return;
                }

                #endregion

                #region Administrative-APIs

                if (ctx.Request.HeaderExists(_Settings.Admin.ApiKeyHeader))
                {
                    if (_Settings.Admin.ApiKeys.Contains(ctx.Request.Headers[_Settings.Admin.ApiKeyHeader]))
                    {
                        _Logging.Debug(_Header + $"admin API key in use from {ctx.Request.Source.IpAddress}");

                        if (ctx.Request.Method.Equals(HttpMethod.GET))
                        {
                            if (ctx.Request.Url.RawWithoutQuery.Equals("/workers"))
                            {
                                Activity adminSpan = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanAdminPrefix + TelemetryConstants.OperationListWorkers, ActivityKind.Internal);
                                try
                                {
                                    _Logging.Debug(_Header + $"worker list retrieved from {ctx.Request.Source.IpAddress}");
                                    List<WorkerMetadata> workers = _WorkerService.Workers;
                                    ConstellationTelemetry.SetTag(adminSpan, TelemetryConstants.AttrCount, workers.Count);
                                    ctx.Response.StatusCode = 200;
                                    ctx.Response.ContentType = Constants.JsonContentType;
                                    await ctx.Response.Send(_Serializer.SerializeJson(workers, true));
                                    RecordAdmin(TelemetryConstants.OperationListWorkers, TelemetryConstants.OutcomeSuccess);
                                    ConstellationTelemetry.SetOk(adminSpan);
                                    return;
                                }
                                catch (Exception e)
                                {
                                    RecordAdmin(TelemetryConstants.OperationListWorkers, TelemetryConstants.OutcomeError);
                                    ConstellationTelemetry.SetException(adminSpan, e);
                                    throw;
                                }
                                finally
                                {
                                    ConstellationTelemetry.Stop(adminSpan);
                                }
                            }
                            else if (ctx.Request.Url.RawWithoutQuery.Equals("/maps"))
                            {
                                Activity adminSpan = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanAdminPrefix + TelemetryConstants.OperationListMaps, ActivityKind.Internal);
                                try
                                {
                                    _Logging.Debug(_Header + $"maps retrieved from {ctx.Request.Source.IpAddress}");
                                    Dictionary<Guid, List<string>> maps = _WorkerService.ResourceMap;
                                    ConstellationTelemetry.SetTag(adminSpan, TelemetryConstants.AttrCount, maps.Count);
                                    ctx.Response.StatusCode = 200;
                                    ctx.Response.ContentType = Constants.JsonContentType;
                                    await ctx.Response.Send(_Serializer.SerializeJson(maps, true));
                                    RecordAdmin(TelemetryConstants.OperationListMaps, TelemetryConstants.OutcomeSuccess);
                                    ConstellationTelemetry.SetOk(adminSpan);
                                    return;
                                }
                                catch (Exception e)
                                {
                                    RecordAdmin(TelemetryConstants.OperationListMaps, TelemetryConstants.OutcomeError);
                                    ConstellationTelemetry.SetException(adminSpan, e);
                                    throw;
                                }
                                finally
                                {
                                    ConstellationTelemetry.Stop(adminSpan);
                                }
                            }
                        }
                    }
                    else
                    {
                        _Logging.Warn(_Header + $"invalid API key used from {ctx.Request.Source.IpAddress}");
                        RecordAdmin(TelemetryConstants.OperationAuthenticate, TelemetryConstants.OutcomeUnauthorized);
                        LogStructured(LogLevel.Warning, null, "Rejected admin request with an invalid API key for {Path}", ctx.Request.Url.RawWithoutQuery);
                        ctx.Response.StatusCode = 401;
                        ctx.Response.ContentType = Constants.JsonContentType;
                        await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.AuthorizationFailed), true));
                        return;
                    }
                }

                #endregion

                #region Find-Worker

                // Use the full raw URL (without query) as the resource identifier for pinning
                string resource = ctx.Request.Url.RawWithoutQuery;

                proxying = true;
                proxyStart = ConstellationTelemetry.StartTimestamp();
                ConstellationTelemetry.Add(ConstellationTelemetry.ProxyActiveRequests, 1, new TagList { { TelemetryConstants.LabelHttpMethod, method } });
                proxySpan = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanProxy, ActivityKind.Internal);
                ConstellationTelemetry.SetTag(proxySpan, TelemetryConstants.AttrResource, resource);
                ConstellationTelemetry.SetTag(proxySpan, TelemetryConstants.AttrHttpMethod, ctx.Request.MethodRaw);

                long placementStart = ConstellationTelemetry.StartTimestamp();
                Activity placementSpan = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanStagePrefix + TelemetryConstants.StagePlacement, ActivityKind.Internal);
                string decision;
                WorkerMetadata worker;
                try
                {
                    worker = _WorkerService.GetByResource(resource, out decision);
                    ConstellationTelemetry.SetTag(placementSpan, TelemetryConstants.AttrPlacementDecision, decision);
                    ConstellationTelemetry.SetTag(proxySpan, TelemetryConstants.AttrPlacementDecision, decision);
                    if (worker == null)
                    {
                        RecordStage(TelemetryConstants.StagePlacement, TelemetryConstants.OutcomeNoWorker, placementStart);
                        ConstellationTelemetry.SetError(placementSpan, TelemetryConstants.OutcomeNoWorker, "No healthy worker available (" + decision + ").");
                    }
                    else
                    {
                        RecordStage(TelemetryConstants.StagePlacement, TelemetryConstants.OutcomeSuccess, placementStart);
                        ConstellationTelemetry.SetTag(placementSpan, TelemetryConstants.AttrWorkerId, worker.GUID.ToString());
                        ConstellationTelemetry.SetOk(placementSpan);
                    }
                }
                catch (Exception e)
                {
                    RecordStage(TelemetryConstants.StagePlacement, TelemetryConstants.OutcomeError, placementStart);
                    ConstellationTelemetry.SetException(placementSpan, e);
                    throw;
                }
                finally
                {
                    ConstellationTelemetry.Stop(placementSpan);
                }

                if (worker == null)
                {
                    _Logging.Warn(_Header + "no worker found for resource " + resource);
                    proxyOutcome = TelemetryConstants.OutcomeNoWorker;
                    LogStructured(LogLevel.Warning, null, "No healthy worker available for resource {Resource} ({Decision})", resource, decision);
                    ctx.Response.StatusCode = 502;
                    await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.BadGateway, null, "No workers available for resource " + resource + "."), true));
                    return;
                }

                _Logging.Debug(_Header + $"routing request for {resource} to worker {worker.GUID}");

                ctx.Response.Headers.Add(Constants.WorkerNameHeader, worker.GUID.ToString());
                ConstellationTelemetry.SetTag(proxySpan, TelemetryConstants.AttrWorkerId, worker.GUID.ToString());

                #endregion

                WebsocketMessage resp = null;
                Activity clientSpan = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanWorkerRequest, ActivityKind.Client);
                ConstellationTelemetry.SetTag(clientSpan, TelemetryConstants.AttrWorkerId, worker.GUID.ToString());
                ConstellationTelemetry.SetTag(clientSpan, TelemetryConstants.AttrMessagingSystem, "websocket");
                ConstellationTelemetry.SetTag(clientSpan, TelemetryConstants.AttrServerAddress, worker.Ip);
                ConstellationTelemetry.SetTag(clientSpan, TelemetryConstants.AttrServerPort, worker.Port);

                try
                {
                    #region Proxy-Request

                    long dispatchStart = ConstellationTelemetry.StartTimestamp();
                    Activity dispatchSpan = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanStagePrefix + TelemetryConstants.StageDispatch, ActivityKind.Internal);
                    WebsocketMessage msg;
                    bool success;

                    try
                    {
                        msg = new WebsocketMessage
                        {
                            Type = WebsocketMessageTypeEnum.Request,
                            Method = ctx.Request.MethodRaw,
                            Url = new UrlDetails
                            {
                                Uri = BuildRequestUri(ctx)
                            },
                            Headers = ctx.Request.Headers,
                            ContentType = ctx.Request.ContentType,
                            Data = ctx.Request.DataAsBytes
                        };

                        // Append the client address to any existing chain, as standard proxies do.
                        string forwardedFor = msg.Headers.Get(Constants.ForwardedForHeader);
                        msg.Headers.Set(Constants.ForwardedForHeader, String.IsNullOrEmpty(forwardedFor) ? ctx.Request.Source.IpAddress : forwardedFor + ", " + ctx.Request.Source.IpAddress);

                        // Continue the trace on the worker: the worker's span becomes a child of the client span.
                        ConstellationTelemetry.Inject(clientSpan, msg);
                        ConstellationTelemetry.SetTag(clientSpan, TelemetryConstants.AttrMessageId, msg.GUID.ToString());
                        ConstellationTelemetry.SetTag(proxySpan, TelemetryConstants.AttrMessageId, msg.GUID.ToString());

                        string msgJson = _Serializer.SerializeJson(msg, false);
                        byte[] msgBytes = Encoding.UTF8.GetBytes(msgJson);
                        success = await Websocket.SendAsync(worker.GUID, msgBytes, WebSocketMessageType.Binary, _TokenSource.Token).ConfigureAwait(false);

                        ConstellationTelemetry.RecordWebsocketMessage(
                            TelemetryConstants.ComponentController,
                            TelemetryConstants.DirectionSent,
                            WebsocketMessageTypeEnum.Request,
                            success ? TelemetryConstants.OutcomeSuccess : TelemetryConstants.OutcomeFailure,
                            msgBytes.Length);

                        if (success)
                        {
                            RecordStage(TelemetryConstants.StageDispatch, TelemetryConstants.OutcomeSuccess, dispatchStart);
                            ConstellationTelemetry.SetOk(dispatchSpan);
                        }
                        else
                        {
                            RecordStage(TelemetryConstants.StageDispatch, TelemetryConstants.OutcomeSendFailed, dispatchStart);
                            ConstellationTelemetry.SetError(dispatchSpan, TelemetryConstants.OutcomeSendFailed, "WebSocket send to worker failed.");
                        }
                    }
                    catch (Exception e)
                    {
                        RecordStage(TelemetryConstants.StageDispatch, TelemetryConstants.OutcomeError, dispatchStart);
                        ConstellationTelemetry.SetException(dispatchSpan, e);
                        throw;
                    }
                    finally
                    {
                        ConstellationTelemetry.Stop(dispatchSpan);
                    }

                    if (!success)
                    {
                        _Logging.Warn(_Header + "unable to proxy request " + ctx.Request.Method.ToString() + " " + resource + " to worker " + worker.GUID);
                        proxyOutcome = TelemetryConstants.OutcomeSendFailed;
                        ConstellationTelemetry.SetError(clientSpan, TelemetryConstants.OutcomeSendFailed, "WebSocket send to worker failed.");
                        LogStructured(LogLevel.Warning, null, "Unable to send request for resource {Resource} to worker {WorkerId}", resource, worker.GUID);
                        ctx.Response.StatusCode = 502;
                        await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.BadGateway, null, "Unable to proxy request for resource " + resource + "."), true));
                        return;
                    }

                    #endregion

                    #region Wait-for-Response

                    long awaitStart = ConstellationTelemetry.StartTimestamp();
                    Activity awaitSpan = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanStagePrefix + TelemetryConstants.StageAwaitResponse, ActivityKind.Internal);

                    try
                    {
                        resp = await _ResponseService.WaitForResponse(msg.GUID, _Settings.Proxy.TimeoutMs, true, _TokenSource.Token).ConfigureAwait(false);
                        string awaitOutcome = resp != null ? TelemetryConstants.OutcomeSuccess : TelemetryConstants.OutcomeNoResponse;
                        RecordStage(TelemetryConstants.StageAwaitResponse, awaitOutcome, awaitStart);
                        if (resp != null) ConstellationTelemetry.SetOk(awaitSpan);
                        else ConstellationTelemetry.SetError(awaitSpan, awaitOutcome, "No response received.");
                    }
                    catch (TimeoutException)
                    {
                        RecordStage(TelemetryConstants.StageAwaitResponse, TelemetryConstants.OutcomeTimeout, awaitStart);
                        ConstellationTelemetry.SetError(awaitSpan, TelemetryConstants.OutcomeTimeout, "Timed out waiting for worker response.");
                        ConstellationTelemetry.SetError(clientSpan, TelemetryConstants.OutcomeTimeout, "Timed out waiting for worker response.");
                        ConstellationTelemetry.Stop(awaitSpan);
                        awaitSpan = null;

                        _Logging.Warn(_Header + "timeout waiting for response to message " + msg.GUID);
                        proxyOutcome = TelemetryConstants.OutcomeTimeout;
                        LogStructured(LogLevel.Warning, null, "Timed out after {TimeoutMs}ms waiting for worker {WorkerId} to answer resource {Resource}", _Settings.Proxy.TimeoutMs, worker.GUID, resource);
                        ctx.Response.StatusCode = 408;
                        await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.Timeout), true));
                        return;
                    }
                    catch (Exception e)
                    {
                        string awaitOutcome = e is OperationCanceledException ? TelemetryConstants.OutcomeCanceled : TelemetryConstants.OutcomeError;
                        RecordStage(TelemetryConstants.StageAwaitResponse, awaitOutcome, awaitStart);
                        ConstellationTelemetry.SetException(awaitSpan, e, awaitOutcome);
                        throw;
                    }
                    finally
                    {
                        ConstellationTelemetry.Stop(awaitSpan);
                    }

                    if (resp == null)
                    {
                        _Logging.Warn(_Header + "no response received for message " + msg.GUID);
                        proxyOutcome = TelemetryConstants.OutcomeNoResponse;
                        ConstellationTelemetry.SetError(clientSpan, TelemetryConstants.OutcomeNoResponse, "No response received.");
                        ctx.Response.StatusCode = 500;
                        await ctx.Response.Send(_Serializer.SerializeJson(new ApiErrorResponse(ApiErrorEnum.InternalError, null, "No response received."), true));
                        return;
                    }

                    int workerStatus = resp.StatusCode != null ? resp.StatusCode.Value : 200;
                    ConstellationTelemetry.SetTag(clientSpan, TelemetryConstants.AttrHttpStatusCode, workerStatus);
                    ConstellationTelemetry.SetOk(clientSpan);

                    #endregion
                }
                catch (Exception e)
                {
                    ConstellationTelemetry.SetException(clientSpan, e, e is OperationCanceledException ? TelemetryConstants.OutcomeCanceled : TelemetryConstants.OutcomeError);
                    throw;
                }
                finally
                {
                    ConstellationTelemetry.Stop(clientSpan);
                }

                #region Respond

                long respondStart = ConstellationTelemetry.StartTimestamp();
                Activity respondSpan = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanStagePrefix + TelemetryConstants.StageRespond, ActivityKind.Internal);

                try
                {
                    ctx.Response.StatusCode = resp.StatusCode != null ? resp.StatusCode.Value : 200;
                    // Merge the worker's headers over the controller's so x-request and x-worker survive.
                    if (resp.Headers != null)
                    {
                        foreach (string key in resp.Headers.AllKeys)
                        {
                            if (String.IsNullOrEmpty(key)) continue;
                            if (key.Equals(Constants.RequestGuidHeader, StringComparison.OrdinalIgnoreCase)) continue;
                            if (key.Equals(Constants.WorkerNameHeader, StringComparison.OrdinalIgnoreCase)) continue;
                            ctx.Response.Headers.Set(key, resp.Headers[key]);
                        }
                    }

                    if (!String.IsNullOrEmpty(resp.ContentType)) ctx.Response.ContentType = resp.ContentType;

                    if (resp.Data != null && resp.Data.Length > 0)
                    {
                        await ctx.Response.Send(resp.Data, _TokenSource.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await ctx.Response.Send(_TokenSource.Token).ConfigureAwait(false);
                    }

                    RecordStage(TelemetryConstants.StageRespond, TelemetryConstants.OutcomeSuccess, respondStart);
                    ConstellationTelemetry.SetTag(respondSpan, TelemetryConstants.AttrHttpStatusCode, ctx.Response.StatusCode);
                    ConstellationTelemetry.SetOk(respondSpan);
                    proxyOutcome = TelemetryConstants.OutcomeSuccess;
                    return;
                }
                catch (Exception e)
                {
                    RecordStage(TelemetryConstants.StageRespond, TelemetryConstants.OutcomeError, respondStart);
                    ConstellationTelemetry.SetException(respondSpan, e);
                    throw;
                }
                finally
                {
                    ConstellationTelemetry.Stop(respondSpan);
                }

                #endregion
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "default route exception for " + ctx.Request.Method.ToString() + " " + ctx.Request.Url.RawWithQuery + ":" + Environment.NewLine + e.ToString());
                proxyOutcome = e is OperationCanceledException ? TelemetryConstants.OutcomeCanceled : TelemetryConstants.OutcomeError;
                ConstellationTelemetry.RecordError(TelemetryConstants.ComponentController, TelemetryConstants.OperationProxy, e);
                ConstellationTelemetry.SetException(proxySpan, e, proxyOutcome);
                LogStructured(LogLevel.Error, e, "Unhandled exception proxying {Method} {Path}", method, ctx.Request.Url.RawWithoutQuery);
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
                if (proxying)
                {
                    TagList activeTags = new TagList { { TelemetryConstants.LabelHttpMethod, method } };
                    ConstellationTelemetry.Add(ConstellationTelemetry.ProxyActiveRequests, -1, activeTags);

                    TagList tags = new TagList
                    {
                        { TelemetryConstants.LabelOutcome, proxyOutcome },
                        { TelemetryConstants.LabelHttpMethod, method }
                    };

                    ConstellationTelemetry.Add(ConstellationTelemetry.ProxyRequests, 1, tags);
                    ConstellationTelemetry.Record(ConstellationTelemetry.ProxyRequestDuration, ConstellationTelemetry.ElapsedSeconds(proxyStart), tags);

                    if (proxyOutcome == TelemetryConstants.OutcomeSuccess) ConstellationTelemetry.SetOk(proxySpan);
                    else if (proxySpan != null && proxySpan.Status != ActivityStatusCode.Error) ConstellationTelemetry.SetError(proxySpan, proxyOutcome, "Proxy request did not succeed (" + proxyOutcome + ").");

                    ConstellationTelemetry.SetTag(proxySpan, TelemetryConstants.AttrHttpStatusCode, ctx.Response.StatusCode);
                    ConstellationTelemetry.Stop(proxySpan);
                }

                ctx.Timestamp.End = DateTime.UtcNow;

                _Logging.Debug(
                    _Header +
                    "completed request " + ctx.Request.Method.ToString() + " " + ctx.Request.Url.RawWithQuery + ": " +
                    ctx.Response.StatusCode + " (" + ctx.Timestamp.TotalMs.Value.ToString("F2") + "ms)");
            }
        }

        private static Uri BuildRequestUri(HttpContextBase ctx)
        {
            // Prefer the Host header the caller used.  Watson 7 composes Url.Full from the listener hostname, which
            // is not a valid URI host when the controller binds a wildcard ("*" or "+").
            string scheme = !String.IsNullOrEmpty(ctx.Request.Url.Scheme) ? ctx.Request.Url.Scheme : "http";
            string host = ctx.Request.Headers != null ? ctx.Request.Headers.Get("Host") : null;

            if (!String.IsNullOrEmpty(host)
                && Uri.TryCreate(scheme + "://" + host + ctx.Request.Url.RawWithQuery, UriKind.Absolute, out Uri fromHost))
                return fromHost;

            if (Uri.TryCreate(ctx.Request.Url.Full, UriKind.Absolute, out Uri full))
                return full;

            int port = ctx.Request.Url.Port.HasValue && ctx.Request.Url.Port.Value > 0 ? ctx.Request.Url.Port.Value : 80;
            return new Uri(scheme + "://localhost:" + port + ctx.Request.Url.RawWithQuery);
        }

        private static void RecordStage(string stage, string outcome, long start)
        {
            TagList tags = new TagList
            {
                { TelemetryConstants.LabelStage, stage },
                { TelemetryConstants.LabelOutcome, outcome }
            };

            ConstellationTelemetry.Add(ConstellationTelemetry.ProxyStageEvents, 1, tags);
            ConstellationTelemetry.Record(ConstellationTelemetry.ProxyStageDuration, ConstellationTelemetry.ElapsedSeconds(start), tags);
        }

        private static void RecordAdmin(string operation, string outcome)
        {
            TagList tags = new TagList
            {
                { TelemetryConstants.LabelOperation, operation },
                { TelemetryConstants.LabelOutcome, outcome }
            };

            ConstellationTelemetry.Add(ConstellationTelemetry.AdminRequests, 1, tags);
        }

        private void LogStructured(LogLevel level, Exception e, string template, params object[] args)
        {
            try
            {
                _StructuredLogger.Log(level, 0, e, template, args);
            }
            catch (Exception)
            {
                // best-effort: logging must never affect request handling
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
            Activity span = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanWorkerUnregister, ActivityKind.Internal, default(ActivityContext));
            ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrWorkerId, e.Client.Guid.ToString());

            try
            {
                ConstellationTelemetry.Add(ConstellationTelemetry.WorkerPoolEvents, 1, new TagList { { TelemetryConstants.LabelEvent, TelemetryConstants.EventDisconnected } });

                if (OnDisconnection != null)
                    InvokeLifecycle(() => OnDisconnection(e.Client.Guid, e.Client.Ip, e.Client.Port));

                WorkerMetadata worker = _WorkerService.GetByGuid(e.Client.Guid);
                if (worker != null)
                {
                    _Logging.Debug(_Header + "canceling operations for worker " + e.Client.Guid);
                    if (!worker.TokenSource.IsCancellationRequested) worker.TokenSource.Cancel();
                    _WorkerService.RemoveWorker(e.Client.Guid);

                    double sessionSeconds = (DateTime.UtcNow - worker.AddedUtc).TotalSeconds;
                    ConstellationTelemetry.Record(ConstellationTelemetry.WorkerSessionDuration, sessionSeconds, default(TagList));
                    LogStructured(LogLevel.Information, null, "Worker {WorkerId} disconnected after {SessionSeconds:F1}s; remaining workers {WorkerCount}", e.Client.Guid, sessionSeconds, _WorkerService.Workers.Count);
                }

                ConstellationTelemetry.SetOk(span);
            }
            catch (Exception ex)
            {
                ConstellationTelemetry.SetException(span, ex);
                throw;
            }
            finally
            {
                ConstellationTelemetry.Stop(span);
            }
        }

        private void WebsocketClientConnected(object sender, ConnectionEventArgs e)
        {
            Activity span = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanWorkerRegister, ActivityKind.Internal, default(ActivityContext));
            ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrWorkerId, e.Client.Guid.ToString());

            try
            {
                if (OnConnection != null)
                    InvokeLifecycle(() => OnConnection(e.Client.Guid, e.Client.Ip, e.Client.Port));

                CancellationTokenSource newTokenSource = new CancellationTokenSource();
                CancellationTokenSource workerTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _TokenSource.Token,
                    newTokenSource.Token);

                WorkerMetadata worker = new WorkerMetadata(_Settings, Websocket, _Logging, _StructuredLogger, e.Client.Guid, e.Client.Ip, e.Client.Port, workerTokenSource);
                _WorkerService.AddWorker(worker);

                ConstellationTelemetry.Add(ConstellationTelemetry.WorkerPoolEvents, 1, new TagList { { TelemetryConstants.LabelEvent, TelemetryConstants.EventConnected } });
                _Logging.Debug(_Header + "registered client " + e.Client.Guid + " " + e.Client.Ip + ":" + e.Client.Port);
                LogStructured(LogLevel.Information, null, "Worker {WorkerId} connected; total workers {WorkerCount}", e.Client.Guid, _WorkerService.Workers.Count);
                ConstellationTelemetry.SetOk(span);
            }
            catch (Exception ex)
            {
                ConstellationTelemetry.SetException(span, ex);
                throw;
            }
            finally
            {
                ConstellationTelemetry.Stop(span);
            }
        }

        private void InvokeLifecycle(Func<Task> callback)
        {
            try
            {
                callback().Wait();
            }
            catch (Exception e)
            {
                ConstellationTelemetry.RecordError(TelemetryConstants.ComponentController, TelemetryConstants.OperationLifecycle, e is AggregateException && e.InnerException != null ? e.InnerException : e);
                throw;
            }
        }

        private void WebsocketMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            int size = e.Data != null ? e.Data.Count : 0;

            try
            {
                WorkerMetadata worker = _WorkerService.GetByGuid(e.Client.Guid);
                if (worker != null)
                {
                    byte[] data = (e.Data != null ? e.Data.ToArray() : new byte[0]);
                    string json = Encoding.UTF8.GetString(data);
                    WebsocketMessage msg = _Serializer.DeserializeJson<WebsocketMessage>(json);
                    worker.LastMessageUtc = DateTime.UtcNow;

                    if (msg.Type == WebsocketMessageTypeEnum.Heartbeat)
                    {
                        ConstellationTelemetry.RecordWebsocketMessage(TelemetryConstants.ComponentController, TelemetryConstants.DirectionReceived, msg.Type, TelemetryConstants.OutcomeSuccess, size);
                        return; // do nothing
                    }

                    _Logging.Debug(_Header + "received message of type " + msg.Type + " from worker " + worker.GUID + " (" + data.Length + " bytes)");

                    if (msg.Type.Equals(WebsocketMessageTypeEnum.Response))
                    {
                        Activity span = ConstellationTelemetry.StartActivityFromTraceParent(TelemetryConstants.SpanResponseReceive, ActivityKind.Consumer, msg.TraceParent, msg.TraceState);
                        ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrWorkerId, worker.GUID.ToString());
                        ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrMessageId, msg.GUID.ToString());
                        ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrMessagingSystem, "websocket");

                        bool added = _ResponseService.AddResponse(msg);
                        string outcome = added ? TelemetryConstants.OutcomeSuccess : TelemetryConstants.OutcomeDuplicate;
                        ConstellationTelemetry.RecordWebsocketMessage(TelemetryConstants.ComponentController, TelemetryConstants.DirectionReceived, msg.Type, outcome, size);
                        if (added) ConstellationTelemetry.SetOk(span);
                        else ConstellationTelemetry.SetError(span, outcome, "Duplicate response for message id.");
                        ConstellationTelemetry.Stop(span);
                    }
                    else
                    {
                        ConstellationTelemetry.RecordWebsocketMessage(TelemetryConstants.ComponentController, TelemetryConstants.DirectionReceived, msg.Type, TelemetryConstants.OutcomeDiscarded, size);
                    }
                }
                else
                {
                    _Logging.Warn(_Header + "unsolicited message from unknown worker " + e.Client.Guid + ", discarding");
                    ConstellationTelemetry.RecordWebsocketMessage(TelemetryConstants.ComponentController, TelemetryConstants.DirectionReceived, WebsocketMessageTypeEnum.Unknown, TelemetryConstants.OutcomeDiscarded, size);
                    LogStructured(LogLevel.Warning, null, "Discarded message from unknown worker {WorkerId}", e.Client.Guid);
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "exception processing message from worker " + e.Client.Guid + Environment.NewLine + ex.ToString());
                ConstellationTelemetry.RecordWebsocketMessage(TelemetryConstants.ComponentController, TelemetryConstants.DirectionReceived, WebsocketMessageTypeEnum.Unknown, TelemetryConstants.OutcomeError, size);
                ConstellationTelemetry.RecordError(TelemetryConstants.ComponentController, TelemetryConstants.OperationReceive, ex);
                LogStructured(LogLevel.Warning, ex, "Failed to process message from worker {WorkerId}", e.Client.Guid);
            }
        }

#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    }
}