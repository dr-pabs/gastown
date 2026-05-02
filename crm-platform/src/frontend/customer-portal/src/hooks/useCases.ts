import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../lib/apiClient';
import type {
  Case,
  CaseComment,
  CreateCaseRequest,
  UpdateCaseRequest,
  AddCaseCommentRequest,
  PagedResult,
  PaginationParams,
} from '../types';

const CASES_KEY = 'cases' as const;

export function useMyCases(params: PaginationParams) {
  return useQuery({
    queryKey: [CASES_KEY, 'my', params],
    queryFn: async () => {
      const response = await apiClient.get<PagedResult<Case>>('/css/cases/my', { params });
      return response.data;
    },
  });
}

export function useAllCases(params: PaginationParams) {
  return useQuery({
    queryKey: [CASES_KEY, 'all', params],
    queryFn: async () => {
      const response = await apiClient.get<PagedResult<Case>>('/css/cases', { params });
      return response.data;
    },
  });
}

export function useCase(id: string) {
  return useQuery({
    queryKey: [CASES_KEY, id],
    queryFn: async () => {
      const response = await apiClient.get<Case>(`/css/cases/${id}`);
      return response.data;
    },
    enabled: Boolean(id),
  });
}

export function useCaseComments(caseId: string) {
  return useQuery({
    queryKey: [CASES_KEY, caseId, 'comments'],
    queryFn: async () => {
      const response = await apiClient.get<CaseComment[]>(`/css/cases/${caseId}/comments`);
      return response.data;
    },
    enabled: Boolean(caseId),
  });
}

export function useCreateCase() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateCaseRequest) => {
      const response = await apiClient.post<Case>('/css/cases', data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [CASES_KEY] });
    },
  });
}

export function useUpdateCase(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: UpdateCaseRequest) => {
      const response = await apiClient.patch<Case>(`/css/cases/${id}`, data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [CASES_KEY] });
    },
  });
}

export function useAddCaseComment(caseId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: AddCaseCommentRequest) => {
      const response = await apiClient.post<CaseComment>(
        `/css/cases/${caseId}/comments`,
        data,
      );
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [CASES_KEY, caseId, 'comments'] });
    },
  });
}

export function useCloseCase(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await apiClient.post(`/css/cases/${id}/close`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [CASES_KEY] });
    },
  });
}
