namespace Constellation.Controller.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Core;
    using SyslogLogging;

    internal class ResponseService : IDisposable
    {
        private string _Header = "[ResponseService] ";
        private Settings _Settings = null;
        private LoggingModule _Logging = null;
        private readonly int _CleanupIntervalMs = 60000; // 60 seconds
        private Task _CleanupTask;
        private CancellationTokenSource _CleanupTokenSource;

        private ConcurrentDictionary<Guid, WebsocketMessage> _Messages = new ConcurrentDictionary<Guid, Core.WebsocketMessage>();
        private bool _Disposed = false;

        internal ResponseService(Settings settings, LoggingModule logging)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));

            StartCleanupTask();

            _Logging.Debug(_Header + "initialized");
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing)
                {
                    StopCleanupTask();
                }

                _Messages.Clear();
                _Messages = null;

                _Disposed = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        internal bool AddResponse(WebsocketMessage msg)
        {
            if (msg == null) return false;
            if (msg.ExpirationUtc == null) msg.ExpirationUtc = DateTime.UtcNow.AddMilliseconds(_Settings.Proxy.ResponseRetentionMs);
            return _Messages.TryAdd(msg.GUID, msg);
        }

        internal async Task<WebsocketMessage> WaitForResponse(Guid guid, int timeoutMs, bool remove = true, CancellationToken token = default)
        {
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(timeoutMs);

                while (!cts.Token.IsCancellationRequested)
                {
                    if (_Messages.TryGetValue(guid, out WebsocketMessage message))
                    {
                        if (remove) _Messages.TryRemove(guid, out _);
                        return message;
                    }

                    await Task.Delay(10, cts.Token);
                }

                throw new TimeoutException($"Timeout waiting for response GUID {guid}.");
            }
        }

        internal void RemoveResponse(Guid guid)
        {
            _Messages.TryRemove(guid, out _);
        }

        private void StartCleanupTask()
        {
            _CleanupTokenSource = new CancellationTokenSource();
            _CleanupTask = Task.Run(async () =>
            {
                while (!_CleanupTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        DateTime now = DateTime.UtcNow;
                        List<Guid> expiredKeys = _Messages
                            .Where(kvp => kvp.Value.ExpirationUtc.HasValue && kvp.Value.ExpirationUtc.Value <= now)
                            .Select(kvp => kvp.Key)
                            .ToList();

                        foreach (Guid key in expiredKeys)
                        {
                            _Messages.TryRemove(key, out _);
                        }

                        await Task.Delay(_CleanupIntervalMs, _CleanupTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, _CleanupTokenSource.Token);
        }

        private void StopCleanupTask()
        {
            _CleanupTokenSource?.Cancel();
            _CleanupTask?.Wait(TimeSpan.FromSeconds(5));
            _CleanupTokenSource?.Dispose();
        }
    }
}
