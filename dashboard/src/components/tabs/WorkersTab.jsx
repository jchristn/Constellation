import { useState, useRef, useMemo, useCallback } from 'react';
import { workerGuid, workerDisplayName, timeAgo, formatDate } from '../../utils/helpers';
import CopyableId from '../CopyableId';
import './TabStyles.css';

const WorkersTab = ({ workers, resourceMap, loading, onRefresh }) => {
  const [filter, setFilter] = useState('');
  const [sort, setSort] = useState({ column: null, asc: true });
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [selectedWorker, setSelectedWorker] = useState(null);
  const [jsonModal, setJsonModal] = useState(null);
  const refreshRef = useRef(null);

  const getWorkerResources = useCallback((guid) => {
    const resources = resourceMap[guid];
    return Array.isArray(resources) ? resources : [];
  }, [resourceMap]);

  const filtered = useMemo(() => {
    let list = [...workers];
    if (filter) {
      const q = filter.toLowerCase();
      list = list.filter(w => {
        const guid = workerGuid(w).toLowerCase();
        const ip = (w.Ip || w.IP || '').toLowerCase();
        const port = String(w.Port || '');
        const health = w.Healthy ? 'healthy' : 'unhealthy';
        return guid.includes(q) || ip.includes(q) || port.includes(q) || health.includes(q);
      });
    }
    if (sort.column) {
      list.sort((a, b) => {
        let va, vb;
        switch (sort.column) {
          case 'guid': va = workerGuid(a).toLowerCase(); vb = workerGuid(b).toLowerCase(); break;
          case 'ip': va = (a.Ip || a.IP || '').toLowerCase(); vb = (b.Ip || b.IP || '').toLowerCase(); break;
          case 'port': va = a.Port || 0; vb = b.Port || 0; break;
          case 'healthy': va = a.Healthy ? 1 : 0; vb = b.Healthy ? 1 : 0; break;
          case 'resources': va = getWorkerResources(workerGuid(a)).length; vb = getWorkerResources(workerGuid(b)).length; break;
          case 'added': va = a.AddedUtc || ''; vb = b.AddedUtc || ''; break;
          case 'lastMessage': va = a.LastMessageUtc || ''; vb = b.LastMessageUtc || ''; break;
          default: va = ''; vb = '';
        }
        if (va < vb) return sort.asc ? -1 : 1;
        if (va > vb) return sort.asc ? 1 : -1;
        return 0;
      });
    }
    return list;
  }, [workers, filter, sort, getWorkerResources]);

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

  if (selectedWorker) {
    const guid = workerGuid(selectedWorker);
    const resources = getWorkerResources(guid);
    return (
      <div>
        <button className="btn-back" onClick={() => setSelectedWorker(null)}>&larr; Back to Workers</button>

        <div className="tab-header">
          <div>
            <h2>Worker Detail</h2>
            <p className="subtitle">{workerDisplayName(selectedWorker)}</p>
          </div>
          <div className="tab-actions">
            <button className="btn-view-json" onClick={() => setJsonModal({ title: 'Worker', data: selectedWorker })}>View JSON</button>
          </div>
        </div>

        <div className="detail-grid">
          <div className="detail-field">
            <span className="detail-label">GUID</span>
            <span className="detail-value"><CopyableId value={guid} /></span>
          </div>
          <div className="detail-field">
            <span className="detail-label">Health</span>
            <span className="detail-value">
              <span className={`health-dot ${selectedWorker.Healthy ? 'healthy' : 'unhealthy'}`}></span>
              {selectedWorker.Healthy ? 'Healthy' : 'Unhealthy'}
            </span>
          </div>
          <div className="detail-field">
            <span className="detail-label">IP Address</span>
            <span className="detail-value mono">{selectedWorker.Ip || selectedWorker.IP || '-'}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label">Port</span>
            <span className="detail-value mono">{selectedWorker.Port || '-'}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label">Connected</span>
            <span className="detail-value">{formatDate(selectedWorker.AddedUtc)}</span>
          </div>
          <div className="detail-field">
            <span className="detail-label">Last Heartbeat</span>
            <span className="detail-value">
              {formatDate(selectedWorker.LastMessageUtc)}
              <span className="text-dim"> ({timeAgo(selectedWorker.LastMessageUtc)})</span>
            </span>
          </div>
        </div>

        <h3 style={{ fontSize: '1.1rem', fontWeight: 600, marginBottom: 8, color: 'var(--text-heading)' }}>
          Pinned Resources ({resources.length})
        </h3>
        {resources.length > 0 ? (
          <div className="table-container">
            <ul className="resource-list">
              {resources.map(res => (
                <li key={res}>
                  <span className="resource-path">{res}</span>
                  <CopyableId value={res} className="" />
                </li>
              ))}
            </ul>
          </div>
        ) : (
          <p className="text-dim" style={{ fontSize: '0.85rem', padding: '12px 0' }}>No resources pinned to this worker.</p>
        )}

        {jsonModal && (
          <div className="modal-overlay" onClick={() => setJsonModal(null)}>
            <div className="modal-box" onClick={e => e.stopPropagation()}>
              <h3>{jsonModal.title}</h3>
              <pre className="json-viewer">{JSON.stringify(jsonModal.data, null, 2)}</pre>
              <div className="modal-actions">
                <button onClick={() => { navigator.clipboard.writeText(JSON.stringify(jsonModal.data, null, 2)); }}>Copy</button>
                <button onClick={() => setJsonModal(null)}>Close</button>
              </div>
            </div>
          </div>
        )}
      </div>
    );
  }

  return (
    <div>
      <div className="tab-header">
        <div>
          <h2>Workers</h2>
          <p className="subtitle">Connected workers with health status and resource assignments.</p>
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
          placeholder="Search workers..."
          style={{ minWidth: 200 }}
          title="Filter by GUID, IP, port, or health status"
        />
      </div>

      <div className="pagination-bar">
        <span className="page-info">
          <span className="record-count">{filtered.length}</span> worker(s)
        </span>
        <div className="page-controls">
          <button onClick={() => setPage(1)} disabled={page <= 1} title="First page">&laquo;</button>
          <button onClick={() => setPage(p => p - 1)} disabled={page <= 1} title="Previous">&lsaquo;</button>
          <span className="page-info">Page {page} / {totalPages}</span>
          <button onClick={() => setPage(p => p + 1)} disabled={page >= totalPages} title="Next">&rsaquo;</button>
          <button onClick={() => setPage(totalPages)} disabled={page >= totalPages} title="Last page">&raquo;</button>
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
              <th onClick={() => handleSort('guid')}>GUID <span className="sort-icon">{sortIcon('guid')}</span></th>
              <th onClick={() => handleSort('ip')}>IP <span className="sort-icon">{sortIcon('ip')}</span></th>
              <th onClick={() => handleSort('port')}>Port <span className="sort-icon">{sortIcon('port')}</span></th>
              <th onClick={() => handleSort('healthy')}>Health <span className="sort-icon">{sortIcon('healthy')}</span></th>
              <th onClick={() => handleSort('resources')}>Resources <span className="sort-icon">{sortIcon('resources')}</span></th>
              <th onClick={() => handleSort('added')}>Connected <span className="sort-icon">{sortIcon('added')}</span></th>
              <th onClick={() => handleSort('lastMessage')}>Last Heartbeat <span className="sort-icon">{sortIcon('lastMessage')}</span></th>
              <th style={{ width: 60 }}></th>
            </tr>
          </thead>
          <tbody>
            {paginated.map(w => {
              const guid = workerGuid(w);
              return (
                <tr key={guid}>
                  <td><CopyableId value={guid} /></td>
                  <td><span className="mono">{w.Ip || w.IP || '-'}</span></td>
                  <td><span className="mono">{w.Port || '-'}</span></td>
                  <td>
                    <span className={`health-dot ${w.Healthy ? 'healthy' : 'unhealthy'}`}></span>
                    {w.Healthy ? 'Healthy' : 'Unhealthy'}
                  </td>
                  <td>{getWorkerResources(guid).length}</td>
                  <td><span title={formatDate(w.AddedUtc)}>{timeAgo(w.AddedUtc)}</span></td>
                  <td><span title={formatDate(w.LastMessageUtc)}>{timeAgo(w.LastMessageUtc)}</span></td>
                  <td>
                    <button
                      style={{ background: 'none', border: 'none', cursor: 'pointer', padding: '4px 8px', color: 'var(--text-muted)', borderRadius: 4, fontSize: '1.1rem' }}
                      onClick={() => setSelectedWorker({ ...w })}
                      title="View details"
                    >&#x22EE;</button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {filtered.length === 0 && (
        <div className="empty-state">
          <div className="empty-icon">&#x2726;</div>
          <p>{filter ? 'No workers match your search.' : 'No workers connected.'}</p>
        </div>
      )}
    </div>
  );
};

export default WorkersTab;
