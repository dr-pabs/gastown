import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../lib/apiClient';
import type { Notification, PagedResult, PaginationParams } from '../types';

const NOTIFICATIONS_KEY = 'notifications' as const;

export function useNotifications(params: PaginationParams) {
  return useQuery({
    queryKey: [NOTIFICATIONS_KEY, params],
    queryFn: async () => {
      const response = await apiClient.get<PagedResult<Notification>>('/notifications', { params });
      return response.data;
    },
  });
}

export function useUnreadNotificationCount() {
  return useQuery({
    queryKey: [NOTIFICATIONS_KEY, 'unread-count'],
    queryFn: async () => {
      const response = await apiClient.get<{ count: number }>('/notifications/unread-count');
      return response.data.count;
    },
    refetchInterval: 30_000, // poll every 30s
  });
}

export function useMarkNotificationRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.post(`/notifications/${id}/read`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [NOTIFICATIONS_KEY] });
    },
  });
}

export function useMarkAllNotificationsRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await apiClient.post('/notifications/mark-all-read');
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [NOTIFICATIONS_KEY] });
    },
  });
}
