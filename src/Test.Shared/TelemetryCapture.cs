namespace Test.Shared
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Threading.Tasks;
    using Constellation.Core.Telemetry;

    /// <summary>
    /// In-memory metric and span collector for tests.  Subscribes to the Constellation and Watson meters and
    /// activity sources exactly as a real host would (by name), records every measurement and every stopped span,
    /// and exposes query helpers.  Dispose to unsubscribe.
    /// </summary>
    public sealed class TelemetryCapture : IDisposable
    {
        private static readonly HashSet<string> _Sources = new HashSet<string>(StringComparer.Ordinal)
        {
            TelemetryConstants.MeterName,
            "Watson"
        };

        private readonly MeterListener _MeterListener;
        private readonly ActivityListener _ActivityListener;
        private readonly ConcurrentQueue<CapturedMeasurement> _Measurements = new ConcurrentQueue<CapturedMeasurement>();
        private readonly ConcurrentQueue<Activity> _Spans = new ConcurrentQueue<Activity>();
        private bool _Disposed = false;

        /// <summary>
        /// Start capturing.
        /// </summary>
        public TelemetryCapture()
        {
            _MeterListener = new MeterListener();
            _MeterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (_Sources.Contains(instrument.Meter.Name)) listener.EnableMeasurementEvents(instrument);
            };
            _MeterListener.SetMeasurementEventCallback<long>((inst, value, tags, state) => Add(inst, value, tags));
            _MeterListener.SetMeasurementEventCallback<int>((inst, value, tags, state) => Add(inst, value, tags));
            _MeterListener.SetMeasurementEventCallback<double>((inst, value, tags, state) => Add(inst, value, tags));
            _MeterListener.Start();

            _ActivityListener = new ActivityListener
            {
                ShouldListenTo = source => _Sources.Contains(source.Name),
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _Spans.Enqueue(activity)
            };
            ActivitySource.AddActivityListener(_ActivityListener);
        }

        /// <summary>
        /// All captured measurements for an instrument.
        /// </summary>
        /// <param name="name">Instrument name.</param>
        /// <returns>Measurements.</returns>
        public List<CapturedMeasurement> Measurements(string name)
        {
            return _Measurements.Where(m => m.Name == name).ToList();
        }

        /// <summary>
        /// Every captured measurement across all instruments.
        /// </summary>
        /// <returns>Measurements.</returns>
        public List<CapturedMeasurement> AllMeasurements()
        {
            return _Measurements.ToList();
        }

        /// <summary>
        /// Sum of measurement values for an instrument whose tags match every supplied key/value pair.
        /// </summary>
        /// <param name="name">Instrument name.</param>
        /// <param name="tags">Tag filter as alternating key, value strings.</param>
        /// <returns>Sum.</returns>
        public double Sum(string name, params string[] tags)
        {
            return Matching(name, tags).Sum(m => m.Value);
        }

        /// <summary>
        /// Count of measurements for an instrument whose tags match every supplied key/value pair.
        /// </summary>
        /// <param name="name">Instrument name.</param>
        /// <param name="tags">Tag filter as alternating key, value strings.</param>
        /// <returns>Count.</returns>
        public int Count(string name, params string[] tags)
        {
            return Matching(name, tags).Count;
        }

        /// <summary>
        /// Sample every observable instrument (gauges) now.
        /// </summary>
        public void RecordObservables()
        {
            _MeterListener.RecordObservableInstruments();
        }

        /// <summary>
        /// All stopped spans with the given name.
        /// </summary>
        /// <param name="name">Span (display) name.</param>
        /// <returns>Spans.</returns>
        public List<Activity> Spans(string name)
        {
            return _Spans.Where(a => a.DisplayName == name || a.OperationName == name).ToList();
        }

        /// <summary>
        /// All stopped spans in a trace.
        /// </summary>
        /// <param name="traceId">Trace id.</param>
        /// <returns>Spans.</returns>
        public List<Activity> Trace(ActivityTraceId traceId)
        {
            return _Spans.Where(a => a.TraceId == traceId).ToList();
        }

        /// <summary>
        /// Poll until a condition holds or the timeout elapses.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>True when the condition held before the timeout.</returns>
        public async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 8000)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                await Task.Delay(50).ConfigureAwait(false);
            }

            return condition();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _MeterListener.Dispose();
            _ActivityListener.Dispose();
        }

        private List<CapturedMeasurement> Matching(string name, string[] tags)
        {
            return _Measurements.Where(m =>
            {
                if (m.Name != name) return false;
                for (int i = 0; i + 1 < tags.Length; i += 2)
                {
                    if (m.Tag(tags[i]) != tags[i + 1]) return false;
                }
                return true;
            }).ToList();
        }

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object>> tags)
        {
            CapturedMeasurement m = new CapturedMeasurement { Name = instrument.Name, Value = value };
            foreach (KeyValuePair<string, object> kvp in tags)
            {
                m.Tags[kvp.Key] = kvp.Value != null ? kvp.Value.ToString() : null;
            }

            _Measurements.Enqueue(m);
        }
    }
}
