namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Thin wrapper over <see cref="HttpClient"/> that returns an <see cref="HttpTestResponse"/>
    /// with status code, body, content-type, and a case-insensitive header collection.
    /// </summary>
    public static class HttpTestClient
    {
        /// <summary>
        /// Send an HTTP request and capture the response.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="url">Full URL.</param>
        /// <param name="body">Optional request body.</param>
        /// <param name="headers">Optional request headers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        public static async Task<HttpTestResponse> SendAsync(
            HttpMethod method,
            string url,
            string body = null,
            IDictionary<string, string> headers = null,
            CancellationToken token = default)
        {
            using (HttpClientHandler handler = new HttpClientHandler())
            using (HttpClient client = new HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromSeconds(30);

                using (HttpRequestMessage request = new HttpRequestMessage(method, url))
                {
                    if (body != null)
                    {
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    }

                    if (headers != null)
                    {
                        foreach (KeyValuePair<string, string> kvp in headers)
                        {
                            request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                        }
                    }

                    using (HttpResponseMessage response = await client.SendAsync(request, token).ConfigureAwait(false))
                    {
                        HttpTestResponse result = new HttpTestResponse
                        {
                            StatusCode = (int)response.StatusCode,
                            Body = await response.Content.ReadAsStringAsync().ConfigureAwait(false),
                            ContentType = response.Content.Headers.ContentType != null
                                ? response.Content.Headers.ContentType.ToString()
                                : null
                        };

                        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
                        {
                            result.Headers[header.Key] = string.Join(",", header.Value);
                        }

                        foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
                        {
                            result.Headers[header.Key] = string.Join(",", header.Value);
                        }

                        return result;
                    }
                }
            }
        }
    }
}
