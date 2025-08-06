namespace Constellation.Controller
{
    using System;

    public class ProxySettings
    {
        public int TimeoutMs
        {
            get => _IntervalMs;
            set => _IntervalMs = (value > 1000 ? value : throw new ArgumentOutOfRangeException(nameof(TimeoutMs)));
        }

        public int ResponseRetentionMs
        {
            get => _ResponseRetentionMs;
            set => _ResponseRetentionMs = (value > 1000 ? value : throw new ArgumentOutOfRangeException(nameof(ResponseRetentionMs))); 
        }

        private int _IntervalMs = 30000;
        private int _ResponseRetentionMs = 30000;
    }
}