namespace Constellation.Controller
{
    using System;
    using System.Diagnostics;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Core;
    using Constellation.Core.Serialization;
    using Constellation.Core.Telemetry;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using SyslogLogging;
    using WatsonWebsocket;

    /// <summary>
    /// Worker metadata.
    /// </summary>
    public class WorkerMetadata
    {
        /// <summary>
        /// GUID.
        /// </summary>
        public Guid GUID { get; set; } = Guid.NewGuid();

        /// <summary>
        /// IP address.
        /// </summary>
        public string Ip { get; set; } = null;

        /// <summary>
        /// Port.
        /// </summary>
        public int Port { get; set; } = 0;

        /// <summary>
        /// Boolean indicating if the worker is healthy.
        /// </summary>
        public bool Healthy { get; set; } = true; // Start as healthy on connection

        /// <summary>
        /// UTC timestamp from when the worker was added.
        /// </summary>
        public DateTime AddedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp from when the last message was received.
        /// </summary>
        public DateTime LastMessageUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Cancellation token source.
        /// </summary>
        [JsonIgnore]
        public CancellationTokenSource TokenSource;

        private string _Header = "[WorkerMetadata] ";
        private Settings _Settings = null;
        private WatsonWsServer _Websocket = null;
        private LoggingModule _Logging = null;
        private Task _HeartbeatTask = null;
        private ILogger _StructuredLogger = NullLogger.Instance;
        private long _LastHeartbeatSuccessTicks = 0;

        private static Serializer _Serializer = new Serializer();

        /// <summary>
        /// Worker metadata.
        /// Parameterless constructor for JSON deserialization.
        /// </summary>
        public WorkerMetadata()
        {
        }

        /// <summary>
        /// Worker metadata.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="server">Server.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="guid">GUID.</param>
        /// <param name="ip">IP.</param>
        /// <param name="port">Port.</param>
        /// <param name="tokenSource">Cancellation token source.</param>
        public WorkerMetadata(
            Settings settings,
            WatsonWsServer server,
            LoggingModule logging,
            Guid guid,
            string ip,
            int port,
            CancellationTokenSource tokenSource)
            : this(settings, server, logging, null, guid, ip, port, tokenSource)
        {
        }

        internal WorkerMetadata(
            Settings settings,
            WatsonWsServer server,
            LoggingModule logging,
            ILogger structuredLogger,
            Guid guid,
            string ip,
            int port,
            CancellationTokenSource tokenSource)
        {
            _StructuredLogger = structuredLogger ?? NullLogger.Instance;
            _Settings = settings;
            _Websocket = server;
            _Logging = logging;
            TokenSource = tokenSource;

            GUID = guid;
            Ip = ip;
            Port = port;

            _Header = "[WorkerMetadata " + GUID + "] ";
            _HeartbeatTask = Task.Run(() => HeartbeatTask(_Settings.Heartbeat.IntervalMs, _Settings.Heartbeat.MaxFailures, tokenSource), tokenSource.Token);
        }

        internal DateTime LastHeartbeatSuccessUtc
        {
            get
            {
                long ticks = Interlocked.Read(ref _LastHeartbeatSuccessTicks);
                return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
            }
        }

