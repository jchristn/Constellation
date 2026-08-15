namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Controller;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the settings types: defaults and validation guards on HeartbeatSettings,
    /// ProxySettings, AdminSettings, LoggingSettings, and the aggregate Settings type.
    /// </summary>
    public static class SettingsSuite
    {
        private const string SuiteId = "Settings";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("HeartbeatDefaults", "HeartbeatSettings exposes documented defaults", ct =>
                {
                    HeartbeatSettings h = new HeartbeatSettings();
                    Check.Equal(2000, h.IntervalMs, "default IntervalMs");
                    Check.Equal(5, h.MaxFailures, "default MaxFailures");
                    return Task.CompletedTask;
                }),

                Case("HeartbeatIntervalGuard", "HeartbeatSettings rejects an interval below 1000", ct =>
                {
                    HeartbeatSettings h = new HeartbeatSettings();
                    Check.Throws<ArgumentOutOfRangeException>(() => h.IntervalMs = 999, "interval < 1000 rejected");
                    h.IntervalMs = 1000;
                    Check.Equal(1000, h.IntervalMs, "interval 1000 accepted");
                    return Task.CompletedTask;
                }),

                Case("HeartbeatMaxFailuresGuard", "HeartbeatSettings rejects max failures below 1", ct =>
                {
                    HeartbeatSettings h = new HeartbeatSettings();
                    Check.Throws<ArgumentOutOfRangeException>(() => h.MaxFailures = 0, "max failures < 1 rejected");
                    h.MaxFailures = 1;
                    Check.Equal(1, h.MaxFailures, "max failures 1 accepted");
                    return Task.CompletedTask;
                }),

                Case("ProxyDefaults", "ProxySettings exposes documented defaults", ct =>
                {
                    ProxySettings p = new ProxySettings();
                    Check.Equal(30000, p.TimeoutMs, "default TimeoutMs");
                    Check.Equal(30000, p.ResponseRetentionMs, "default ResponseRetentionMs");
                    return Task.CompletedTask;
                }),

                Case("ProxyTimeoutGuard", "ProxySettings rejects a timeout below 1000", ct =>
                {
                    ProxySettings p = new ProxySettings();
                    Check.Throws<ArgumentOutOfRangeException>(() => p.TimeoutMs = 500, "timeout < 1000 rejected");
                    Check.Throws<ArgumentOutOfRangeException>(() => p.ResponseRetentionMs = 500, "retention < 1000 rejected");
                    return Task.CompletedTask;
                }),

                Case("AdminDefaults", "AdminSettings exposes documented defaults", ct =>
                {
                    AdminSettings a = new AdminSettings();
                    Check.Equal("x-api-key", a.ApiKeyHeader, "default ApiKeyHeader");
                    Check.NotNull(a.ApiKeys, "ApiKeys not null");
                    Check.True(a.ApiKeys.Contains("constellationadmin"), "default admin key present");
                    return Task.CompletedTask;
                }),

                Case("AdminApiKeyHeaderGuard", "AdminSettings rejects an empty API key header", ct =>
                {
                    AdminSettings a = new AdminSettings();
                    Check.Throws<ArgumentNullException>(() => a.ApiKeyHeader = null, "null header rejected");
                    Check.Throws<ArgumentNullException>(() => a.ApiKeyHeader = string.Empty, "empty header rejected");
                    return Task.CompletedTask;
                }),

                Case("AdminApiKeysGuard", "AdminSettings rejects an empty API key list", ct =>
                {
                    AdminSettings a = new AdminSettings();
                    Check.Throws<ArgumentException>(() => a.ApiKeys = new List<string>(), "empty key list rejected");
                    Check.Throws<ArgumentException>(() => a.ApiKeys = null, "null key list rejected");
                    return Task.CompletedTask;
                }),

                Case("LoggingDefaults", "LoggingSettings exposes documented defaults", ct =>
                {
                    LoggingSettings l = new LoggingSettings();
                    Check.Equal(0, l.MinimumSeverity, "default MinimumSeverity");
                    Check.True(l.ConsoleLogging, "console logging default true");
                    Check.NotNull(l.Servers, "servers not null");
                    return Task.CompletedTask;
                }),

                Case("LoggingSeverityGuard", "LoggingSettings enforces a 0-7 severity range", ct =>
                {
                    LoggingSettings l = new LoggingSettings();
                    Check.Throws<ArgumentOutOfRangeException>(() => l.MinimumSeverity = -1, "severity -1 rejected");
                    Check.Throws<ArgumentOutOfRangeException>(() => l.MinimumSeverity = 8, "severity 8 rejected");
                    l.MinimumSeverity = 7;
                    Check.Equal(7, l.MinimumSeverity, "severity 7 accepted");
                    return Task.CompletedTask;
                }),

                Case("LoggingServersNullCoalesced", "LoggingSettings coalesces a null server list", ct =>
                {
                    LoggingSettings l = new LoggingSettings();
                    l.Servers = null;
                    Check.NotNull(l.Servers, "servers coalesced to non-null");
                    return Task.CompletedTask;
                }),

                Case("SettingsDefaults", "Settings has non-null sub-settings by default", ct =>
                {
                    Settings s = new Settings();
                    Check.NotNull(s.Webserver, "Webserver not null");
                    Check.NotNull(s.Websocket, "Websocket not null");
                    Check.NotNull(s.Heartbeat, "Heartbeat not null");
                    Check.NotNull(s.Proxy, "Proxy not null");
                    Check.NotNull(s.Logging, "Logging not null");
                    Check.NotNull(s.Admin, "Admin not null");
                    return Task.CompletedTask;
                }),

                Case("SettingsNullGuards", "Settings rejects null sub-settings", ct =>
                {
                    Settings s = new Settings();
                    Check.Throws<ArgumentNullException>(() => s.Heartbeat = null, "null Heartbeat rejected");
                    Check.Throws<ArgumentNullException>(() => s.Proxy = null, "null Proxy rejected");
                    Check.Throws<ArgumentNullException>(() => s.Admin = null, "null Admin rejected");
                    Check.Throws<ArgumentNullException>(() => s.Logging = null, "null Logging rejected");
                    return Task.CompletedTask;
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Settings and Validation", cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> execute)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, execute);
        }
    }
}
