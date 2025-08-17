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
        /// <summary>
        /// Unknown.
        /// </summary>
        Unknown,
        /// <summary>
        /// Heartbeat.
        /// </summary>
        Heartbeat,
        /// <summary>
        /// Request.
        /// </summary>
        Request,
        /// <summary>
        /// Response.
        /// </summary>
        Response
    }
}