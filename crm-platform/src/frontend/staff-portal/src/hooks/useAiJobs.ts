import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import apiClient from '../lib/apiClient';
import type {
  AiJob,
  AiResult,
  PromptTemplate,
  SyncAiRequest,
  AsyncAiRequest,
  PagedResult,
  PaginationParams,
} from '../types';

const AI_JOBS_KEY = 'ai-jobs' as const;
const PROMPT_TEMPLATES_KEY = 'prompt-templates' as const;

export function useAiJobs(params: PaginationParams) {
  return useQuery({
    queryKey: [AI_JOBS_KEY, params],
    queryFn: async () => {
      const response = await apiClient.get<PagedResult<AiJob>>('/ai/jobs', { params });
      return response.data;
    },
  });
}

export function useAiJob(id: string) {
  return useQuery({
    queryKey: [AI_JOBS_KEY, id],
    queryFn: async () => {
      const response = await apiClient.get<AiJob>(`/ai/jobs/${id}`);
      return response.data;
    },
    enabled: Boolean(id),
    // Poll until terminal
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      if (status === 'Succeeded' || status === 'Failed' || status === 'Abandoned') {
        return false;
      }
      return 3_000;
    },
  });
}

export function useAiResult(jobId: string) {
  return useQuery({
    queryKey: [AI_JOBS_KEY, jobId, 'result'],
    queryFn: async () => {
      const response = await apiClient.get<AiResult>(`/ai/jobs/${jobId}/result`);
      return response.data;
    },
    enabled: Boolean(jobId),
  });
}

export function useRunSyncAi() {
  return useMutation({
    mutationFn: async (data: SyncAiRequest) => {
      const response = await apiClient.post<AiResult>('/ai/sync', data);
      return response.data;
    },
  });
}

export function useQueueAiJob() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: AsyncAiRequest) => {
      const response = await apiClient.post<AiJob>('/ai/jobs', data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [AI_JOBS_KEY] });
    },
  });
}

export function usePromptTemplates(params: PaginationParams) {
  return useQuery({
    queryKey: [PROMPT_TEMPLATES_KEY, params],
    queryFn: async () => {
      const response = await apiClient.get<PagedResult<PromptTemplate>>('/ai/prompt-templates', {
        params,
      });
      return response.data;
    },
  });
}

export function useCreatePromptTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: Omit<PromptTemplate, 'id' | 'tenantId' | 'createdAt' | 'updatedAt' | 'isSystemDefault'>) => {
      const response = await apiClient.post<PromptTemplate>('/ai/prompt-templates', data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [PROMPT_TEMPLATES_KEY] });
    },
  });
}

export function useUpdatePromptTemplate(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: Partial<Pick<PromptTemplate, 'name' | 'templateBody'>>) => {
      const response = await apiClient.patch<PromptTemplate>(`/ai/prompt-templates/${id}`, data);
      return response.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [PROMPT_TEMPLATES_KEY] });
    },
  });
}

export function useDeletePromptTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete(`/ai/prompt-templates/${id}`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: [PROMPT_TEMPLATES_KEY] });
    },
  });
}
