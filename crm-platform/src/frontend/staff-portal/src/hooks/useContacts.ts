import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../lib/apiClient';
import type {
  Contact,
  CreateContactRequest,
  UpdateContactRequest,
  PagedResult,
  PaginationParams,
} from '../types';

const CONTACTS_KEY = 'contacts' as const;

export function useContacts(params: PaginationParams) {
  return useQuery({
    queryKey: [CONTACTS_KEY, params],
    queryFn: async () => {
      const response = await apiClient.get<PagedResult<Contact>>('/sfa/contacts', { params });
      return response.data;
    },
  });
}

export function useContact(id: string) {
  return useQuery({
    queryKey: [CONTACTS_KEY, id],
    queryFn: async () => {
      const response = await apiClient.get<Contact>(`/sfa/contacts/${id}`);
      return response.data;
    },
    enabled: Boolean(id),
  });
}

export function useCreateContact() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateContactRequest) => {
      const response = await apiClient.post<Contact>('/sfa/contacts', data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [CONTACTS_KEY] });
    },
  });
}

export function useUpdateContact(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: UpdateContactRequest) => {
      const response = await apiClient.patch<Contact>(`/sfa/contacts/${id}`, data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [CONTACTS_KEY] });
    },
  });
}

export function useDeleteContact() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete(`/sfa/contacts/${id}`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [CONTACTS_KEY] });
    },
  });
}
