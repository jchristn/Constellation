namespace Constellation.Core
{
    using System;
    using System.Collections.Specialized;
    using System.Web;
    using System.Linq;

    /// <summary>
    /// Websocket message.
    /// </summary>
    public class WebsocketMessage
    {
        /// <summary>
        /// GUID.
        /// </summary>
        public Guid GUID { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Tye type of message.
        /// </summary>
        public WebsocketMessageTypeEnum Type { get; set; } = WebsocketMessageTypeEnum.Unknown;

        /// <summary>
        /// Timestamp of the message.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Expiration timestamp for the message.
        /// </summary>
        public DateTime? ExpirationUtc { get; set; } = null;

        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int? StatusCode
        {
            get => _StatusCode;
            set
            {
                if (value == null) _StatusCode = value;
                else _StatusCode = (value.Value >= 100 && value.Value <= 599) ? value : throw new ArgumentOutOfRangeException(nameof(StatusCode));
            }
        }

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method { get; set; } = null;

        /// <summary>
        /// Content-type.
        /// </summary>
        public string ContentType { get; set; } = null;

        /// <summary>
        /// URL details.
        /// </summary>
        public UrlDetails Url { get; set; } = null;

        /// <summary>
        /// Headers.
        /// </summary>
        public NameValueCollection Headers
        {
            get => _Headers;
            set => _Headers = value ?? new NameValueCollection(StringComparer.InvariantCultureIgnoreCase);
        }

        /// <summary>
        /// Data.
        /// </summary>
        public byte[] Data
        {
            get => _Data;
            set => _Data = (value != null ? value : Array.Empty<byte>());
        }

        private NameValueCollection _Headers = new NameValueCollection(StringComparer.InvariantCultureIgnoreCase);
        private byte[] _Data = Array.Empty<byte>();
        private int? _StatusCode = null;

        /// <summary>
        /// Websocket message.
        /// </summary>
        public WebsocketMessage()
        {

        }
    }
}