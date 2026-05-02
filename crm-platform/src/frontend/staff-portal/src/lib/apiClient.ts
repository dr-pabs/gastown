import axios, { type InternalAxiosRequestConfig, type AxiosError } from 'axios';
import { PublicClientApplication } from '@azure/msal-browser';
import { msalConfig, apiScopes } from './authConfig';

const msalInstance = new PublicClientApplication(msalConfig);

const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL,
  timeout: 30_000,
  headers: {
    'Content-Type': 'application/json',
  },
});

/** Attach Bearer token and X-Tenant-Id header to every outbound request. */
apiClient.interceptors.request.use(async (config: InternalAxiosRequestConfig) => {
  const accounts = msalInstance.getAllAccounts();
  if (accounts.length === 0) {
    return config;
  }

  const account = accounts[0];

  // acquireTokenSilent — never store tokens ourselves; MSAL manages token storage
  const tokenResponse = await msalInstance.acquireTokenSilent({
    scopes: apiScopes,
    account,
  });

  config.headers['Authorization'] = `Bearer ${tokenResponse.accessToken}`;

  // Resolve tenant ID from the `tid` claim in the ID token claims
  const tid = (account.idTokenClaims as Record<string, unknown> | undefined)?.['tid'];
  if (typeof tid === 'string') {
    config.headers['X-Tenant-Id'] = tid;
  }

  return config;
});

/** Translate 4xx/5xx into structured errors the UI can handle. */
apiClient.interceptors.response.use(
  (response) => response,
  (error: AxiosError<ApiProblemDetails>) => {
    const status = error.response?.status;

    if (status === 401) {
      // Force re-login if token is rejected
      void msalInstance.loginRedirect({ scopes: apiScopes });
    }

    return Promise.reject(error);
  },
);

export interface ApiProblemDetails {
  type: string;
  title: string;
  status: number;
  detail: string;
  traceId: string;
}

export default apiClient;
