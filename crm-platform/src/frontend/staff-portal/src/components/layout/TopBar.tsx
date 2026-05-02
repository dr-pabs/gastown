import { useTranslation } from 'react-i18next';
import { useMsal } from '@azure/msal-react';
import { useUiStore } from '../../stores/uiStore';
import { useUnreadNotificationCount } from '../../hooks/useNotifications';
import { Link } from 'react-router-dom';

function MenuIcon() {
  return (
    <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" />
    </svg>
  );
}

function BellIcon({ count }: { count?: number }) {
  return (
    <span className="relative">
      <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
          d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
      </svg>
      {count !== undefined && count > 0 && (
        <span className="absolute -right-1 -top-1 flex h-4 w-4 items-center justify-center rounded-full bg-danger-500 text-[10px] font-bold text-white">
          {count > 99 ? '99+' : count}
        </span>
      )}
    </span>
  );
}

export function TopBar() {
  const { t } = useTranslation();
  const toggleSidebar = useUiStore((s) => s.toggleSidebar);
  const { accounts, instance } = useMsal();
  const { data: unreadCount } = useUnreadNotificationCount();

  const displayName = accounts[0]?.name ?? accounts[0]?.username ?? '';

  const handleSignOut = () => {
    void instance.logoutRedirect();
  };

  return (
    <header className="flex h-16 items-center justify-between border-b bg-white px-4 shadow-sm">
      {/* Left: toggle sidebar */}
      <button
        onClick={toggleSidebar}
        className="rounded p-2 text-gray-500 hover:bg-gray-100"
        aria-label="Toggle sidebar"
      >
        <MenuIcon />
      </button>

      {/* Right: notifications + user */}
      <div className="flex items-center gap-4">
        <Link
          to="/notifications"
          className="rounded p-2 text-gray-500 hover:bg-gray-100"
          aria-label={t('nav.notifications')}
        >
          <BellIcon count={unreadCount} />
        </Link>

        {/* User menu (simplified — no dropdown for now) */}
        <div className="flex items-center gap-2">
          <div className="flex h-8 w-8 items-center justify-center rounded-full bg-primary-600 text-sm font-semibold text-white">
            {displayName.charAt(0).toUpperCase()}
          </div>
          <span className="hidden text-sm font-medium text-gray-700 sm:block">{displayName}</span>
          <button
            onClick={handleSignOut}
            className="rounded px-2 py-1 text-xs text-gray-500 hover:bg-gray-100"
          >
            {t('auth.signOut')}
          </button>
        </div>
      </div>
    </header>
  );
}
