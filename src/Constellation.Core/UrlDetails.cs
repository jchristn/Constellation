namespace Constellation.Core
{
    using System;
    using System.Collections.Specialized;
    using System.Web;
    using System.Linq;

    /// <summary>
    /// URL details.
    /// This object is used within the request proxied from from the controller to the worker.
    /// </summary>
    public class UrlDetails
    {
        /// <summary>
        /// URI.
        /// </summary>
        public Uri Uri
        {
            get => _Uri;
            set => _Uri = value;
        }

        /// <summary>
        /// URL.
        /// </summary>
        public string Url
        {
            get => _Uri?.ToString() ?? null;
            set => _Uri = (value != null ? new Uri(value) : null);
        }

        /// <summary>
        /// URL without query.
        /// </summary>
        public string UrlWithoutQuery
        {
            get => _Uri != null
                ? _Uri.GetLeftPart(UriPartial.Path)
                : null;
        }

        /// <summary>
        /// Path and query.
        /// </summary>
        public string PathAndQuery
        {
            get => _Uri != null
                ? _Uri.PathAndQuery
                : null;
        }

        /// <summary>
        /// Path.
        /// </summary>
        public string Path
        {
            get => _Uri?.AbsolutePath ?? null;
        }

        /// <summary>
        /// Path segments.
        /// </summary>
        public string[] PathSegments
        {
            get => _Uri?.AbsolutePath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                ?? new string[0];
        }

        /// <summary>
        /// Query elements.
        /// </summary>
        public NameValueCollection QueryElements
        {
            get => _Uri != null
                ? HttpUtility.ParseQueryString(_Uri.Query)
                : new NameValueCollection();
        }

        private Uri _Uri = null;

        /// <summary>
        /// URL details.
        /// </summary>
        public UrlDetails()
        {

        }
    }
}