namespace Constellation.Core
{
    using System;
    using System.Collections.Specialized;
    using System.Web;
    using System.Linq;

    /// <summary>
    /// Worker node.
    /// </summary>
    public class WorkerNode
    {
        /// <summary>
        /// GUID.
        /// </summary>
        public Guid GUID { get; set; } = Guid.NewGuid();
    }
}