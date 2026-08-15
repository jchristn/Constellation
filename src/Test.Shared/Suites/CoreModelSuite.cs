namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Constellation.Core;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the core data models: WebsocketMessage, UrlDetails, node types, constants,
    /// and the message type enum.
    /// </summary>
    public static class CoreModelSuite
    {
        private const string SuiteId = "CoreModel";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("WebsocketMessage_Defaults", "WebsocketMessage has sane defaults", ct =>
                {
                    WebsocketMessage msg = new WebsocketMessage();
                    Check.NotEqual(Guid.Empty, msg.GUID, "GUID default");
                    Check.Equal(WebsocketMessageTypeEnum.Unknown, msg.Type, "Type default");
                    Check.Null(msg.StatusCode, "StatusCode default");
                    Check.Null(msg.ExpirationUtc, "ExpirationUtc default");
                    Check.NotNull(msg.Headers, "Headers default not null");
                    Check.NotNull(msg.Data, "Data default not null");
                    Check.Equal(0, msg.Data.Length, "Data default empty");
                    return Task.CompletedTask;
                }),

                Case("WebsocketMessage_StatusCodeValid", "WebsocketMessage accepts valid status codes", ct =>
                {
                    WebsocketMessage msg = new WebsocketMessage();
                    msg.StatusCode = 200;
                    Check.Equal(200, msg.StatusCode.Value, "200 accepted");
                    msg.StatusCode = 100;
                    Check.Equal(100, msg.StatusCode.Value, "100 accepted");
                    msg.StatusCode = 599;
                    Check.Equal(599, msg.StatusCode.Value, "599 accepted");
                    msg.StatusCode = null;
                    Check.Null(msg.StatusCode, "null accepted");
                    return Task.CompletedTask;
                }),

                Case("WebsocketMessage_StatusCodeTooLow", "WebsocketMessage rejects status code below 100", ct =>
                {
                    WebsocketMessage msg = new WebsocketMessage();
                    Check.Throws<ArgumentOutOfRangeException>(() => msg.StatusCode = 99, "99 rejected");
                    return Task.CompletedTask;
                }),

                Case("WebsocketMessage_StatusCodeTooHigh", "WebsocketMessage rejects status code above 599", ct =>
                {
                    WebsocketMessage msg = new WebsocketMessage();
                    Check.Throws<ArgumentOutOfRangeException>(() => msg.StatusCode = 600, "600 rejected");
                    return Task.CompletedTask;
                }),

                Case("WebsocketMessage_NullHeadersCoalesced", "WebsocketMessage replaces null headers with empty collection", ct =>
                {
                    WebsocketMessage msg = new WebsocketMessage();
                    msg.Headers = null;
                    Check.NotNull(msg.Headers, "Headers coalesced to non-null");
                    Check.Equal(0, msg.Headers.Count, "Headers empty after null set");
                    return Task.CompletedTask;
                }),

                Case("WebsocketMessage_NullDataCoalesced", "WebsocketMessage replaces null data with empty array", ct =>
                {
                    WebsocketMessage msg = new WebsocketMessage();
                    msg.Data = null;
                    Check.NotNull(msg.Data, "Data coalesced to non-null");
                    Check.Equal(0, msg.Data.Length, "Data empty after null set");
                    return Task.CompletedTask;
                }),

                Case("UrlDetails_Defaults", "UrlDetails has null/empty defaults", ct =>
                {
                    UrlDetails url = new UrlDetails();
                    Check.Null(url.Uri, "Uri default null");
                    Check.Null(url.Url, "Url default null");
                    Check.Null(url.Path, "Path default null");
                    Check.Null(url.PathAndQuery, "PathAndQuery default null");
                    Check.Null(url.UrlWithoutQuery, "UrlWithoutQuery default null");
                    Check.NotNull(url.PathSegments, "PathSegments not null");
                    Check.Equal(0, url.PathSegments.Length, "PathSegments empty");
                    Check.NotNull(url.QueryElements, "QueryElements not null");
                    return Task.CompletedTask;
                }),

                Case("UrlDetails_Parsing", "UrlDetails parses a full URL", ct =>
                {
                    UrlDetails url = new UrlDetails();
                    url.Url = "http://localhost:8000/api/users?x=1&y=2";
                    Check.NotNull(url.Uri, "Uri parsed");
                    Check.Equal("/api/users", url.Path, "Path parsed");
                    Check.Equal(2, url.PathSegments.Length, "PathSegments count");
                    Check.Equal("api", url.PathSegments[0], "PathSegments[0]");
                    Check.Equal("users", url.PathSegments[1], "PathSegments[1]");
                    Check.Equal("1", url.QueryElements["x"], "Query x");
                    Check.Equal("2", url.QueryElements["y"], "Query y");
                    Check.Equal("http://localhost:8000/api/users", url.UrlWithoutQuery, "UrlWithoutQuery");
                    Check.Equal("/api/users?x=1&y=2", url.PathAndQuery, "PathAndQuery");
                    return Task.CompletedTask;
                }),

                Case("UrlDetails_SetNull", "UrlDetails accepts a null URL", ct =>
                {
                    UrlDetails url = new UrlDetails();
                    url.Url = "http://localhost/x";
                    url.Url = null;
                    Check.Null(url.Uri, "Uri null after null set");
                    return Task.CompletedTask;
                }),

                Case("UrlDetails_InvalidUrlThrows", "UrlDetails rejects a malformed URL", ct =>
                {
                    UrlDetails url = new UrlDetails();
                    Check.Throws<UriFormatException>(() => url.Url = "notaurl", "malformed URL rejected");
                    return Task.CompletedTask;
                }),

                Case("WorkerNode_Guid", "WorkerNode generates a non-empty GUID", ct =>
                {
                    WorkerNode node = new WorkerNode();
                    Check.NotEqual(Guid.Empty, node.GUID, "WorkerNode GUID");
                    return Task.CompletedTask;
                }),

                Case("ControllerNode_Guid", "ControllerNode defaults to empty GUID", ct =>
                {
                    ControllerNode node = new ControllerNode();
                    Check.Equal(Guid.Empty, node.GUID, "ControllerNode GUID default");
                    return Task.CompletedTask;
                }),

                Case("WorkerRemovedEventArgs_Guid", "WorkerRemovedEventArgs has a GUID", ct =>
                {
                    WorkerRemovedEventArgs args = new WorkerRemovedEventArgs();
                    Check.NotEqual(Guid.Empty, args.GUID, "WorkerRemovedEventArgs GUID");
                    return Task.CompletedTask;
                }),

                Case("HttpResponseDetails_Headers", "HttpResponseDetails headers never null", ct =>
                {
                    HttpResponseDetails details = new HttpResponseDetails();
                    Check.NotNull(details.Headers, "Headers default not null");
                    details.Headers = null;
                    Check.NotNull(details.Headers, "Headers coalesced after null set");
                    return Task.CompletedTask;
                }),

                Case("Constants_Values", "Constants expose expected values", ct =>
                {
                    Check.False(string.IsNullOrEmpty(Constants.Logo), "Logo present");
                    Check.Equal("application/json", Constants.JsonContentType, "JsonContentType");
                    Check.Equal("text/plain", Constants.TextContentType, "TextContentType");
                    Check.Equal("application/octet-stream", Constants.BinaryContentType, "BinaryContentType");
                    Check.Equal("text/html", Constants.HtmlContentType, "HtmlContentType");
                    Check.Equal("authorization", Constants.AuthorizationHeader, "AuthorizationHeader");
                    Check.Equal("x-worker", Constants.WorkerNameHeader, "WorkerNameHeader");
                    Check.Equal("x-request", Constants.RequestGuidHeader, "RequestGuidHeader");
                    Check.Equal("x-forwarded-for", Constants.ForwardedForHeader, "ForwardedForHeader");
                    return Task.CompletedTask;
                }),

                Case("MessageTypeEnum_Values", "WebsocketMessageTypeEnum exposes all values", ct =>
                {
                    Check.Equal(0, (int)WebsocketMessageTypeEnum.Unknown, "Unknown ordinal");
                    Check.True(Enum.IsDefined(typeof(WebsocketMessageTypeEnum), WebsocketMessageTypeEnum.Heartbeat), "Heartbeat defined");
                    Check.True(Enum.IsDefined(typeof(WebsocketMessageTypeEnum), WebsocketMessageTypeEnum.Request), "Request defined");
                    Check.True(Enum.IsDefined(typeof(WebsocketMessageTypeEnum), WebsocketMessageTypeEnum.Response), "Response defined");
                    return Task.CompletedTask;
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Core Data Models", cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<System.Threading.CancellationToken, Task> execute)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, execute);
        }
    }
}
