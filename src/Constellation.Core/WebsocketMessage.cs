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
        public Guid GUID { get; set; } = Guid.NewGuid();
        public WebsocketMessageTypeEnum Type { get; set; } = WebsocketMessageTypeEnum.Unknown;
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ExpirationUtc { get; set; } = null;
        public int? StatusCode
        {
            get => _StatusCode;
            set
            {
                if (value == null) _StatusCode = value;
                else _StatusCode = (value.Value >= 100 && value.Value <= 599) ? value : throw new ArgumentOutOfRangeException(nameof(StatusCode));
            }
        }
        public string Method { get; set; } = null;
        public string ContentType { get; set; } = null;
        public UrlDetails Url { get; set; } = null;
        public NameValueCollection Headers
        {
            get => _Headers;
            set => _Headers = value ?? new NameValueCollection(StringComparer.InvariantCultureIgnoreCase);
        }
        public byte[] Data
        {
            get => _Data;
            set => _Data = (value != null ? value : Array.Empty<byte>());
        }

        private NameValueCollection _Headers = new NameValueCollection(StringComparer.InvariantCultureIgnoreCase);
        private byte[] _Data = Array.Empty<byte>();
        private int? _StatusCode = null;

        public WebsocketMessage()
        {

        }
    }
}