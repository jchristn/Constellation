namespace Constellation.Controller
{
    using System;

    /// <summary>
    /// Heartbeat settings.
    /// </summary>
    public class HeartbeatSettings
    {
        /// <summary>
        /// Interval at which heartbeats are sent, in milliseconds.  Minimum value is 1000.  Default is 2000.
        /// </summary>
        public int IntervalMs
        {
            get => _IntervalMs;
            set => _IntervalMs = (value >= 1000 ? value : throw new ArgumentOutOfRangeException(nameof(IntervalMs)));
        }

        /// <summary>
        /// Maximum number of heartbeat failures before a worker is taken out of rotation.  Minimum value is 1.  Default is 5.
        /// </summary>
        public int MaxFailures
        {
            get => _MaxFailures;
            set => _MaxFailures = (value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(MaxFailures)));
        }

        private int _IntervalMs = 2000;
        private int _MaxFailures = 5;

        /// <summary>
        /// Heartbeat settings.
        /// </summary>
        public HeartbeatSettings()
        {

        }
    }
}