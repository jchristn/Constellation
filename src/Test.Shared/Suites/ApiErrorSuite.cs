namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Constellation.Core;
    using Touchstone.Core;

    /// <summary>
    /// Tests for ApiErrorResponse and ApiErrorEnum: status-code mapping, messages, and
    /// constructor behavior.
    /// </summary>
    public static class ApiErrorSuite
    {
        private const string SuiteId = "ApiError";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("Default", "ApiErrorResponse defaults to AuthenticationFailed", ct =>
                {
                    ApiErrorResponse resp = new ApiErrorResponse();
                    Check.Equal(ApiErrorEnum.AuthenticationFailed, resp.Error, "default error");
                    Check.Equal(401, resp.StatusCode, "default status code");
                    Check.False(string.IsNullOrEmpty(resp.Message), "default message present");
                    return Task.CompletedTask;
                }),

                Case("Constructor", "ApiErrorResponse constructor sets error, context, description", ct =>
                {
                    ApiErrorResponse resp = new ApiErrorResponse(ApiErrorEnum.NotFound, "ctx", "desc");
                    Check.Equal(ApiErrorEnum.NotFound, resp.Error, "error set");
                    Check.Equal("ctx", (string)resp.Context, "context set");
                    Check.Equal("desc", resp.Description, "description set");
                    Check.Equal(404, resp.StatusCode, "NotFound status code");
                    return Task.CompletedTask;
                }),

                Case("StatusCodeMapping", "ApiErrorResponse maps each error to expected status code", ct =>
                {
                    AssertStatus(ApiErrorEnum.AuthenticationFailed, 401);
                    AssertStatus(ApiErrorEnum.AuthorizationFailed, 401);
                    AssertStatus(ApiErrorEnum.BadGateway, 502);
                    AssertStatus(ApiErrorEnum.BadRequest, 400);
                    AssertStatus(ApiErrorEnum.Conflict, 409);
                    AssertStatus(ApiErrorEnum.DeserializationError, 400);
                    AssertStatus(ApiErrorEnum.Inactive, 401);
                    AssertStatus(ApiErrorEnum.InternalError, 500);
                    AssertStatus(ApiErrorEnum.InvalidEmail, 400);
                    AssertStatus(ApiErrorEnum.InvalidRange, 400);
                    AssertStatus(ApiErrorEnum.InUse, 409);
                    AssertStatus(ApiErrorEnum.NotEmpty, 400);
                    AssertStatus(ApiErrorEnum.NotFound, 404);
                    AssertStatus(ApiErrorEnum.RequestBodyMissing, 400);
                    AssertStatus(ApiErrorEnum.RequiredPropertiesMissing, 400);
                    AssertStatus(ApiErrorEnum.Timeout, 408);
                    AssertStatus(ApiErrorEnum.TokenExpired, 401);
                    AssertStatus(ApiErrorEnum.TooLarge, 413);
                    return Task.CompletedTask;
                }),

                Case("MessagesPresent", "ApiErrorResponse produces a non-empty message for every error", ct =>
                {
                    foreach (ApiErrorEnum error in Enum.GetValues(typeof(ApiErrorEnum)))
                    {
                        ApiErrorResponse resp = new ApiErrorResponse(error);
                        Check.False(string.IsNullOrEmpty(resp.Message), "message present for " + error);
                    }
                    return Task.CompletedTask;
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "API Error Responses", cases);
        }

        private static void AssertStatus(ApiErrorEnum error, int expected)
        {
            ApiErrorResponse resp = new ApiErrorResponse(error);
            Check.Equal(expected, resp.StatusCode, "status code for " + error);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> execute)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, execute);
        }
    }
}
