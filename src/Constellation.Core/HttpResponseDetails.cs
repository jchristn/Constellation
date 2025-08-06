namespace Constellation.Core
{
    using System;
    using System.Collections.Specialized;

    /// <summary>
    /// HTTP response details.
    /// This object is used within the response sent to the controller by the worker.
    /// </summary>
    public class HttpResponseDetails
    {
        public NameValueCollection Headers
        {
            get => _Headers;
            set => _Headers = value ?? new NameValueCollection(StringComparer.InvariantCultureIgnoreCase);
        }

        private int _StatusCode = 200;
        private NameValueCollection _Headers = new NameValueCollection(StringComparer.InvariantCultureIgnoreCase);
    }
}
