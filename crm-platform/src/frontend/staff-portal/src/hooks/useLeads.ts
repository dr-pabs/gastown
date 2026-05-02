import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../lib/apiClient';
import type {
  Lead,
  CreateLeadRequest,
  UpdateLeadRequest,
  PagedResult,
  PaginationParams,
} from '../types';

const LEADS_KEY = 'leads' as const;

export function useLeads(params: PaginationParams) {
  return useQuery({
    queryKey: [LEADS_KEY, params],
    queryFn: async () => {
      const response = await apiClient.get<PagedResult<Lead>>('/sfa/leads', {
        params,
      });
      return response.data;
    },
  });
}

export function useLead(id: string) {
  return useQuery({
    queryKey: [LEADS_KEY, id],
    queryFn: async () => {
      const response = await apiClient.get<Lead>(`/sfa/leads/${id}`);
      return response.data;
    },
    enabled: Boolean(id),
  });
}

export function useCreateLead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateLeadRequest) => {
      const response = await apiClient.post<Lead>('/sfa/leads', data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [LEADS_KEY] });
    },
  });
}

export function useUpdateLead(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: UpdateLeadRequest) => {
      const response = await apiClient.patch<Lead>(`/sfa/leads/${id}`, data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [LEADS_KEY] });
    },
  });
}

export function useDeleteLead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete(`/sfa/leads/${id}`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [LEADS_KEY] });
    },
  });
}
