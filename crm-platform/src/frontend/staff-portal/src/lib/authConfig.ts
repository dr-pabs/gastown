import { Configuration, LogLevel } from '@azure/msal-browser';

const clientId = import.meta.env.VITE_AZURE_CLIENT_ID;
const tenantId = import.meta.env.VITE_AZURE_TENANT_ID;
const authority = `${import.meta.env.VITE_AZURE_AUTHORITY}${tenantId}`;

if (!clientId || !tenantId) {
  throw new Error('VITE_AZURE_CLIENT_ID and VITE_AZURE_TENANT_ID must be set in environment');
}

export const msalConfig: Configuration = {
  auth: {
    clientId,
    authority,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: {
    cacheLocation: 'sessionStorage', // sessionStorage — never localStorage
    storeAuthStateInCookie: false,
  },
  system: {
    loggerOptions: {
      loggerCallback: (level: LogLevel, message: string, containsPii: boolean) => {
        if (containsPii) return; // never log PII
        if (import.meta.env.DEV) {
          switch (level) {
            case LogLevel.Error:
              console.error(message);
              break;
            case LogLevel.Warning:
              console.warn(message);
              break;
            case LogLevel.Info:
              console.info(message);
              break;
            default:
              break;
          }
        }
      },
      piiLoggingEnabled: false,
    },
  },
};

/** Scopes required to call the CRM API. */
export const apiScopes: string[] = [`api://${clientId}/crm.access`];

/** Login request — used for interactive login. */
export const loginRequest = {
  scopes: apiScopes,
};
