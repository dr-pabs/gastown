import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { MyCases } from '../../pages/MyCases';

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({
    accounts: [
      {
        username: 'user@example.com',
        idTokenClaims: { tid: 'tenant-1', roles: [] },
      },
    ],
  }),
}));

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string) => key,
  }),
}));

const mockGet = vi.fn();
vi.mock('../../lib/apiClient', () => ({
  apiClient: { get: mockGet },
}));

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={qc}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  );
}

describe('MyCases', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows loading spinner initially', () => {
    mockGet.mockReturnValue(new Promise(() => undefined));
    render(<MyCases />, { wrapper });
    // Spinner or loading state present
    expect(document.querySelector('svg') ?? screen.queryByRole('status')).toBeTruthy();
  });

  it('renders cases list when data is returned', async () => {
    mockGet.mockResolvedValue({
      data: {
        items: [
          {
            id: 'case-1',
            subject: 'Login issue',
            status: 'Open',
            priority: 'High',
            createdAt: '2024-01-15T10:00:00Z',
            slaBreached: false,
            isDeleted: false,
            tenantId: 't1',
            updatedAt: '2024-01-15T10:00:00Z',
          },
        ],
        totalCount: 1,
        page: 1,
        pageSize: 20,
        totalPages: 1,
      },
    });
    render(<MyCases />, { wrapper });
    expect(await screen.findByText('Login issue')).toBeInTheDocument();
  });

  it('shows empty state when no cases', async () => {
    mockGet.mockResolvedValue({
      data: {
        items: [],
        totalCount: 0,
        page: 1,
        pageSize: 20,
        totalPages: 0,
      },
    });
    render(<MyCases />, { wrapper });
    expect(await screen.findByText('cases.noActiveCases')).toBeInTheDocument();
  });
});
