import { useState, useRef, useMemo } from 'react';
import { workerGuid, workerDisplayName } from '../../utils/helpers';
import CopyableId from '../CopyableId';
import './TabStyles.css';

const ResourcesTab = ({ workers, resourceList, loading, onRefresh }) => {
  const [filter, setFilter] = useState('');
  const [workerFilter, setWorkerFilter] = useState('');
  const [sort, setSort] = useState({ column: null, asc: true });
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const refreshRef = useRef(null);

  const filtered = useMemo(() => {
    let list = [...resourceList];
    if (filter) {
      const q = filter.toLowerCase();
      list = list.filter(r => r.resource.toLowerCase().includes(q));
    }
    if (workerFilter) {
      list = list.filter(r => r.workerGuid === workerFilter);
    }
    if (sort.column) {
      list.sort((a, b) => {
        let va, vb;
        switch (sort.column) {
          case 'resource': va = a.resource; vb = b.resource; break;
          case 'worker': va = a.workerGuid; vb = b.workerGuid; break;
          case 'ip': va = a.workerIp; vb = b.workerIp; break;
          default: va = ''; vb = '';
        }
        if (va < vb) return sort.asc ? -1 : 1;
        if (va > vb) return sort.asc ? 1 : -1;
        return 0;
      });
    }
    return list;
  }, [resourceList, filter, workerFilter, sort]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const paginated = filtered.slice((page - 1) * pageSize, page * pageSize);

  const handleSort = (col) => {
    if (sort.column === col) {
      setSort({ column: col, asc: !sort.asc });
    } else {
      setSort({ column: col, asc: true });
    }
    setPage(1);
  };

  const sortIcon = (col) => {
    if (sort.column !== col) return '\u2195';
    return sort.asc ? '\u2191' : '\u2193';
  };

  const handleRefresh = async () => {
    if (refreshRef.current) {
      refreshRef.current.classList.add('spinning');
      setTimeout(() => refreshRef.current?.classList.remove('spinning'), 500);
    }
    await onRefresh();
  };

  return (
    <div>
      <div className="tab-header">
        <div>
          <h2>Resource Map</h2>
          <p className="subtitle">Resources pinned to workers. Each resource is owned by exactly one worker.</p>
        </div>
        <div className="tab-actions">
          <button ref={refreshRef} className="btn-refresh" onClick={handleRefresh} title="Refresh">&#x21bb;</button>
        </div>
      </div>

      <div className="filters-bar">
        <input
          type="text"
          value={filter}
          onChange={(e) => { setFilter(e.target.value); setPage(1); }}
          placeholder="Search resources..."
          style={{ minWidth: 200 }}
          title="Filter by resource path"
        />
        <select
          value={workerFilter}
          onChange={(e) => { setWorkerFilter(e.target.value); setPage(1); }}
          title="Filter by worker"
        >
          <option value="">All Workers</option>
          {workers.map(w => {
            const guid = workerGuid(w);
            return (
              <option key={guid} value={guid}>
                {workerDisplayName(w)} ({guid.substring(0, 8)}...)
              </option>
            );
          })}
        </select>
      </div>

      <div className="pagination-bar">
        <span className="page-info">
          <span className="record-count">{filtered.length}</span> resource(s)
        </span>
        <div className="page-controls">
          <button onClick={() => setPage(1)} disabled={page <= 1}>&laquo;</button>
          <button onClick={() => setPage(p => p - 1)} disabled={page <= 1}>&lsaquo;</button>
          <span className="page-info">Page {page} / {totalPages}</span>
          <button onClick={() => setPage(p => p + 1)} disabled={page >= totalPages}>&rsaquo;</button>
          <button onClick={() => setPage(totalPages)} disabled={page >= totalPages}>&raquo;</button>
          <select value={pageSize} onChange={(e) => { setPageSize(Number(e.target.value)); setPage(1); }} title="Rows per page">
            <option value={10}>10</option>
            <option value={25}>25</option>
            <option value={50}>50</option>
            <option value={100}>100</option>
          </select>
        </div>
      </div>

      <div className="table-container">
        <table>
          <thead>
            <tr>
              <th onClick={() => handleSort('resource')}>Resource Path <span className="sort-icon">{sortIcon('resource')}</span></th>
              <th onClick={() => handleSort('worker')}>Worker GUID <span className="sort-icon">{sortIcon('worker')}</span></th>
              <th onClick={() => handleSort('ip')}>Worker Address <span className="sort-icon">{sortIcon('ip')}</span></th>
              <th>Health</th>
            </tr>
          </thead>
          <tbody>
            {paginated.map((r, i) => (
              <tr key={r.resource + r.workerGuid + i}>
                <td>
                  <CopyableId value={r.resource} />
                </td>
                <td>
                  <CopyableId value={r.workerGuid} className="id-cell" />
                </td>
                <td><span className="mono">{r.workerIp}:{r.workerPort}</span></td>
                <td>
                  {r.workerHealthy !== null ? (
                    <>
                      <span className={`health-dot ${r.workerHealthy ? 'healthy' : 'unhealthy'}`}></span>
                      {r.workerHealthy ? 'Healthy' : 'Unhealthy'}
                    </>
                  ) : (
                    <span className="text-dim">Unknown</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {filtered.length === 0 && (
        <div className="empty-state">
          <div className="empty-icon">&#x2726;</div>
          <p>{filter || workerFilter ? 'No resources match your filters.' : 'No resources mapped yet.'}</p>
          <p className="empty-hint text-dim">Resources are automatically pinned to workers when requests arrive.</p>
        </div>
      )}
    </div>
  );
};

export default ResourcesTab;
