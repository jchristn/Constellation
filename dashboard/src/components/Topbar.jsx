import { useAuth } from '../context/AuthContext';
import { useNavigate } from 'react-router-dom';
import './Topbar.css';

const Topbar = ({ activeTab, onTabChange }) => {
  const { theme, toggleTheme, logout, serverUrl } = useAuth();
  const navigate = useNavigate();

  const handleLogout = () => {
    logout();
    navigate('/');
  };

  const tabs = [
    { key: 'home', label: 'Dashboard', title: 'System overview with worker health and resource counts' },
    { key: 'workers', label: 'Workers', title: 'Connected workers with health status and resource assignments' },
    { key: 'resources', label: 'Resources', title: 'Resource-to-worker pinning map' },
  ];

  return (
    <div className="topbar">
      <div className="topbar-left">
        <img src="/logo.png" alt="Constellation" className="topbar-logo" />
        <h1 className="topbar-title">Constellation</h1>
      </div>

      <nav className="topbar-nav">
        {tabs.map(tab => (
          <button
            key={tab.key}
            className={activeTab === tab.key ? 'active' : ''}
            onClick={() => onTabChange(tab.key)}
            title={tab.title}
          >
            {tab.label}
          </button>
        ))}
      </nav>

      <div className="topbar-right">
        {serverUrl && (
          <div className="topbar-server-url" title={serverUrl}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <rect x="2" y="2" width="20" height="8" rx="2" ry="2"></rect>
              <rect x="2" y="14" width="20" height="8" rx="2" ry="2"></rect>
              <line x1="6" y1="6" x2="6.01" y2="6"></line>
              <line x1="6" y1="18" x2="6.01" y2="18"></line>
            </svg>
            <span className="server-url-text">{serverUrl}</span>
          </div>
        )}
        <button className="topbar-button" onClick={toggleTheme} title="Toggle Theme">
          {theme === 'light' ? '\uD83C\uDF19' : '\u2600\uFE0F'}
        </button>
        <a href="https://github.com/jchristn/constellation" target="_blank" rel="noopener noreferrer" className="topbar-github" title="View on GitHub">
          <svg height="20" width="20" viewBox="0 0 16 16" fill="currentColor"><path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27s1.36.09 2 .27c1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0 0 16 8c0-4.42-3.58-8-8-8z"/></svg>
        </a>
        <button className="topbar-button" onClick={handleLogout} title="Logout">
          Logout
        </button>
      </div>
    </div>
  );
};

export default Topbar;
