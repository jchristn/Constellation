namespace Constellation.Core.Telemetry
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Reflection;
    using System.Runtime.InteropServices;

    /// <summary>
    /// The single <see cref="System.Diagnostics.Metrics.Meter"/> and <see cref="System.Diagnostics.ActivitySource"/>
    /// through which every Constellation component emits metrics and spans.  Constellation takes no dependency on any
    /// exporter: a host subscribes to <see cref="TelemetryConstants.MeterName"/> and
    /// <see cref="TelemetryConstants.ActivitySourceName"/> (for example with Radiant or the OpenTelemetry SDK).  When
    /// nothing is subscribed every recording is a near-free no-op and every span start returns null.
    /// All recording is best-effort: a failing listener never propagates an exception into request handling.
    /// Thread safety: all members are thread safe.
    /// </summary>
    public static class ConstellationTelemetry
    {
        #region Public-Members

        /// <summary>
        /// The Constellation meter, named <see cref="TelemetryConstants.MeterName"/>.  Never null.
        /// </summary>
        public static Meter Meter
        {
            get => _Meter;
        }

        /// <summary>
        /// The Constellation activity source, named <see cref="TelemetryConstants.ActivitySourceName"/>.  Never null.
        /// </summary>
        public static ActivitySource ActivitySource
        {
            get => _ActivitySource;
        }

        /// <summary>
        /// The Constellation build version reported on the meter, activity source, and build-info gauge.  Never null.
        /// </summary>
        public static string Version
        {
            get => _Version;
        }

        #endregion

        #region Internal-Members

        internal static readonly Counter<long> ProxyRequests;
        internal static readonly Histogram<double> ProxyRequestDuration;
        internal static readonly Histogram<double> ProxyStageDuration;
        internal static readonly Counter<long> ProxyStageEvents;
        internal static readonly UpDownCounter<long> ProxyActiveRequests;
        internal static readonly Counter<long> PlacementDecisions;
        internal static readonly Histogram<double> PlacementDuration;
        internal static readonly Counter<long> WorkerPoolEvents;
        internal static readonly Histogram<double> WorkerSessionDuration;
        internal static readonly Counter<long> Heartbeats;
        internal static readonly Histogram<double> HeartbeatDuration;
        internal static readonly Counter<long> ResponsesReceived;
        internal static readonly Counter<long> ResponsesExpired;
        internal static readonly Histogram<double> ResponsesCleanupDuration;
        internal static readonly Counter<long> WebsocketMessages;
        internal static readonly Histogram<double> WebsocketMessageSize;
        internal static readonly Counter<long> AdminRequests;
        internal static readonly Counter<long> WorkerRequests;
        internal static readonly Histogram<double> WorkerRequestDuration;
        internal static readonly Histogram<double> WorkerHandlerDuration;
        internal static readonly Counter<long> WorkerConnectionAttempts;
        internal static readonly Counter<long> WorkerConnectionEvents;
        internal static readonly Counter<long> Errors;

        #endregion

        #region Private-Members

        private static readonly string _Version = ResolveVersion();
        private static readonly Meter _Meter = new Meter(TelemetryConstants.MeterName, _Version);
        private static readonly ActivitySource _ActivitySource = new ActivitySource(TelemetryConstants.ActivitySourceName, _Version);
        private static readonly ConcurrentDictionary<IControllerTelemetryState, byte> _Controllers = new ConcurrentDictionary<IControllerTelemetryState, byte>();
        private static readonly ConcurrentDictionary<IWorkerTelemetryState, byte> _Workers = new ConcurrentDictionary<IWorkerTelemetryState, byte>();
        private static readonly string _Runtime = RuntimeInformation.FrameworkDescription;

        #endregion

        #region Constructors-and-Factories

        static ConstellationTelemetry()
        {
            ProxyRequests = _Meter.CreateCounter<long>(TelemetryConstants.ProxyRequests, "{request}", "Proxied requests by outcome and HTTP method.");
            ProxyRequestDuration = _Meter.CreateHistogram<double>(TelemetryConstants.ProxyRequestDuration, "s", "End-to-end proxy duration from placement through response.");
            ProxyStageDuration = _Meter.CreateHistogram<double>(TelemetryConstants.ProxyStageDuration, "s", "Per-stage proxy duration.");
            ProxyStageEvents = _Meter.CreateCounter<long>(TelemetryConstants.ProxyStageEvents, "{event}", "Proxy stage executions by stage and outcome.");
            ProxyActiveRequests = _Meter.CreateUpDownCounter<long>(TelemetryConstants.ProxyActiveRequests, "{request}", "Proxy requests currently in flight.");
            PlacementDecisions = _Meter.CreateCounter<long>(TelemetryConstants.PlacementDecisions, "{decision}", "Resource placement decisions.");
            PlacementDuration = _Meter.CreateHistogram<double>(TelemetryConstants.PlacementDuration, "s", "Resource placement decision duration.");
            WorkerPoolEvents = _Meter.CreateCounter<long>(TelemetryConstants.WorkerPoolEvents, "{event}", "Worker pool lifecycle events.");
            WorkerSessionDuration = _Meter.CreateHistogram<double>(TelemetryConstants.WorkerSessionDuration, "s", "Worker session lifetime from registration to disconnection.");
            Heartbeats = _Meter.CreateCounter<long>(TelemetryConstants.Heartbeats, "{heartbeat}", "Heartbeats sent by the controller, by outcome.");
            HeartbeatDuration = _Meter.CreateHistogram<double>(TelemetryConstants.HeartbeatDuration, "s", "Heartbeat send duration.");
            ResponsesReceived = _Meter.CreateCounter<long>(TelemetryConstants.ResponsesReceived, "{response}", "Worker responses offered to the correlation store, by outcome.");
            ResponsesExpired = _Meter.CreateCounter<long>(TelemetryConstants.ResponsesExpired, "{response}", "Orphaned responses evicted from the correlation store.");
            ResponsesCleanupDuration = _Meter.CreateHistogram<double>(TelemetryConstants.ResponsesCleanupDuration, "s", "Correlation-store cleanup pass duration.");
            WebsocketMessages = _Meter.CreateCounter<long>(TelemetryConstants.WebsocketMessages, "{message}", "WebSocket messages by component, direction, type, and outcome.");
            WebsocketMessageSize = _Meter.CreateHistogram<double>(TelemetryConstants.WebsocketMessageSize, "By", "WebSocket message size.");
            AdminRequests = _Meter.CreateCounter<long>(TelemetryConstants.AdminRequests, "{request}", "Administrative API requests by operation and outcome.");
            WorkerRequests = _Meter.CreateCounter<long>(TelemetryConstants.WorkerRequests, "{request}", "Requests handled by a worker, by outcome.");
            WorkerRequestDuration = _Meter.CreateHistogram<double>(TelemetryConstants.WorkerRequestDuration, "s", "Worker request handling duration.");
            WorkerHandlerDuration = _Meter.CreateHistogram<double>(TelemetryConstants.WorkerHandlerDuration, "s", "Application request handler duration on the worker.");
            WorkerConnectionAttempts = _Meter.CreateCounter<long>(TelemetryConstants.WorkerConnectionAttempts, "{attempt}", "Worker connection attempts to the controller, by outcome.");
            WorkerConnectionEvents = _Meter.CreateCounter<long>(TelemetryConstants.WorkerConnectionEvents, "{event}", "Worker connection state changes.");
            Errors = _Meter.CreateCounter<long>(TelemetryConstants.Errors, "{error}", "Caught errors by component, operation, and error type.");

            _Meter.CreateObservableGauge<long>(TelemetryConstants.Workers, ObserveWorkers, "{worker}", "Workers registered with the controller, by state.");
            _Meter.CreateObservableGauge<long>(TelemetryConstants.ResourcesMapped, () => SumControllers(c => c.MappedResources), "{resource}", "Pinned resources currently mapped to workers.");
            _Meter.CreateObservableGauge<long>(TelemetryConstants.ResponsesPending, () => SumControllers(c => c.PendingResponses), "{response}", "Worker responses held in the correlation store.");
            _Meter.CreateObservableGauge<double>(TelemetryConstants.HeartbeatLastSuccess, ObserveLastHeartbeat, "s", "Most recent successful heartbeat, Unix time.");
            _Meter.CreateObservableGauge<double>(TelemetryConstants.ResponsesCleanupLastSuccess, ObserveLastCleanup, "s", "Most recent successful correlation-store cleanup, Unix time.");
            _Meter.CreateObservableGauge<double>(TelemetryConstants.ConfigHeartbeatInterval, ObserveHeartbeatInterval, "s", "Configured heartbeat interval.");
            _Meter.CreateObservableGauge<long>(TelemetryConstants.ConfigHeartbeatMaxFailures, ObserveHeartbeatMaxFailures, "{failure}", "Configured maximum heartbeat failures before eviction.");
            _Meter.CreateObservableGauge<double>(TelemetryConstants.ConfigProxyTimeout, ObserveProxyTimeout, "s", "Configured proxy timeout.");
            _Meter.CreateObservableGauge<long>(TelemetryConstants.WorkerConnected, ObserveWorkerConnected, "{worker}", "Worker instances in this process connected to a controller.");
            _Meter.CreateObservableGauge<long>(TelemetryConstants.BuildInfo, ObserveBuildInfo, null, "Build information carried as labels; value is always 1.");
        }

        #endregion

        #region Internal-Methods

        internal static void RegisterController(IControllerTelemetryState state)
        {
            if (state != null) _Controllers.TryAdd(state, 0);
        }

        internal static void UnregisterController(IControllerTelemetryState state)
        {
            if (state != null) _Controllers.TryRemove(state, out _);
        }

        internal static void RegisterWorker(IWorkerTelemetryState state)
        {
            if (state != null) _Workers.TryAdd(state, 0);
        }

        internal static void UnregisterWorker(IWorkerTelemetryState state)
        {
            if (state != null) _Workers.TryRemove(state, out _);
        }

        internal static long StartTimestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        internal static double ElapsedSeconds(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) / (double)Stopwatch.Frequency;
        }

        internal static void Add(Counter<long> counter, long value, in TagList tags)
        {
            try
            {
                counter.Add(value, tags);
            }
            catch (Exception)
            {
                // best-effort: a failing listener must never affect the caller
            }
        }

        internal static void Add(UpDownCounter<long> counter, long value, in TagList tags)
        {
            try
            {
                counter.Add(value, tags);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static void Record(Histogram<double> histogram, double value, in TagList tags)
        {
            try
            {
                histogram.Record(value, tags);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static void RecordError(string component, string operation, Exception e)
        {
            RecordError(component, operation, e != null ? e.GetType().FullName : TelemetryConstants.OutcomeError);
        }

        internal static void RecordError(string component, string operation, string errorType)
        {
            TagList tags = new TagList
            {
                { TelemetryConstants.LabelComponent, component },
                { TelemetryConstants.LabelOperation, operation },
                { TelemetryConstants.LabelErrorType, errorType ?? TelemetryConstants.OutcomeError }
            };

            Add(Errors, 1, tags);
        }

        internal static void RecordWebsocketMessage(string component, string direction, WebsocketMessageTypeEnum type, string outcome, int sizeBytes)
        {
            string typeName = MessageTypeName(type);

            TagList countTags = new TagList
            {
                { TelemetryConstants.LabelComponent, component },
                { TelemetryConstants.LabelDirection, direction },
                { TelemetryConstants.LabelMessageType, typeName },
                { TelemetryConstants.LabelOutcome, outcome }
            };

            Add(WebsocketMessages, 1, countTags);

            if (sizeBytes >= 0)
            {
                TagList sizeTags = new TagList
                {
                    { TelemetryConstants.LabelComponent, component },
                    { TelemetryConstants.LabelDirection, direction },
                    { TelemetryConstants.LabelMessageType, typeName }
                };

                Record(WebsocketMessageSize, sizeBytes, sizeTags);
            }
        }

        internal static Activity StartActivity(string name, ActivityKind kind)
        {
            try
            {
                return _ActivitySource.StartActivity(name, kind);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static Activity StartActivity(string name, ActivityKind kind, ActivityContext parent, DateTimeOffset startTime = default)
        {
            try
            {
                // With no explicit parent this must be a root span.  Background loops and socket receive callbacks
                // run on an ExecutionContext captured when they were started, so Activity.Current there can be a
                // long-finished span (for example the connect or register span); never inherit it.
                if (parent == default(ActivityContext)) Activity.Current = null;
                return _ActivitySource.StartActivity(name, kind, parent, null, null, startTime);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static Activity StartActivityFromTraceParent(string name, ActivityKind kind, string traceParent, string traceState)
        {
            ActivityContext parent = default;
            try
            {
                if (!String.IsNullOrEmpty(traceParent))
                    ActivityContext.TryParse(traceParent, traceState, true, out parent);
            }
            catch (Exception)
            {
                parent = default;
            }

            return StartActivity(name, kind, parent);
        }

        internal static void Inject(Activity activity, WebsocketMessage msg)
        {
            if (msg == null) return;

            try
            {
                Activity source = activity ?? Activity.Current;
                if (source == null || source.IdFormat != ActivityIdFormat.W3C) return;
                msg.TraceParent = source.Id;
                msg.TraceState = source.TraceStateString;
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static void SetTag(Activity activity, string key, object value)
        {
            if (activity == null) return;

            try
            {
                activity.SetTag(key, value);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static void SetOk(Activity activity, string outcome = TelemetryConstants.OutcomeSuccess)
        {
            if (activity == null) return;

            try
            {
                activity.SetTag(TelemetryConstants.AttrOutcome, outcome);
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static void SetError(Activity activity, string outcome, string description)
        {
            if (activity == null) return;

            try
            {
                activity.SetTag(TelemetryConstants.AttrOutcome, outcome);
                activity.SetTag(TelemetryConstants.AttrErrorType, outcome);
                activity.SetStatus(ActivityStatusCode.Error, description);
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static void SetException(Activity activity, Exception e, string outcome = TelemetryConstants.OutcomeError)
        {
            if (activity == null || e == null) return;

            try
            {
                activity.SetTag(TelemetryConstants.AttrOutcome, outcome);
                activity.SetTag(TelemetryConstants.AttrErrorType, e.GetType().FullName);
                activity.SetStatus(ActivityStatusCode.Error, e.Message);

                ActivityTagsCollection tags = new ActivityTagsCollection
                {
                    { "exception.type", e.GetType().FullName },
                    { "exception.message", e.Message },
                    { "exception.stacktrace", e.ToString() }
                };

                activity.AddEvent(new ActivityEvent("exception", default, tags));
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static void Stop(Activity activity)
        {
            if (activity == null) return;

            try
            {
                activity.Dispose();
            }
            catch (Exception)
            {
                // best-effort
            }
        }

        internal static string NormalizeMethod(string method)
        {
            if (String.IsNullOrEmpty(method)) return TelemetryConstants.HttpMethodOther;

            switch (method.ToUpperInvariant())
            {
                case "GET": return "GET";
                case "HEAD": return "HEAD";
                case "POST": return "POST";
                case "PUT": return "PUT";
                case "DELETE": return "DELETE";
                case "PATCH": return "PATCH";
                case "OPTIONS": return "OPTIONS";
                case "TRACE": return "TRACE";
                case "CONNECT": return "CONNECT";
                default: return TelemetryConstants.HttpMethodOther;
            }
        }

        internal static string MessageTypeName(WebsocketMessageTypeEnum type)
        {
            switch (type)
            {
                case WebsocketMessageTypeEnum.Request: return "request";
                case WebsocketMessageTypeEnum.Response: return "response";
                case WebsocketMessageTypeEnum.Heartbeat: return "heartbeat";
                default: return "unknown";
            }
        }

        internal static double UnixSeconds(DateTime utc)
        {
            return (utc - DateTime.UnixEpoch).TotalSeconds;
        }

        #endregion

        #region Private-Methods

        private static string ResolveVersion()
        {
            try
            {
                Assembly assembly = typeof(ConstellationTelemetry).Assembly;
                AssemblyInformationalVersionAttribute info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string version = info != null ? info.InformationalVersion : assembly.GetName().Version?.ToString();
                if (String.IsNullOrEmpty(version)) return "unknown";
                int plus = version.IndexOf('+');
                return plus > 0 ? version.Substring(0, plus) : version;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static long SumControllers(Func<IControllerTelemetryState, long> selector)
        {
            long total = 0;
            foreach (IControllerTelemetryState state in _Controllers.Keys)
            {
                try
                {
                    total += selector(state);
                }
                catch (Exception)
                {
                    // best-effort
                }
            }

            return total;
        }

        private static double MaxControllers(Func<IControllerTelemetryState, double> selector)
        {
            double max = 0;
            foreach (IControllerTelemetryState state in _Controllers.Keys)
            {
                try
                {
                    double value = selector(state);
                    if (value > max) max = value;
                }
                catch (Exception)
                {
                    // best-effort
                }
            }

            return max;
        }

        private static IEnumerable<Measurement<long>> ObserveWorkers()
        {
            if (_Controllers.IsEmpty) return Array.Empty<Measurement<long>>();

            return new Measurement<long>[]
            {
                new Measurement<long>(SumControllers(c => c.HealthyWorkers), new KeyValuePair<string, object>(TelemetryConstants.LabelState, TelemetryConstants.StateHealthy)),
                new Measurement<long>(SumControllers(c => c.UnhealthyWorkers), new KeyValuePair<string, object>(TelemetryConstants.LabelState, TelemetryConstants.StateUnhealthy))
            };
        }

        private static IEnumerable<Measurement<double>> ObserveLastHeartbeat()
        {
            double value = MaxControllers(c => c.LastHeartbeatSuccessUnixSeconds);
            if (value <= 0) return Array.Empty<Measurement<double>>();
            return new Measurement<double>[] { new Measurement<double>(value) };
        }

        private static IEnumerable<Measurement<double>> ObserveLastCleanup()
        {
            double value = MaxControllers(c => c.LastCleanupSuccessUnixSeconds);
            if (value <= 0) return Array.Empty<Measurement<double>>();
            return new Measurement<double>[] { new Measurement<double>(value) };
        }

        private static IEnumerable<Measurement<double>> ObserveHeartbeatInterval()
        {
            if (_Controllers.IsEmpty) return Array.Empty<Measurement<double>>();
            return new Measurement<double>[] { new Measurement<double>(MaxControllers(c => c.HeartbeatIntervalSeconds)) };
        }

        private static IEnumerable<Measurement<long>> ObserveHeartbeatMaxFailures()
        {
            if (_Controllers.IsEmpty) return Array.Empty<Measurement<long>>();
            return new Measurement<long>[] { new Measurement<long>((long)MaxControllers(c => c.HeartbeatMaxFailures)) };
        }

        private static IEnumerable<Measurement<double>> ObserveProxyTimeout()
        {
            if (_Controllers.IsEmpty) return Array.Empty<Measurement<double>>();
            return new Measurement<double>[] { new Measurement<double>(MaxControllers(c => c.ProxyTimeoutSeconds)) };
        }

        private static IEnumerable<Measurement<long>> ObserveWorkerConnected()
        {
            if (_Workers.IsEmpty) return Array.Empty<Measurement<long>>();

            long connected = 0;
            foreach (IWorkerTelemetryState state in _Workers.Keys)
            {
                try
                {
                    if (state.IsConnected) connected++;
                }
                catch (Exception)
                {
                    // best-effort
                }
            }

            return new Measurement<long>[] { new Measurement<long>(connected) };
        }

        private static IEnumerable<Measurement<long>> ObserveBuildInfo()
        {
            return new Measurement<long>[]
            {
                new Measurement<long>(
                    1,
                    new KeyValuePair<string, object>(TelemetryConstants.LabelVersion, _Version),
                    new KeyValuePair<string, object>(TelemetryConstants.LabelRuntime, _Runtime))
            };
        }

        #endregion
    }
}
