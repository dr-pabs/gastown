import type { Configuration, RedirectRequest } from '@azure/msal-browser';

// Entra External ID (CIAM) — separate from staff portal's corporate tenant
export const msalConfig: Configuration = {
  auth: {
    clientId: import.meta.env['VITE_MSAL_CLIENT_ID'] as string,
    // CIAM authority format: https://<tenant>.ciamlogin.com/<tenant>.onmicrosoft.com
    authority: import.meta.env['VITE_MSAL_AUTHORITY'] as string,
    redirectUri: import.meta.env['VITE_MSAL_REDIRECT_URI'] as string,
    postLogoutRedirectUri: import.meta.env['VITE_MSAL_REDIRECT_URI'] as string,
  },
  cache: {
    cacheLocation: 'sessionStorage',
    storeAuthStateInCookie: false,
  },
};

export const loginRequest: RedirectRequest = {
  scopes: ['openid', 'profile', import.meta.env['VITE_API_SCOPE'] as string],
};

// Role claim that grants visibility into all company cases (portal superuser)
export const PORTAL_SUPERUSER_ROLE = 'portal.superuser';
