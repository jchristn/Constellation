namespace Constellation.Core
{
    using System;
    using System.Collections.Specialized;
    using System.Web;
    using System.Linq;

    /// <summary>
    /// Worker removed event.
    /// </summary>
    public class WorkerRemovedEventArgs : EventArgs
    {
        /// <summary>
        /// GUID of the removed worker.
        /// </summary>
        public Guid GUID { get; set; } = Guid.NewGuid();
    }
}