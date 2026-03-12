export default class ApiClient {
  constructor(serverUrl, apiKey) {
    this.serverUrl = serverUrl.replace(/\/+$/, '');
    this.apiKey = apiKey;
  }

  async testConnection() {
    try {
      const resp = await fetch(this.serverUrl + '/', { method: 'HEAD' });
      return resp.ok;
    } catch {
      return false;
    }
  }

  async request(method, path, body) {
    const opts = {
      method,
      headers: {
        'x-api-key': this.apiKey,
        'Content-Type': 'application/json',
      },
    };
    if (body) opts.body = JSON.stringify(body);

    const resp = await fetch(this.serverUrl + path, opts);
    if (resp.status === 401 || resp.status === 403) {
      throw new Error('Authentication failed');
    }

    const text = await resp.text();
    if (!text) return resp.ok ? {} : null;

    try {
      return JSON.parse(text);
    } catch {
      return text;
    }
  }

  async getWorkers() {
    const data = await this.request('GET', '/workers');
    if (data && Array.isArray(data)) return data;
    if (data && typeof data === 'object') return Object.values(data);
    return [];
  }

  async getResourceMap() {
    const data = await this.request('GET', '/maps');
    if (data && typeof data === 'object') return data;
    return {};
  }

  async checkHealth() {
    try {
      const resp = await fetch(this.serverUrl + '/', { method: 'HEAD' });
      return resp.ok;
    } catch {
      return false;
    }
  }
}
