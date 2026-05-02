import axios from 'axios';
import { PublicClientApplication } from '@azure/msal-browser';
import { loginRequest } from './authConfig';

const apiClient = axios.create({
  baseURL: import.meta.env['VITE_API_BASE_URL'] as string ?? '/api',
  headers: { 'Content-Type': 'application/json' },
});

// Lazy-initialised MSAL instance (shared with MsalProvider in main.tsx)
let _pca: PublicClientApplication | null = null;
export function setPca(pca: PublicClientApplication) {
  _pca = pca;
}

apiClient.interceptors.request.use(async (config) => {
  if (_pca) {
    const accounts = _pca.getAllAccounts();
    if (accounts.length > 0) {
      try {
        const tokenResponse = await _pca.acquireTokenSilent({
          ...loginRequest,
          account: accounts[0],
        });
        config.headers['Authorization'] = `Bearer ${tokenResponse.accessToken}`;
        // Tenant ID from the ID token claims
        const tenantId = (accounts[0].idTokenClaims as Record<string, string>)?.['tid'];
        if (tenantId) {
          config.headers['X-Tenant-Id'] = tenantId;
        }
      } catch {
        // Silent acquisition failed — MSAL will handle redirect
      }
    }
  }
  return config;
});

export default apiClient;
