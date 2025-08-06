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
        public Guid GUID { get; set; } = Guid.NewGuid();
    }
}