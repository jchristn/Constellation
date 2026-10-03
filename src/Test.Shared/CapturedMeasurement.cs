namespace Test.Shared
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One metric measurement observed by <see cref="TelemetryCapture"/>.
    /// </summary>
    public sealed class CapturedMeasurement
    {
        /// <summary>
        /// Instrument name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Measured value.
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// Tags (labels) attached to the measurement.  Never null.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Return the tag value for a key, or null when absent.
        /// </summary>
        /// <param name="key">Tag key.</param>
        /// <returns>Tag value or null.</returns>
        public string Tag(string key)
        {
            return Tags.TryGetValue(key, out string value) ? value : null;
        }
    }
}
