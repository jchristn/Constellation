namespace Constellation.Core
{
    using System;
    using System.Collections.Specialized;
    using System.Web;
    using System.Linq;

    /// <summary>
    /// Websocket message type enum.
    /// </summary>
    public enum WebsocketMessageTypeEnum
    {
        Unknown,
        Heartbeat,
        Request,
        Response
    }
}