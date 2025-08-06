namespace Constellation.ControllerServer
{
    using System;
    using System.IO;
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

        public static async Task Main(string[] args)
        {
            Welcome();
            LoadSettings();

            await InitializeGlobals();

            _Logging.Info(_Header + "starting at " + DateTime.UtcNow + " using process ID " + _ProcessId);

            EventWaitHandle waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
            AssemblyLoadContext.Default.Unloading += (ctx) => waitHandle.Set();
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

            #region Controller

            _Controller = new Controller(_Settings, _Logging, _TokenSource);
            await _Controller.Start();

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