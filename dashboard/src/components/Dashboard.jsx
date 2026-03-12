import { useState, useEffect, useCallback, useRef } from 'react';
import { useAuth } from '../context/AuthContext';
import Topbar from './Topbar';
import HomeTab from './tabs/HomeTab';
import WorkersTab from './tabs/WorkersTab';
import ResourcesTab from './tabs/ResourcesTab';
import { buildResourceList } from '../utils/helpers';
import './Dashboard.css';

const Dashboard = () => {
  const { apiClient } = useAuth();
  const [activeTab, setActiveTab] = useState('home');
  const [workers, setWorkers] = useState([]);
  const [resourceMap, setResourceMap] = useState({});
  const [resourceList, setResourceList] = useState([]);
  const [serverHealthy, setServerHealthy] = useState(false);
  const [loading, setLoading] = useState(true);
  const pollRef = useRef(null);

  const refreshAll = useCallback(async () => {
    if (!apiClient) return;
    try {
      const [healthy, workerData, mapData] = await Promise.all([
        apiClient.checkHealth(),
        apiClient.getWorkers(),
        apiClient.getResourceMap(),
      ]);
      setServerHealthy(healthy);
      setWorkers(workerData);
      setResourceMap(mapData);
      setResourceList(buildResourceList(workerData, mapData));
    } catch (err) {
      console.error('Refresh failed:', err);
      setServerHealthy(false);
    } finally {
      setLoading(false);
    }
  }, [apiClient]);

  useEffect(() => {
    refreshAll();
    pollRef.current = setInterval(refreshAll, 10000);
    return () => {
      if (pollRef.current) clearInterval(pollRef.current);
    };
  }, [refreshAll]);

  const handleTabChange = (tab) => {
    setActiveTab(tab);
  };

  const renderTab = () => {
    switch (activeTab) {
      case 'home':
        return (
          <HomeTab
            workers={workers}
            resourceList={resourceList}
            resourceMap={resourceMap}
            serverHealthy={serverHealthy}
            loading={loading}
            onRefresh={refreshAll}
            onNavigate={handleTabChange}
          />
        );
      case 'workers':
        return (
          <WorkersTab
            workers={workers}
            resourceMap={resourceMap}
            loading={loading}
            onRefresh={refreshAll}
          />
        );
      case 'resources':
        return (
          <ResourcesTab
            workers={workers}
            resourceList={resourceList}
            loading={loading}
            onRefresh={refreshAll}
          />
        );
      default:
        return null;
    }
  };

  return (
    <div className="dashboard">
      <Topbar activeTab={activeTab} onTabChange={handleTabChange} />
      <div className="dashboard-content">
        {renderTab()}
      </div>
    </div>
  );
};

export default Dashboard;
