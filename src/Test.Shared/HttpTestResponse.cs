namespace Test.Shared
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Simplified view of an HTTP response used by integration tests.
    /// </summary>
    public class HttpTestResponse
    {
        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Response body as a string.
        /// </summary>
        public string Body { get; set; }

        /// <summary>
        /// Content-type of the response.
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// Response headers (both response and content headers are merged).  Lookups are
        /// case-insensitive.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Retrieve a header value, or null if the header is not present.
        /// </summary>
        /// <param name="name">Header name.</param>
        /// <returns>Header value or null.</returns>
        public string Header(string name)
        {
            if (name == null) return null;
            return Headers.TryGetValue(name, out string value) ? value : null;
        }
    }
}
