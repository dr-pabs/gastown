import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { Dashboard } from '../../pages/Dashboard';
import apiClient from '../../lib/apiClient';

// Mock MSAL hooks
vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({
    accounts: [{ name: 'Test User', username: 'test@example.com' }],
    instance: { acquireTokenSilent: vi.fn() },
  }),
}));

vi.mock('react-i18next', () => ({
  useTranslation: () => ({
    t: (key: string) => key,
    i18n: { changeLanguage: vi.fn() },
  }),
}));

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
  // Return empty paged results for all queries
  mockedGet.mockResolvedValue({
    data: { items: [], totalCount: 0, page: 1, pageSize: 5, totalPages: 0 },
  });
});

describe('Dashboard', () => {
  it('renders the dashboard heading', async () => {
    render(<Dashboard />, { wrapper: createWrapper() });
    expect(screen.getByText('dashboard.title')).toBeInTheDocument();
  });

  it('shows loading spinner while data is fetching', () => {
    mockedGet.mockReturnValue(new Promise(() => undefined));
    render(<Dashboard />, { wrapper: createWrapper() });
    // Spinner is shown for each loading query; at least one should exist
    const spinners = document.querySelectorAll('[class*="animate-spin"]');
    expect(spinners.length).toBeGreaterThan(0);
  });
});
