namespace Constellation.Controller
{
    using System;

    public class HeartbeatSettings
    {
        public int IntervalMs
        {
            get => _IntervalMs;
            set => _IntervalMs = (value > 1000 ? value : throw new ArgumentOutOfRangeException(nameof(IntervalMs)));
        }

        public int MaxFailures
        {
            get => _MaxFailures;
            set => _MaxFailures = (value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxFailures)));
        }

        private int _IntervalMs = 2000;
        private int _MaxFailures = 5;
    }
}