namespace Test.Shared
{
    using System.Threading;

    /// <summary>
    /// Thread-safe allocator that hands out unique HTTP/WebSocket port pairs so that
    /// integration test cases never collide, even when a runner executes them in parallel.
    /// </summary>
    public static class PortAllocator
    {
        private static int _Counter = 0;
        private const int _HttpBase = 21000;

        /// <summary>
        /// Retrieve the next unique port pair.
        /// </summary>
        /// <returns>Port pair.</returns>
        public static PortPair Next()
        {
            int index = Interlocked.Increment(ref _Counter);
            int http = _HttpBase + (index * 10);
            return new PortPair(http, http + 1);
        }
    }
}