        private async Task HeartbeatTask(int intervalMs, int maxFailures, CancellationTokenSource tokenSource)
        {
            bool firstRun = true;
            int currentFailures = 0;
            DateTimeOffset tickStartUtc = DateTimeOffset.UtcNow;
            long tickStart = ConstellationTelemetry.StartTimestamp();

            while (!tokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    #region Wait

                    if (!firstRun) await Task.Delay(_Settings.Heartbeat.IntervalMs, tokenSource.Token).ConfigureAwait(false);
                    else firstRun = false;

                    #endregion

                    #region Send-Heartbeat

                    tickStartUtc = DateTimeOffset.UtcNow;
                    tickStart = ConstellationTelemetry.StartTimestamp();

                    WebsocketMessage message = new WebsocketMessage();
                    message.GUID = GUID;
                    message.Type = WebsocketMessageTypeEnum.Heartbeat;

                    string messageJson = _Serializer.SerializeJson(message, false);
                    byte[] messageBytes = Encoding.UTF8.GetBytes(messageJson);
                    bool success = await _Websocket.SendAsync(GUID, messageBytes, System.Net.WebSockets.WebSocketMessageType.Text, tokenSource.Token).ConfigureAwait(false);

                    RecordHeartbeat(success ? TelemetryConstants.OutcomeSuccess : TelemetryConstants.OutcomeFailure, tickStart);
                    ConstellationTelemetry.RecordWebsocketMessage(
                        TelemetryConstants.ComponentController,
                        TelemetryConstants.DirectionSent,
                        WebsocketMessageTypeEnum.Heartbeat,
                        success ? TelemetryConstants.OutcomeSuccess : TelemetryConstants.OutcomeFailure,
                        messageBytes.Length);

                    if (!success)
                    {
                        currentFailures += 1;
                        EmitHeartbeatFailureSpan(tickStartUtc, currentFailures, null);

                        if (currentFailures > _Settings.Heartbeat.MaxFailures)
                        {
                            _Logging.Warn(_Header + "heartbeat failure limit exceeded for worker " + GUID + ", removing");
                            Healthy = false;
                            RecordEviction(currentFailures);
                            break;
                        }
                    }
                    else
                    {
                        _Logging.Debug(_Header + "worker " + GUID + " heartbeat successful");
                        Interlocked.Exchange(ref _LastHeartbeatSuccessTicks, DateTime.UtcNow.Ticks);

                        if (!Healthy)
                        {
                            TagList recoveredTags = new TagList { { TelemetryConstants.LabelEvent, TelemetryConstants.EventRecovered } };
                            ConstellationTelemetry.Add(ConstellationTelemetry.WorkerPoolEvents, 1, recoveredTags);
                            LogStructured(LogLevel.Information, "Worker {WorkerId} recovered after a successful heartbeat", null);
                        }

                        Healthy = true;
                        currentFailures = 0;
                    }

                    #endregion
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "heartbeat operation exception for work " + GUID + Environment.NewLine + e.ToString());
                    RecordHeartbeat(TelemetryConstants.OutcomeError, tickStart);
                    ConstellationTelemetry.RecordError(TelemetryConstants.ComponentController, TelemetryConstants.OperationHeartbeat, e);
                    EmitHeartbeatFailureSpan(tickStartUtc, currentFailures, e);
                    LogStructured(LogLevel.Warning, "Heartbeat to worker {WorkerId} raised an exception", e);
                }
            }

            _Logging.Info(_Header + "heartbeat operation canceled for worker " + GUID);
        }

        private static void RecordHeartbeat(string outcome, long start)
        {
            TagList tags = new TagList { { TelemetryConstants.LabelOutcome, outcome } };
            ConstellationTelemetry.Add(ConstellationTelemetry.Heartbeats, 1, tags);
            ConstellationTelemetry.Record(ConstellationTelemetry.HeartbeatDuration, ConstellationTelemetry.ElapsedSeconds(start), tags);
        }

        private void EmitHeartbeatFailureSpan(DateTimeOffset startUtc, int failures, Exception e)
        {
            Activity span = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanHeartbeat, ActivityKind.Producer, default(ActivityContext), startUtc);
            if (span == null) return;

            ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrWorkerId, GUID.ToString());
            ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrHeartbeatFailures, failures);
            ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrMessagingSystem, "websocket");

            if (e != null) ConstellationTelemetry.SetException(span, e);
            else ConstellationTelemetry.SetError(span, TelemetryConstants.OutcomeFailure, "Heartbeat could not be delivered to the worker.");

            ConstellationTelemetry.Stop(span);
        }

        private void RecordEviction(int failures)
        {
            TagList tags = new TagList { { TelemetryConstants.LabelEvent, TelemetryConstants.EventEvicted } };
            ConstellationTelemetry.Add(ConstellationTelemetry.WorkerPoolEvents, 1, tags);

            Activity span = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanWorkerEvict, ActivityKind.Internal, default(ActivityContext));
            ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrWorkerId, GUID.ToString());
            ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrHeartbeatFailures, failures);
            ConstellationTelemetry.SetError(span, TelemetryConstants.EventEvicted, "Heartbeat failure limit exceeded; worker marked unhealthy.");
            LogStructured(LogLevel.Warning, "Worker {WorkerId} exceeded the heartbeat failure limit and was marked unhealthy", null);
            ConstellationTelemetry.Stop(span);
        }

        private void LogStructured(LogLevel level, string template, Exception e)
        {
            try
            {
                _StructuredLogger.Log(level, 0, e, template, GUID);
            }
            catch (Exception)
            {
                // best-effort
            }
        }
    }
}