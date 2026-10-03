namespace Constellation.ControllerServer
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Controller;
    using Constellation.Core;
    using Constellation.Core.Serialization;
    using SyslogLogging;

    public static class Program
    {
        private static string _Header = "[Constellation] ";
        private static int _ProcessId = Environment.ProcessId;
        private static Serializer _Serializer = new Serializer();
        private static Settings _Settings = null;
        private static LoggingModule _Logging = null;
        private static CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private static Controller _Controller = null;
        private static TelemetryHost _Telemetry = null;
        private static Microsoft.Extensions.Logging.ILogger _StructuredLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public static async Task Main(string[] args)
        {
            Welcome();
            LoadSettings();

            await InitializeGlobals();

            _Logging.Info(_Header + "starting at " + DateTime.UtcNow + " using process ID " + _ProcessId);

            EventWaitHandle waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
            AssemblyLoadContext.Default.Unloading += (ctx) => waitHandle.Set();

            // On SIGTERM (docker stop) cancel the default immediate termination so Main can stop the controller and
            // flush and dispose the telemetry host before exiting.
            using PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, (signal) =>
            {
                signal.Cancel = true;
                waitHandle.Set();
            });
            Console.CancelKeyPress += async (sender, eventArgs) =>
            {
                waitHandle.Set();
                eventArgs.Cancel = true;

                await _Controller.Stop();
                if (!_TokenSource.IsCancellationRequested) _TokenSource.Cancel();
            };

            bool waitHandleSignal = false;
            do
            {
                waitHandleSignal = waitHandle.WaitOne(1000);
            }
            while (!waitHandleSignal);

            _Logging.Info(_Header + "stopping at " + DateTime.UtcNow);
            LogStructured("Constellation controller stopping");

            try
            {
                _Controller?.Dispose();
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "exception disposing controller: " + e.Message);
            }

            _Telemetry?.Dispose();
        }

        private static void LogStructured(string message)
        {
            try
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(_StructuredLogger, message);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        private static void Welcome()
        {
            Console.WriteLine(
                Environment.NewLine +
                Constants.Logo +
                Environment.NewLine +
                Constants.Copyright +
                Environment.NewLine);
        }

        private static void LoadSettings()
        {
            if (!File.Exists(Constants.SettingsFile))
            {
                Console.WriteLine("Settings file " + Constants.SettingsFile + " does not exist, creating");

                _Settings = new Settings();
                _Settings.Websocket.Hostnames.Add("localhost");
                _Settings.Websocket.Port = 8001;

                File.WriteAllText(Constants.SettingsFile, _Serializer.SerializeJson(_Settings, true));
            }
            else
            {
                _Settings = _Serializer.DeserializeJson<Settings>(File.ReadAllText(Constants.SettingsFile));
            }
        }

        private static async Task InitializeGlobals()
        {
            #region Logging

            Console.WriteLine("Initializing logging");

            if (_Settings.Logging.Servers.Count > 0)
                _Logging = new LoggingModule(_Settings.Logging.Servers);
            else
                _Logging = new LoggingModule();

            _Logging.Settings.MinimumSeverity = (SyslogLogging.Severity)_Settings.Logging.MinimumSeverity;
            _Logging.Settings.EnableConsole = _Settings.Logging.ConsoleLogging;
            _Logging.Settings.EnableColors = _Settings.Logging.EnableColors;

            if (!String.IsNullOrEmpty(_Settings.Logging.LogDirectory))
            {
                if (!Directory.Exists(_Settings.Logging.LogDirectory))
                    Directory.CreateDirectory(_Settings.Logging.LogDirectory);

                _Settings.Logging.LogFilename = _Settings.Logging.LogDirectory + _Settings.Logging.LogFilename;
            }

            if (!String.IsNullOrEmpty(_Settings.Logging.LogFilename))
            {
                _Logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
                _Logging.Settings.LogFilename = _Settings.Logging.LogFilename;
            }

            _Logging.Debug(_Header + "logging initialized");

            #endregion

            #region Telemetry

            if (!_Settings.Webserver.Telemetry.Enable)
                _Logging.Warn(_Header + "Watson HTTP telemetry is disabled (Webserver.Telemetry.Enable = false); HTTP metrics and request spans will not be emitted");

            _Telemetry = new TelemetryHost(_Settings, _Logging);
            _StructuredLogger = _Telemetry.CreateLogger("Constellation.ControllerServer");

            #endregion

            #region Controller

            _Controller = new Controller(_Settings, _Logging, _TokenSource);
            _Controller.Logger = _Telemetry.CreateLogger("Constellation.Controller");
            await _Controller.Start();
            LogStructured("Constellation controller started");

            string restScheme = _Settings.Webserver.Ssl.Enable ? "https" : "http";
            Console.WriteLine("REST server listening on " + restScheme + "://" + _Settings.Webserver.Hostname + ":" + _Settings.Webserver.Port);

            string wsScheme = _Settings.Websocket.Ssl ? "wss" : "ws";
            foreach (string hostname in _Settings.Websocket.Hostnames)
            {
                Console.WriteLine("Websocket server listening on " + wsScheme + "://" + hostname + ":" + _Settings.Websocket.Port);
            }

            #endregion
        }
    }

    internal class Controller : ConstellationControllerBase
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        public override async Task OnConnection(Guid guid, string ipAddress, int port)
        {
        }

        public override async Task OnDisconnection(Guid guid, string ipAddress, int port)
        {
        }

        public Controller(Settings settings, LoggingModule logging, CancellationTokenSource tokenSource) : base(settings, logging, tokenSource)
        {
        }

#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    }
}