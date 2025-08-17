namespace Constellation.Controller
{
    using System;

    /// <summary>
    /// Proxy settings.
    /// </summary>
    public class ProxySettings
    {
        /// <summary>
        /// Request timeout, in milliseconds.  Minimum value is 1000.  Default is 30000.
        /// </summary>
        public int TimeoutMs
        {
            get => _IntervalMs;
            set => _IntervalMs = (value >= 1000 ? value : throw new ArgumentOutOfRangeException(nameof(TimeoutMs)));
        }

        /// <summary>
        /// Response retention time, in milliseconds.  Minimum value is 1000.  Default is 30000.
        /// </summary>
        public int ResponseRetentionMs
        {
            get => _ResponseRetentionMs;
            set => _ResponseRetentionMs = (value >= 1000 ? value : throw new ArgumentOutOfRangeException(nameof(ResponseRetentionMs))); 
        }

        private int _IntervalMs = 30000;
        private int _ResponseRetentionMs = 30000;

        /// <summary>
        /// Proxy settings.
        /// </summary>
        public ProxySettings()
        {

        }
    }
}