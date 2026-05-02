import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNotifications, useMarkNotificationRead, useMarkAllNotificationsRead } from '../hooks/useNotifications';
import { Button, Badge, Spinner } from '../components/ui';
import type { Notification, NotificationChannel, PaginationParams } from '../types';

// NotificationChannel = 'InApp' | 'Email' | 'Sms' | 'Teams'
const channelVariant: Record<NotificationChannel, 'default' | 'info' | 'success' | 'warning' | 'danger'> = {
  InApp: 'default',
  Email: 'info',
  Sms: 'warning',
  Teams: 'success',
};

export function Notifications() {
  const { t } = useTranslation();
  const [params] = useState<PaginationParams>({ page: 1, pageSize: 50 });

  const { data, isLoading } = useNotifications(params);
  const markRead = useMarkNotificationRead();
  const markAllRead = useMarkAllNotificationsRead();

  const notifications = data?.items ?? [];

  return (
    <div className="p-6 max-w-3xl mx-auto">
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">{t('notifications.title')}</h1>
        <Button
          variant="secondary"
          size="sm"
          onClick={() => { markAllRead.mutate(); }}
          loading={markAllRead.isPending}
        >{t('notifications.markAllRead')}</Button>
      </div>

      {isLoading ? (
        <div className="flex h-32 items-center justify-center">
          <Spinner size="lg" />
        </div>
      ) : (
        <div className="space-y-2">
          {notifications.map((notification: Notification) => (
            <div
              key={notification.id}
              className={`flex items-start gap-4 rounded-lg border p-4 ${
                notification.readAt ? 'border-gray-100 bg-white' : 'border-blue-100 bg-blue-50'
              }`}
            >
              <div className="flex-1 min-w-0">
                {notification.subject && (
                  <p className={`text-sm font-medium ${notification.readAt ? 'text-gray-700' : 'text-gray-900'}`}>
                    {notification.subject}
                  </p>
                )}
                <p className={`text-sm ${notification.readAt ? 'text-gray-500' : 'text-gray-700'}`}>
                  {notification.body}
                </p>
                <div className="mt-1 flex items-center gap-2 text-xs text-gray-400">
                  <Badge label={notification.channel} variant={channelVariant[notification.channel]} />
                  <span>{new Date(notification.createdAt).toLocaleString()}</span>
                </div>
              </div>
              {!notification.readAt && (
                <div className="flex flex-shrink-0 items-center gap-2">
                  <span className="h-2 w-2 rounded-full bg-blue-500" />
                  <button
                    onClick={() => { markRead.mutate(notification.id); }}
                    className="text-xs text-blue-600 hover:text-blue-800"
                  >
                    {t('notifications.markRead')}
                  </button>
                </div>
              )}
            </div>
          ))}

          {notifications.length === 0 && (
            <p className="py-8 text-center text-sm text-gray-400">{t('notifications.noNotifications')}</p>
          )}
        </div>
      )}
    </div>
  );
}
