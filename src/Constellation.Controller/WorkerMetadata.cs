namespace Constellation.Controller
{
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Core;
    using Constellation.Core.Serialization;
    using SyslogLogging;
    using WatsonWebsocket;

    public class WorkerMetadata
    {
        public Guid GUID { get; set; } = Guid.NewGuid();
        public string Ip { get; set; } = null;
        public int Port { get; set; } = 0;
        public bool Healthy { get; set; } = true; // Start as healthy on connection
        public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastMessageUtc { get; set; } = DateTime.UtcNow;
        [JsonIgnore]
        public CancellationTokenSource TokenSource;

        private string _Header = "[WorkerMetadata] ";
        private Settings _Settings = null;
        private WatsonWsServer _Websocket = null;
        private LoggingModule _Logging = null;
        private Task _HeartbeatTask = null;

        private static Serializer _Serializer = new Serializer();

        public WorkerMetadata(
            Settings settings,
            WatsonWsServer server,
            LoggingModule logging,
            Guid guid,
            string ip,
            int port,
            CancellationTokenSource tokenSource)
        {
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

        private async Task HeartbeatTask(int intervalMs, int maxFailures, CancellationTokenSource tokenSource)
        {
            bool firstRun = true;
            int currentFailures = 0;

            while (!tokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    #region Wait

                    if (!firstRun) await Task.Delay(_Settings.Heartbeat.IntervalMs, tokenSource.Token).ConfigureAwait(false);
                    else firstRun = false;

                    #endregion

                    #region Send-Heartbeat

                    WebsocketMessage message = new WebsocketMessage();
                    message.GUID = GUID;
                    message.Type = WebsocketMessageTypeEnum.Heartbeat;

                    string messageJson = _Serializer.SerializeJson(message, false);
                    byte[] messageBytes = Encoding.UTF8.GetBytes(messageJson);
                    bool success = await _Websocket.SendAsync(GUID, messageBytes, System.Net.WebSockets.WebSocketMessageType.Text, tokenSource.Token).ConfigureAwait(false);
                    if (!success)
                    {
                        currentFailures += 1;
                        if (currentFailures > _Settings.Heartbeat.MaxFailures)
                        {
                            _Logging.Warn(_Header + "heartbeat failure limit exceeded for worker " + GUID + ", removing");
                            Healthy = false;
                            break;
                        }
                    }
                    else
                    {
                        _Logging.Debug(_Header + "worker " + GUID + " heartbeat successful");
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
                }
            }

            _Logging.Info(_Header + "heartbeat operation canceled for worker " + GUID);
        }
    }
}