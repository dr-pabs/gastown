import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../lib/apiClient';
import type {
  Opportunity,
  CreateOpportunityRequest,
  UpdateOpportunityRequest,
  PagedResult,
  PaginationParams,
} from '../types';

const OPPORTUNITIES_KEY = 'opportunities' as const;

export function useOpportunities(params: PaginationParams) {
  return useQuery({
    queryKey: [OPPORTUNITIES_KEY, params],
    queryFn: async () => {
      const response = await apiClient.get<PagedResult<Opportunity>>('/sfa/opportunities', {
        params,
      });
      return response.data;
    },
  });
}

export function useOpportunity(id: string) {
  return useQuery({
    queryKey: [OPPORTUNITIES_KEY, id],
    queryFn: async () => {
      const response = await apiClient.get<Opportunity>(`/sfa/opportunities/${id}`);
      return response.data;
    },
    enabled: Boolean(id),
  });
}

export function useCreateOpportunity() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateOpportunityRequest) => {
      const response = await apiClient.post<Opportunity>('/sfa/opportunities', data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [OPPORTUNITIES_KEY] });
    },
  });
}

export function useUpdateOpportunity(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: UpdateOpportunityRequest) => {
      const response = await apiClient.patch<Opportunity>(`/sfa/opportunities/${id}`, data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [OPPORTUNITIES_KEY] });
    },
  });
}

export function useDeleteOpportunity() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete(`/sfa/opportunities/${id}`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [OPPORTUNITIES_KEY] });
    },
  });
}
