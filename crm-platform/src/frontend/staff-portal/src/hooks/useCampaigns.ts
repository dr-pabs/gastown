import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../lib/apiClient';
import type { Campaign, PagedResult, PaginationParams } from '../types';

const CAMPAIGNS_KEY = 'campaigns' as const;

export function useCampaigns(params: PaginationParams) {
  return useQuery({
    queryKey: [CAMPAIGNS_KEY, params],
    queryFn: async () => {
      const response = await apiClient.get<PagedResult<Campaign>>('/marketing/campaigns', {
        params,
      });
      return response.data;
    },
  });
}

export function useCampaign(id: string) {
  return useQuery({
    queryKey: [CAMPAIGNS_KEY, id],
    queryFn: async () => {
      const response = await apiClient.get<Campaign>(`/marketing/campaigns/${id}`);
      return response.data;
    },
    enabled: Boolean(id),
  });
}

export function useDeleteCampaign() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete(`/marketing/campaigns/${id}`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [CAMPAIGNS_KEY] });
    },
  });
}
