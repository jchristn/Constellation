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
        public Uri Uri
        {
            get => _Uri;
            set => _Uri = value;
        }

        public string Url
        {
            get => _Uri?.ToString() ?? null;
            set => _Uri = (value != null ? new Uri(value) : null);
        }

        public string UrlWithoutQuery
        {
            get => _Uri != null
                ? _Uri.GetLeftPart(UriPartial.Path)
                : null;
        }

        public string PathAndQuery
        {
            get => _Uri != null
                ? _Uri.PathAndQuery
                : null;
        }

        public string Path
        {
            get => _Uri?.AbsolutePath ?? null;
        }

        public string[] PathSegments
        {
            get => _Uri?.AbsolutePath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                ?? new string[0];
        }

        public NameValueCollection QueryElements
        {
            get => _Uri != null
                ? HttpUtility.ParseQueryString(_Uri.Query)
                : new NameValueCollection();
        }

        private Uri _Uri = null;
    }
}