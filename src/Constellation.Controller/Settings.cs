namespace Constellation.Controller
{
    using SyslogLogging;
    using System;
    using WatsonWebserver.Core;
    using WatsonWebsocket;

    public class Settings
    {
        public WebserverSettings Webserver
        {
            get => _Webserver;
            set => _Webserver = value ?? throw new ArgumentNullException(nameof(Webserver));
        }

        public WebsocketSettings Websocket
        {
            get => _Websocket;
            set => _Websocket = value ?? throw new ArgumentNullException(nameof(Websocket));
        }

        public HeartbeatSettings Heartbeat
        {
            get => _Heartbeat;
            set => _Heartbeat = value ?? throw new ArgumentNullException(nameof(Heartbeat));
        }

        public ProxySettings Proxy
        {
            get => _Proxy;
            set => _Proxy = value ?? throw new ArgumentNullException(nameof(Proxy));
        }

        public LoggingSettings Logging
        {
            get => _Logging;
            set => _Logging = value ?? throw new ArgumentNullException(nameof(Logging));
        }

        private WebserverSettings _Webserver = new WebserverSettings();
        private WebsocketSettings _Websocket = new WebsocketSettings();
        private HeartbeatSettings _Heartbeat = new HeartbeatSettings();
        private ProxySettings _Proxy = new ProxySettings();
        private LoggingSettings _Logging = new LoggingSettings();
    }
}