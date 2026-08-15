namespace Test.Shared
{
    /// <summary>
    /// A pair of TCP ports; one for the controller HTTP listener and one for the
    /// controller WebSocket listener.
    /// </summary>
    public class PortPair
    {
        /// <summary>
        /// HTTP port used for client (REST) requests.
        /// </summary>
        public int Http { get; private set; }

        /// <summary>
        /// WebSocket port used for worker connections.
        /// </summary>
        public int Websocket { get; private set; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="http">HTTP port.</param>
        /// <param name="websocket">WebSocket port.</param>
        public PortPair(int http, int websocket)
        {
            Http = http;
            Websocket = websocket;
        }
    }
}
