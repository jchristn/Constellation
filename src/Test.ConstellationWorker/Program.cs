namespace Test.ConstellationWorker
{
    using System;
    using System.Collections.Specialized;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Core;
    using Constellation.Worker;
    using SyslogLogging;

    public static class Program
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        private static string _Hostname = "localhost";
        private static int _Port = 8001;
        private static bool _Ssl = false;

        public static async Task Main(string[] args)
        {
            Console.WriteLine(Constants.Logo);
            Console.WriteLine("Test Constellation Worker");
            Console.WriteLine();

            if (args.Length >= 1) _Hostname = args[0];
            if (args.Length >= 2) Int32.TryParse(args[1], out _Port);
            if (args.Length >= 3) Boolean.TryParse(args[2], out _Ssl);

            Console.WriteLine("Connecting to controller at " + (_Ssl ? "wss://" : "ws://") + _Hostname + ":" + _Port);
            Console.WriteLine();

            using (CancellationTokenSource tokenSource = new CancellationTokenSource())
            {
                LoggingModule logging = new LoggingModule();

                using (TestWorker worker = new TestWorker(logging, _Hostname, _Port, _Ssl, tokenSource))
                {
                    await worker.Start();

                    Console.WriteLine("Worker started, GUID: " + worker.GUID);
                    Console.WriteLine("Press ENTER to exit");
                    Console.WriteLine();
                    Console.ReadLine();

                    tokenSource.Cancel();
                }
            }
        }

#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    }

    public class TestWorker : ConstellationWorkerBase
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        private string _Header = "[TestWorker] ";
        private bool _Disposed = false;

        public TestWorker(LoggingModule logging, string hostname, int port, bool ssl, CancellationTokenSource tokenSource)
            : base(logging, hostname, port, ssl, tokenSource)
        {
        }

        public override async Task OnConnection(Guid guid)
        {
            Console.WriteLine(_Header + "Connected to controller (GUID: " + guid + ")");
        }

        public override async Task OnDisconnection(Guid guid)
        {
            if (!_Disposed)
                Console.WriteLine(_Header + "Disconnected from controller (GUID: " + guid + ")");
        }

        public override async Task<WebsocketMessage> OnRequestReceived(WebsocketMessage req)
        {
            if (req.Type.Equals(WebsocketMessageTypeEnum.Heartbeat)) return null;

            string method = req.Method ?? "UNKNOWN";
            string path = req.Url != null ? req.Url.Path : "/";
            string body = req.Data != null && req.Data.Length > 0
                ? Encoding.UTF8.GetString(req.Data)
                : "(empty)";

            Console.WriteLine();
            Console.WriteLine(_Header + "Request received:");
            Console.WriteLine("  Method      : " + method);
            Console.WriteLine("  Path        : " + path);
            Console.WriteLine("  Content-Type: " + (req.ContentType ?? "(none)"));
            Console.WriteLine("  Body        : " + body);
            Console.WriteLine();

            WebsocketMessage resp = new WebsocketMessage
            {
                GUID = req.GUID,
                Type = WebsocketMessageTypeEnum.Response,
                StatusCode = 200,
                ContentType = Constants.JsonContentType,
                Headers = new NameValueCollection(),
                Data = Encoding.UTF8.GetBytes("{\"status\":\"ok\",\"message\":\"Request processed successfully\",\"worker\":\"" + GUID + "\"}")
            };

            return resp;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                _Disposed = true;
                Console.WriteLine(_Header + "Disposing...");
                try
                {
                    base.Dispose(disposing);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(_Header + "Disposal error (expected): " + ex.GetType().Name);
                }
            }
        }

#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    }
}
