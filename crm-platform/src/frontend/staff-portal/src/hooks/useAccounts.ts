import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../lib/apiClient';
import type {
  Account,
  CreateAccountRequest,
  UpdateAccountRequest,
  PagedResult,
  PaginationParams,
} from '../types';

const ACCOUNTS_KEY = 'accounts' as const;

export function useAccounts(params: PaginationParams) {
  return useQuery({
    queryKey: [ACCOUNTS_KEY, params],
    queryFn: async () => {
      const response = await apiClient.get<PagedResult<Account>>('/sfa/accounts', { params });
      return response.data;
    },
  });
}

export function useAccount(id: string) {
  return useQuery({
    queryKey: [ACCOUNTS_KEY, id],
    queryFn: async () => {
      const response = await apiClient.get<Account>(`/sfa/accounts/${id}`);
      return response.data;
    },
    enabled: Boolean(id),
  });
}

export function useCreateAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateAccountRequest) => {
      const response = await apiClient.post<Account>('/sfa/accounts', data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [ACCOUNTS_KEY] });
    },
  });
}

export function useUpdateAccount(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: UpdateAccountRequest) => {
      const response = await apiClient.patch<Account>(`/sfa/accounts/${id}`, data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [ACCOUNTS_KEY] });
    },
  });
}

export function useDeleteAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete(`/sfa/accounts/${id}`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [ACCOUNTS_KEY] });
    },
  });
}
