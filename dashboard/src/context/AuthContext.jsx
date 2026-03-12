import { createContext, useContext, useState, useEffect } from 'react';
import ApiClient from '../utils/api';

const AuthContext = createContext(null);

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};

export const AuthProvider = ({ children }) => {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [apiClient, setApiClient] = useState(null);
  const [serverUrl, setServerUrl] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [theme, setTheme] = useState('light');

  useEffect(() => {
    const savedUrl = localStorage.getItem('constellation_server_url');
    const savedKey = localStorage.getItem('constellation_api_key');
    const savedTheme = localStorage.getItem('constellation_theme');

    if (savedUrl && savedKey) {
      setServerUrl(savedUrl);
      setApiKey(savedKey);
      const client = new ApiClient(savedUrl, savedKey);
      setApiClient(client);
      setIsAuthenticated(true);
    }

    if (savedTheme) {
      setTheme(savedTheme);
      document.body.setAttribute('data-theme', savedTheme);
    }
  }, []);

  const login = async (url, key) => {
    const client = new ApiClient(url, key);
    const isConnected = await client.testConnection();
    if (!isConnected) {
      throw new Error('Failed to connect to controller');
    }

    localStorage.setItem('constellation_server_url', url);
    localStorage.setItem('constellation_api_key', key);

    setServerUrl(url);
    setApiKey(key);
    setApiClient(client);
    setIsAuthenticated(true);
    return true;
  };

  const logout = () => {
    localStorage.removeItem('constellation_server_url');
    localStorage.removeItem('constellation_api_key');
    setServerUrl('');
    setApiKey('');
    setApiClient(null);
    setIsAuthenticated(false);
  };

  const toggleTheme = () => {
    const newTheme = theme === 'light' ? 'dark' : 'light';
    setTheme(newTheme);
    localStorage.setItem('constellation_theme', newTheme);
    document.body.setAttribute('data-theme', newTheme);
  };

  const value = {
    isAuthenticated,
    apiClient,
    serverUrl,
    apiKey,
    theme,
    login,
    logout,
    toggleTheme,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};
