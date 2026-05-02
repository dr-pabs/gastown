import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { useLeads } from '../../hooks/useLeads';
import apiClient from '../../lib/apiClient';

vi.mock('../../lib/apiClient');

const mockedGet = vi.spyOn(apiClient, 'get');

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  };
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('useLeads', () => {
  it('calls /sfa/leads with pagination params', async () => {
    const params = { page: 1, pageSize: 20 };
    const mockData = { items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 };
    mockedGet.mockResolvedValueOnce({ data: mockData });

    const { result } = renderHook(() => useLeads(params), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockedGet).toHaveBeenCalledWith('/sfa/leads', { params });
    expect(result.current.data).toEqual(mockData);
  });

  it('returns loading state initially', () => {
    mockedGet.mockReturnValueOnce(new Promise(() => undefined));
    const { result } = renderHook(() => useLeads({ page: 1, pageSize: 20 }), {
      wrapper: createWrapper(),
    });
    expect(result.current.isLoading).toBe(true);
  });

  it('returns error state on failure', async () => {
    mockedGet.mockRejectedValueOnce(new Error('Network error'));
    const { result } = renderHook(() => useLeads({ page: 1, pageSize: 20 }), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
