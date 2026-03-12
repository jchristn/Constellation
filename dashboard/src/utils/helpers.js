export function workerGuid(worker) {
  return worker.GUID || worker.Guid || '';
}

export function workerDisplayName(worker) {
  const ip = worker.Ip || worker.IP || '?';
  const port = worker.Port || '?';
  return ip + ':' + port;
}

export function timeAgo(dateStr) {
  if (!dateStr) return '-';
  const d = new Date(dateStr);
  if (isNaN(d.getTime())) return dateStr;
  const now = new Date();
  const diff = Math.floor((now - d) / 1000);
  if (diff < 5) return 'just now';
  if (diff < 60) return diff + 's ago';
  if (diff < 3600) return Math.floor(diff / 60) + 'm ago';
  if (diff < 86400) return Math.floor(diff / 3600) + 'h ago';
  return Math.floor(diff / 86400) + 'd ago';
}

export function formatDate(dateStr) {
  if (!dateStr) return '-';
  const d = new Date(dateStr);
  if (isNaN(d.getTime())) return dateStr;
  return d.toLocaleString();
}

export function buildResourceList(workers, resourceMap) {
  const list = [];
  for (const [wGuid, resources] of Object.entries(resourceMap)) {
    if (Array.isArray(resources)) {
      for (const res of resources) {
        const worker = workers.find(w => workerGuid(w) === wGuid);
        list.push({
          resource: res,
          workerGuid: wGuid,
          workerIp: worker ? (worker.Ip || worker.IP || '') : '',
          workerPort: worker ? (worker.Port || '') : '',
          workerHealthy: worker ? worker.Healthy : null,
        });
      }
    }
  }
  return list;
}
