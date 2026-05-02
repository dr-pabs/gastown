import { Link, useLocation } from 'react-router-dom';
import { useMsal } from '@azure/msal-react';
import { useTranslation } from 'react-i18next';
import { PORTAL_SUPERUSER_ROLE } from '../../lib/authConfig';

function isSuperuser(accounts: ReturnType<typeof useMsal>['accounts']): boolean {
  if (accounts.length === 0) return false;
  const roles = (accounts[0].idTokenClaims as Record<string, unknown>)?.['roles'];
  return Array.isArray(roles) && roles.includes(PORTAL_SUPERUSER_ROLE);
}

export function NavBar() {
  const { t } = useTranslation();
  const { accounts, instance } = useMsal();
  const location = useLocation();
  const superuser = isSuperuser(accounts);

  const navLinks = [
    { to: '/cases/my', label: t('nav.myCases') },
    ...(superuser ? [{ to: '/cases/all', label: t('nav.allCases') }] : []),
    { to: '/cases/new', label: t('nav.newCase') },
  ];

  return (
    <header className="border-b border-gray-200 bg-white">
      <div className="mx-auto flex max-w-5xl items-center justify-between px-4 py-3">
        <nav className="flex items-center gap-1">
          <span className="mr-4 text-sm font-bold text-blue-700">CRM Support</span>
          {navLinks.map((link) => (
            <Link
              key={link.to}
              to={link.to}
              className={`rounded-md px-3 py-2 text-sm font-medium transition-colors ${
                location.pathname.startsWith(link.to)
                  ? 'bg-blue-50 text-blue-700'
                  : 'text-gray-600 hover:text-gray-900 hover:bg-gray-100'
              }`}
            >
              {link.label}
            </Link>
          ))}
        </nav>
        <div className="flex items-center gap-3">
          <span className="text-sm text-gray-500">{accounts[0]?.username}</span>
          <button
            onClick={() => { void instance.logoutRedirect(); }}
            className="text-sm text-gray-600 hover:text-gray-900"
          >
            {t('auth.signOut')}
          </button>
        </div>
      </div>
    </header>
  );
}
