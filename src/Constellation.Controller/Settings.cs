namespace Constellation.Controller
{
    using SyslogLogging;
    using System;
    using WatsonWebserver.Core;
    using WatsonWebsocket;

    /// <summary>
    /// Settings.
    /// </summary>
    public class Settings
    {
        /// <summary>
        /// Webserver settings.
        /// </summary>
        public WebserverSettings Webserver
        {
            get => _Webserver;
            set => _Webserver = value ?? throw new ArgumentNullException(nameof(Webserver));
        }

        /// <summary>
        /// Websocket settings.
        /// </summary>
        public WebsocketSettings Websocket
        {
            get => _Websocket;
            set => _Websocket = value ?? throw new ArgumentNullException(nameof(Websocket));
        }

        /// <summary>
        /// Heartbeat settings.
        /// </summary>
        public HeartbeatSettings Heartbeat
        {
            get => _Heartbeat;
            set => _Heartbeat = value ?? throw new ArgumentNullException(nameof(Heartbeat));
        }

        /// <summary>
        /// Proxy settings.
        /// </summary>
        public ProxySettings Proxy
        {
            get => _Proxy;
            set => _Proxy = value ?? throw new ArgumentNullException(nameof(Proxy));
        }

        /// <summary>
        /// Logging settings.
        /// </summary>
        public LoggingSettings Logging
        {
            get => _Logging;
            set => _Logging = value ?? throw new ArgumentNullException(nameof(Logging));
        }

        /// <summary>
        /// Admin settings.
        /// </summary>
        public AdminSettings Admin
        {
            get => _Admin;
            set => _Admin = (value != null ? value : throw new ArgumentNullException(nameof(Admin)));
        }

        private WebserverSettings _Webserver = new WebserverSettings();
        private WebsocketSettings _Websocket = new WebsocketSettings();
        private HeartbeatSettings _Heartbeat = new HeartbeatSettings();
        private ProxySettings _Proxy = new ProxySettings();
        private LoggingSettings _Logging = new LoggingSettings();
        private AdminSettings _Admin = new AdminSettings();

        /// <summary>
        /// Settings.
        /// </summary>
        public Settings()
        {

        }
    }
}