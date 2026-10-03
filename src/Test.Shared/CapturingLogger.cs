namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// In-memory <see cref="ILogger"/> that records every structured log entry, including the
    /// named template values, so tests can assert on what the controller logged.
    /// </summary>
    public sealed class CapturingLogger : ILogger
    {
        /// <summary>
        /// When true, every call to <see cref="Log{TState}"/> throws after recording the entry.
        /// </summary>
        public bool ThrowOnLog { get; set; } = false;

        private readonly List<CapturedLogEntry> _Entries = new List<CapturedLogEntry>();
        private readonly object _Lock = new object();

        /// <summary>
        /// Snapshot of the captured entries.
        /// </summary>
        public IReadOnlyList<CapturedLogEntry> Entries
        {
            get
            {
                lock (_Lock) return _Entries.ToList();
            }
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Dictionary<string, object> values = new Dictionary<string, object>();
            if (state is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                foreach (KeyValuePair<string, object> pair in pairs) values[pair.Key] = pair.Value;
            }

            CapturedLogEntry entry = new CapturedLogEntry(
                logLevel,
                formatter != null ? formatter(state, exception) : null,
                values,
                exception);

            lock (_Lock) _Entries.Add(entry);

            if (ThrowOnLog) throw new InvalidOperationException("CapturingLogger configured to throw");
        }

        /// <summary>
        /// Wait until an entry matching the predicate has been captured.
        /// </summary>
        /// <param name="predicate">Predicate.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>The first matching entry, or null on timeout.</returns>
        public async Task<CapturedLogEntry> WaitForAsync(Func<CapturedLogEntry, bool> predicate, int timeoutMs = 8000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                CapturedLogEntry match = Entries.FirstOrDefault(predicate);
                if (match != null || DateTime.UtcNow >= deadline) return match;
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// One structured log entry captured by <see cref="CapturingLogger"/>.
    /// </summary>
    public sealed class CapturedLogEntry
    {
        /// <summary>
        /// Log level.
        /// </summary>
        public LogLevel Level { get; }

        /// <summary>
        /// Rendered message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Named template values, plus the original template under "{OriginalFormat}".
        /// </summary>
        public IReadOnlyDictionary<string, object> Values { get; }

        /// <summary>
        /// Exception, if any.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Original message template.
        /// </summary>
        public string Template
        {
            get => Values.TryGetValue("{OriginalFormat}", out object t) ? t as string : null;
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="level">Log level.</param>
        /// <param name="message">Rendered message.</param>
        /// <param name="values">Named template values.</param>
        /// <param name="exception">Exception.</param>
        public CapturedLogEntry(LogLevel level, string message, IReadOnlyDictionary<string, object> values, Exception exception)
        {
            Level = level;
            Message = message;
            Values = values;
            Exception = exception;
        }
    }
}
