import { useMsal } from '@azure/msal-react';
import { PORTAL_SUPERUSER_ROLE } from '../lib/authConfig';

export function useIsSuperuser(): boolean {
  const { accounts } = useMsal();
  if (accounts.length === 0) return false;
  const roles = (accounts[0].idTokenClaims as Record<string, unknown>)?.['roles'];
  return Array.isArray(roles) && roles.includes(PORTAL_SUPERUSER_ROLE);
}
