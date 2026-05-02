import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../lib/apiClient';
import type { Journey, PagedResult, PaginationParams } from '../types';

const JOURNEYS_KEY = 'journeys' as const;

export function useJourneys(params: PaginationParams) {
  return useQuery({
    queryKey: [JOURNEYS_KEY, params],
    queryFn: async () => {
      const response = await apiClient.get<PagedResult<Journey>>('/marketing/journeys', { params });
      return response.data;
    },
  });
}

export function useJourney(id: string) {
  return useQuery({
    queryKey: [JOURNEYS_KEY, id],
    queryFn: async () => {
      const response = await apiClient.get<Journey>(`/marketing/journeys/${id}`);
      return response.data;
    },
    enabled: Boolean(id),
  });
}

export function useDeleteJourney() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete(`/marketing/journeys/${id}`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [JOURNEYS_KEY] });
    },
  });
}
