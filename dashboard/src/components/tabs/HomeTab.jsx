import { useRef } from 'react';
import { useAuth } from '../../context/AuthContext';
import { workerGuid, workerDisplayName, timeAgo, formatDate } from '../../utils/helpers';
import CopyableId from '../CopyableId';
import './TabStyles.css';

const HomeTab = ({ workers, resourceList, serverHealthy, loading, onRefresh, onNavigate }) => {
  const { serverUrl } = useAuth();
  const refreshRef = useRef(null);

  const healthyCount = workers.filter(w => w.Healthy === true).length;
  const unhealthyCount = workers.filter(w => w.Healthy === false).length;

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
          <h2>System Status</h2>
          <p className="subtitle">Overview of controller health, connected workers, and resource assignments.</p>
        </div>
        <div className="tab-actions">
          <button ref={refreshRef} className="btn-refresh" onClick={handleRefresh} title="Refresh all data">&#x21bb;</button>
        </div>
      </div>

      <div className="cards-grid">
        <div className="stat-card">
          <div className="card-label">Controller</div>
          <div className="card-value small">
            <span className={`health-dot ${serverHealthy ? 'healthy' : 'unhealthy'}`}></span>
            {serverHealthy ? 'Healthy' : 'Unreachable'}
          </div>
          <div className="card-note">{serverUrl}</div>
        </div>

        <div className="stat-card clickable" onClick={() => onNavigate('workers')}>
          <div className="card-label">Workers</div>
          <div className="card-value">{workers.length}</div>
          <div className="card-detail">
            <span className="tag healthy">{healthyCount} healthy</span>
            {unhealthyCount > 0 && <span className="tag unhealthy">{unhealthyCount} unhealthy</span>}
          </div>
        </div>

        <div className="stat-card clickable" onClick={() => onNavigate('resources')}>
          <div className="card-label">Resources</div>
          <div className="card-value">{resourceList.length}</div>
          <div className="card-note">Pinned across {workers.length} worker(s)</div>
        </div>
      </div>

      <div className="settings-grid">
        <div className="settings-card">
          <h4>Connection</h4>
          <div className="settings-item">
            <span className="label">Controller URL</span>
            <span className="value">{serverUrl}</span>
          </div>
          <div className="settings-item">
            <span className="label">Status</span>
            <span>
              <span className={`health-dot ${serverHealthy ? 'healthy' : 'unhealthy'}`} style={{ marginRight: 4 }}></span>
              <span style={{ fontSize: '0.85rem' }}>{serverHealthy ? 'Connected' : 'Disconnected'}</span>
            </span>
          </div>
          <div className="settings-item">
            <span className="label">Auto-Refresh</span>
            <span className="value">Every 10s</span>
          </div>
        </div>

        <div className="settings-card">
          <h4>Worker Health</h4>
          <div className="settings-item">
            <span className="label">Total Workers</span>
            <span className="value">{workers.length}</span>
          </div>
          <div className="settings-item">
            <span className="label">Healthy</span>
            <span className="value" style={{ color: 'var(--green)' }}>{healthyCount}</span>
          </div>
          <div className="settings-item">
            <span className="label">Unhealthy</span>
            <span className="value" style={unhealthyCount > 0 ? { color: 'var(--red)' } : {}}>{unhealthyCount}</span>
          </div>
          <div className="settings-item">
            <span className="label">Resources Pinned</span>
            <span className="value">{resourceList.length}</span>
          </div>
        </div>
      </div>

      {workers.length > 0 && (
        <div>
          <h3 style={{ fontSize: '1.1rem', fontWeight: 600, marginBottom: 8, marginTop: 8, color: 'var(--text-heading)' }}>Workers</h3>
          <div className="table-container">
            <table>
              <thead>
                <tr>
                  <th>Worker</th>
                  <th>Health</th>
                  <th>Resources</th>
                  <th>Last Heartbeat</th>
                </tr>
              </thead>
              <tbody>
                {workers.slice(0, 10).map(w => {
                  const guid = workerGuid(w);
                  const resources = resourceList.filter(r => r.workerGuid === guid);
                  return (
                    <tr key={guid} className="clickable" onClick={() => onNavigate('workers')}>
                      <td>
                        <strong>{workerDisplayName(w)}</strong>
                        <div className="id-cell">
                          <CopyableId value={guid.substring(0, 12) + '...'} className="mono" />
                        </div>
                      </td>
                      <td>
                        <span className={`health-dot ${w.Healthy ? 'healthy' : 'unhealthy'}`}></span>
                        {w.Healthy ? 'Healthy' : 'Unhealthy'}
                      </td>
                      <td>{resources.length}</td>
                      <td>
                        <span title={formatDate(w.LastMessageUtc)}>{timeAgo(w.LastMessageUtc)}</span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          {workers.length > 10 && (
            <p className="text-dim" style={{ fontSize: '0.85rem', marginTop: 8 }}>
              Showing 10 of {workers.length} workers.{' '}
              <a href="#" onClick={(e) => { e.preventDefault(); onNavigate('workers'); }}>View all</a>
            </p>
          )}
        </div>
      )}

      {workers.length === 0 && !loading && (
        <div className="empty-state">
          <div className="empty-icon">&#x2726;</div>
          <p>No workers connected.</p>
          <p className="empty-hint text-dim">Workers connect to the controller via WebSocket on the configured port.</p>
        </div>
      )}

      <h3 style={{ fontSize: '1.1rem', fontWeight: 600, marginBottom: 4, marginTop: 24, color: 'var(--text-heading)' }}>About</h3>
      <div className="about-card">
        <p>
          <strong>Constellation</strong> is a RESTful workload placement and virtualization system
          designed for exactly-one resource ownership patterns. The controller routes HTTP requests to workers based on
          resource pinning &mdash; the raw URL path becomes the resource key, ensuring all requests to the same URL are routed
          to the same worker for exclusive resource ownership.
        </p>
        <p>
          Workers connect via WebSocket and are monitored with periodic heartbeats. When a worker fails, its resources
          are automatically reassigned to healthy workers using round-robin distribution.
        </p>
      </div>
    </div>
  );
};

export default HomeTab;
