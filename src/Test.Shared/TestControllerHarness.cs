namespace Test.Shared
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Controller;
    using SyslogLogging;

    /// <summary>
    /// Minimal concrete controller used by the integration test suite.
    /// </summary>
    public class TestControllerHarness : ConstellationControllerBase
    {
        /// <summary>
        /// Logging module supplied at construction.
        /// </summary>
        public LoggingModule Logging { get; private set; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="tokenSource">Cancellation token source.</param>
        public TestControllerHarness(Settings settings, LoggingModule logging, CancellationTokenSource tokenSource)
            : base(settings, logging, tokenSource)
        {
            Logging = logging;
        }

        /// <inheritdoc />
        public override Task OnConnection(Guid guid, string ipAddress, int port)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task OnDisconnection(Guid guid, string ipAddress, int port)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    this.Stop().Wait(5000);
                }
                catch
                {
                }
            }

            base.Dispose(disposing);
        }
    }
}
