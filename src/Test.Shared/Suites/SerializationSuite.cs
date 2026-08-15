namespace Test.Shared.Suites
{
    using System;
    using System.Globalization;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Core;
    using Constellation.Core.Serialization;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the Serializer and its custom JSON converters (enum, NameValueCollection,
    /// DateTime), plus XML serialization and object copying.
    /// </summary>
    public static class SerializationSuite
    {
        private const string SuiteId = "Serialization";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("SerializeNull", "SerializeJson returns null for a null object", ct =>
                {
                    Serializer s = new Serializer();
                    Check.Null(s.SerializeJson((object)null, false), "null serializes to null");
                    return Task.CompletedTask;
                }),

                Case("JsonRoundTrip", "WebsocketMessage survives a JSON round trip", ct =>
                {
                    Serializer s = new Serializer();
                    WebsocketMessage original = new WebsocketMessage
                    {
                        Type = WebsocketMessageTypeEnum.Response,
                        StatusCode = 201,
                        Method = "POST",
                        ContentType = Constants.JsonContentType,
                        Data = Encoding.UTF8.GetBytes("hello world")
                    };

                    string json = s.SerializeJson(original, false);
                    WebsocketMessage copy = s.DeserializeJson<WebsocketMessage>(json);

                    Check.Equal(original.GUID, copy.GUID, "GUID round trip");
                    Check.Equal(WebsocketMessageTypeEnum.Response, copy.Type, "Type round trip");
                    Check.Equal(201, copy.StatusCode.Value, "StatusCode round trip");
                    Check.Equal("POST", copy.Method, "Method round trip");
                    Check.Equal("hello world", Encoding.UTF8.GetString(copy.Data), "Data round trip");
                    return Task.CompletedTask;
                }),

                Case("EnumSerializedAsString", "Enum values serialize as their string name", ct =>
                {
                    Serializer s = new Serializer();
                    WebsocketMessage msg = new WebsocketMessage { Type = WebsocketMessageTypeEnum.Heartbeat };
                    string json = s.SerializeJson(msg, false);
                    Check.Contains("Heartbeat", json, "enum name present in JSON");
                    return Task.CompletedTask;
                }),

                Case("StrictEnumRejectsInvalid", "Deserializing an invalid enum value throws", ct =>
                {
                    Serializer s = new Serializer();
                    Check.Throws<JsonException>(
                        () => s.DeserializeJson<WebsocketMessage>("{\"Type\":\"NotARealType\"}"),
                        "invalid enum value rejected");
                    return Task.CompletedTask;
                }),

                Case("NameValueCollectionRoundTrip", "Headers survive a JSON round trip", ct =>
                {
                    Serializer s = new Serializer();
                    WebsocketMessage msg = new WebsocketMessage { Type = WebsocketMessageTypeEnum.Request };
                    msg.Headers.Add("X-Custom", "value-1");
                    msg.Headers.Add("X-Second", "value-2");

                    string json = s.SerializeJson(msg, false);
                    WebsocketMessage copy = s.DeserializeJson<WebsocketMessage>(json);

                    Check.Equal("value-1", copy.Headers["X-Custom"], "header 1 round trip");
                    Check.Equal("value-2", copy.Headers["X-Second"], "header 2 round trip");
                    return Task.CompletedTask;
                }),

                Case("DateTimeRoundTrip", "DateTime survives a JSON round trip preserving the instant to microsecond precision", ct =>
                {
                    Serializer s = new Serializer();
                    WebsocketMessage msg = new WebsocketMessage();
                    string json = s.SerializeJson(msg, false);
                    WebsocketMessage copy = s.DeserializeJson<WebsocketMessage>(json);

                    // The converter serializes with a 'Z' suffix and parsing yields a Local
                    // DateTime; the represented instant must be preserved to microseconds.
                    string fmt = "yyyy-MM-ddTHH:mm:ss.ffffff";
                    Check.Equal(
                        msg.TimestampUtc.ToUniversalTime().ToString(fmt, CultureInfo.InvariantCulture),
                        copy.TimestampUtc.ToUniversalTime().ToString(fmt, CultureInfo.InvariantCulture),
                        "timestamp instant round trip");
                    return Task.CompletedTask;
                }),

                Case("DeserializeNullStringThrows", "DeserializeJson rejects a null string", ct =>
                {
                    Serializer s = new Serializer();
                    Check.Throws<ArgumentNullException>(() => s.DeserializeJson<WebsocketMessage>((string)null), "null string rejected");
                    return Task.CompletedTask;
                }),

                Case("DeserializeEmptyStringThrows", "DeserializeJson rejects an empty string", ct =>
                {
                    Serializer s = new Serializer();
                    Check.Throws<ArgumentNullException>(() => s.DeserializeJson<WebsocketMessage>(string.Empty), "empty string rejected");
                    return Task.CompletedTask;
                }),

                Case("DeserializeEmptyBytesThrows", "DeserializeJson rejects an empty byte array", ct =>
                {
                    Serializer s = new Serializer();
                    Check.Throws<ArgumentNullException>(() => s.DeserializeJson<WebsocketMessage>(Array.Empty<byte>()), "empty bytes rejected");
                    return Task.CompletedTask;
                }),

                Case("CopyObject", "CopyObject produces an independent deep copy", ct =>
                {
                    Serializer s = new Serializer();
                    WebsocketMessage original = new WebsocketMessage
                    {
                        Type = WebsocketMessageTypeEnum.Request,
                        Method = "GET"
                    };

                    WebsocketMessage copy = s.CopyObject<WebsocketMessage>(original);
                    Check.NotNull(copy, "copy not null");
                    Check.Equal(original.GUID, copy.GUID, "copy GUID equal");
                    Check.Equal("GET", copy.Method, "copy Method equal");

                    copy.Method = "DELETE";
                    Check.Equal("GET", original.Method, "original unaffected by copy mutation");
                    return Task.CompletedTask;
                }),

                Case("CopyNull", "CopyObject returns default for a null input", ct =>
                {
                    Serializer s = new Serializer();
                    WebsocketMessage copy = s.CopyObject<WebsocketMessage>(null);
                    Check.Null(copy, "null copy returns null");
                    return Task.CompletedTask;
                }),

                Case("XmlRoundTrip", "XML serialization round trips a simple type", ct =>
                {
                    Serializer s = new Serializer();
                    WorkerNode node = new WorkerNode();
                    string xml = s.SerializeXml(node, true);
                    Check.False(string.IsNullOrEmpty(xml), "xml produced");
                    WorkerNode copy = s.DeserializeXml<WorkerNode>(xml);
                    Check.Equal(node.GUID, copy.GUID, "xml GUID round trip");
                    return Task.CompletedTask;
                }),

                Case("SerializeXmlNullThrows", "SerializeXml rejects a null object", ct =>
                {
                    Serializer s = new Serializer();
                    Check.Throws<ArgumentNullException>(() => s.SerializeXml(null, false), "null xml rejected");
                    return Task.CompletedTask;
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Serialization", cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> execute)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, execute);
        }
    }
}
