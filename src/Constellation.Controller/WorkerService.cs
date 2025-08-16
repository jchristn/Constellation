namespace Constellation.Controller
{
    using SyslogLogging;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Worker service.
    /// </summary>
    public class WorkerService
    {
        public List<WorkerMetadata> Workers
        {
            get
            {
                lock (_WorkersLock)
                {
                    return new List<WorkerMetadata>(_Workers);
                }
            }
        }

        private string _Header = "[WorkerService] ";
        private Settings _Settings = null;
        private LoggingModule _Logging = null;
        private List<WorkerMetadata> _Workers = new List<WorkerMetadata>();
        private readonly object _WorkersLock = new object();
        private int _LastIndex = 0;

        // Resource to Worker mapping
        private Dictionary<string, Guid> _ResourceToWorkerMap = new Dictionary<string, Guid>();
        private readonly object _ResourceMapLock = new object();

        /// <summary>
        /// Worker service.
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="logging"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public WorkerService(Settings settings, LoggingModule logging)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <summary>
        /// Retrieve worker by resource.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>WorkerMetadata.</returns>
        public WorkerMetadata GetByResource(string resource)
        {
            if (String.IsNullOrEmpty(resource)) throw new ArgumentNullException(nameof(resource));

            lock (_ResourceMapLock)
            {
                // Check if resource is already mapped to a worker
                if (_ResourceToWorkerMap.ContainsKey(resource))
                {
                    Guid workerGuid = _ResourceToWorkerMap[resource];

                    // Check if the mapped worker is still available
                    lock (_WorkersLock)
                    {
                        var worker = _Workers.FirstOrDefault(w => w.GUID == workerGuid);
                        if (worker != null && worker.Healthy)
                        {
                            _Logging.Debug(_Header + $"resource '{resource}' mapped to existing worker {workerGuid}");
                            return worker;
                        }
                        else
                        {
                            // Worker is no longer available, remove mapping
                            _ResourceToWorkerMap.Remove(resource);
                            _Logging.Info(_Header + $"previous worker {workerGuid} for resource '{resource}' is no longer available");
                        }
                    }
                }

                // No existing mapping or worker unavailable, select new worker using round-robin
                lock (_WorkersLock)
                {
                    if (_Workers.Count < 1)
                    {
                        _Logging.Warn(_Header + "no workers available to satisfy request to resource " + resource);
                        return null;
                    }

                    // Find next healthy worker using round-robin
                    WorkerMetadata selectedWorker = null;
                    int attempts = 0;

                    while (selectedWorker == null && attempts < _Workers.Count)
                    {
                        _LastIndex++;
                        if (_LastIndex >= _Workers.Count)
                        {
                            _LastIndex = 0;
                        }

                        var candidate = _Workers[_LastIndex];
                        if (candidate.Healthy)
                        {
                            selectedWorker = candidate;
                        }

                        attempts++;
                    }

                    if (selectedWorker == null)
                    {
                        _Logging.Warn(_Header + "no healthy workers available to satisfy request to resource " + resource);
                        return null;
                    }

                    // Map the resource to this worker
                    _ResourceToWorkerMap[resource] = selectedWorker.GUID;
                    _Logging.Info(_Header + $"Resource '{resource}' newly mapped to worker {selectedWorker.GUID}");

                    return selectedWorker;
                }
            }
        }

        /// <summary>
        /// Retrieve worker by GUID.
        /// </summary>
        /// <param name="guid">GUID.</param>
        /// <returns>Worker metadata.</returns>
        public WorkerMetadata GetByGuid(Guid guid)
        {
            lock (_WorkersLock)
            {
                if (_Workers.Any(w => w.GUID.Equals(guid))) return _Workers.First(w => w.GUID.Equals(guid));
            }

            return null;
        }

        /// <summary>
        /// Add a worker.
        /// </summary>
        /// <param name="worker">Worker metadata.</param>
        public void AddWorker(WorkerMetadata worker)
        {
            if (worker == null) throw new ArgumentNullException(nameof(worker));

            lock (_WorkersLock)
            {
                _Workers.Add(worker);
                _Logging.Info(_Header + $"added worker {worker.GUID} to pool (total: {_Workers.Count})");
            }
        }

        /// <summary>
        /// Remove a worker by GUID.
        /// </summary>
        /// <param name="guid">GUID.</param>
        /// <returns>True if removed.</returns>
        public bool RemoveWorker(Guid guid)
        {
            lock (_WorkersLock)
            {
                bool removed = _Workers.RemoveAll(w => w.GUID == guid) > 0;

                if (removed)
                {
                    _Logging.Info(_Header + $"removed worker {guid} from pool (remaining: {_Workers.Count})");

                    // Clean up any resource mappings for this worker
                    lock (_ResourceMapLock)
                    {
                        var resourcesToRemove = _ResourceToWorkerMap
                            .Where(kvp => kvp.Value == guid)
                            .Select(kvp => kvp.Key)
                            .ToList();

                        foreach (var resource in resourcesToRemove)
                        {
                            _ResourceToWorkerMap.Remove(resource);
                            _Logging.Debug(_Header + $"removed resource mapping for '{resource}' due to worker removal");
                        }
                    }
                }

                return removed;
            }
        }

        /// <summary>
        /// Retrieve resource mappings.
        /// </summary>
        /// <returns>Dictionary where the key is the resource and the value is the GUID of the worker.</returns>
        public Dictionary<string, Guid> GetResourceMappings()
        {
            lock (_ResourceMapLock)
            {
                return new Dictionary<string, Guid>(_ResourceToWorkerMap);
            }
        }

        /// <summary>
        /// Remove resource mappings for a given resource.
        /// </summary>
        /// <param name="resource">Resource.</param>
        public void ClearResourceMapping(string resource)
        {
            if (String.IsNullOrEmpty(resource)) return;

            lock (_ResourceMapLock)
            {
                if (_ResourceToWorkerMap.Remove(resource))
                {
                    _Logging.Info(_Header + $"cleared resource mapping for '{resource}'");
                }
            }
        }
    }
}