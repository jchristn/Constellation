namespace Constellation.Controller.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Core;
    using Constellation.Core.Telemetry;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
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
        private ILogger _StructuredLogger = NullLogger.Instance;
        private long _LastCleanupSuccessTicks = 0;

        internal ResponseService(Settings settings, LoggingModule logging, ILogger structuredLogger = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _StructuredLogger = structuredLogger ?? NullLogger.Instance;

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

        internal void SetStructuredLogger(ILogger logger)
        {
            _StructuredLogger = logger ?? NullLogger.Instance;
        }

        internal long PendingCount
        {
            get
            {
                ConcurrentDictionary<Guid, WebsocketMessage> messages = _Messages;
                return messages != null ? messages.Count : 0;
            }
        }

        internal DateTime LastCleanupSuccessUtc
        {
            get
            {
                long ticks = Interlocked.Read(ref _LastCleanupSuccessTicks);
                return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
            }
        }

        internal bool AddResponse(WebsocketMessage msg)
        {
            if (msg == null)
            {
                RecordReceived(TelemetryConstants.OutcomeDiscarded);
                return false;
            }

            if (msg.ExpirationUtc == null) msg.ExpirationUtc = DateTime.UtcNow.AddMilliseconds(_Settings.Proxy.ResponseRetentionMs);
            bool added = _Messages.TryAdd(msg.GUID, msg);
            RecordReceived(added ? TelemetryConstants.OutcomeSuccess : TelemetryConstants.OutcomeDuplicate);
            return added;
        }

        internal async Task<WebsocketMessage> WaitForResponse(Guid guid, int timeoutMs, bool remove = true, CancellationToken token = default)
        {
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(timeoutMs);

                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        if (_Messages.TryGetValue(guid, out WebsocketMessage message))
                        {
                            if (remove) _Messages.TryRemove(guid, out _);
                            return message;
                        }

                        await Task.Delay(10, cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // The linked token fired because the timeout elapsed (not because the caller canceled);
                    // surface it as a timeout so the caller can answer 408 instead of a generic 500.
                }

                token.ThrowIfCancellationRequested();
                throw new TimeoutException($"Timeout waiting for response GUID {guid}.");
            }
        }

        internal int CleanupExpired()
        {
            Activity span = ConstellationTelemetry.StartActivity(TelemetryConstants.SpanResponsesCleanup, ActivityKind.Internal, default(ActivityContext));
            long start = ConstellationTelemetry.StartTimestamp();
            string outcome = TelemetryConstants.OutcomeSuccess;
            int removed = 0;

            try
            {
                DateTime now = DateTime.UtcNow;
                List<Guid> expiredKeys = _Messages
                    .Where(kvp => kvp.Value.ExpirationUtc.HasValue && kvp.Value.ExpirationUtc.Value <= now)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (Guid key in expiredKeys)
                {
                    if (_Messages.TryRemove(key, out _)) removed++;
                }

                if (removed > 0)
                {
                    ConstellationTelemetry.Add(ConstellationTelemetry.ResponsesExpired, removed, default(TagList));
                    _Logging.Debug(_Header + "evicted " + removed + " expired response(s)");
                    try
                    {
                        _StructuredLogger.LogInformation("Evicted {ExpiredCount} orphaned worker response(s) from the correlation store", removed);
                    }
                    catch (Exception)
                    {
                        // best-effort
                    }
                }

                Interlocked.Exchange(ref _LastCleanupSuccessTicks, DateTime.UtcNow.Ticks);
                ConstellationTelemetry.SetTag(span, TelemetryConstants.AttrCount, removed);
                ConstellationTelemetry.SetOk(span);
                return removed;
            }
            catch (Exception e)
            {
                outcome = TelemetryConstants.OutcomeError;
                ConstellationTelemetry.RecordError(TelemetryConstants.ComponentController, TelemetryConstants.OperationCleanup, e);
                ConstellationTelemetry.SetException(span, e);
                _Logging.Warn(_Header + "cleanup exception:" + Environment.NewLine + e.ToString());
                return removed;
            }
            finally
            {
                TagList tags = new TagList { { TelemetryConstants.LabelOutcome, outcome } };
                ConstellationTelemetry.Record(ConstellationTelemetry.ResponsesCleanupDuration, ConstellationTelemetry.ElapsedSeconds(start), tags);
                ConstellationTelemetry.Stop(span);
            }
        }

        private static void RecordReceived(string outcome)
        {
            TagList tags = new TagList { { TelemetryConstants.LabelOutcome, outcome } };
            ConstellationTelemetry.Add(ConstellationTelemetry.ResponsesReceived, 1, tags);
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
                        CleanupExpired();

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
