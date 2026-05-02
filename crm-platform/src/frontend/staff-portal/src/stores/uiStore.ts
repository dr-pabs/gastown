import { create } from 'zustand';

interface UiState {
  sidebarOpen: boolean;
  theme: 'light' | 'dark';
  activeNav: string;

  setSidebarOpen: (open: boolean) => void;
  toggleSidebar: () => void;
  setTheme: (theme: 'light' | 'dark') => void;
  setActiveNav: (nav: string) => void;
}

export const useUiStore = create<UiState>()((set) => ({
  sidebarOpen: true,
  theme: 'light',
  activeNav: 'dashboard',

  setSidebarOpen: (open) => set({ sidebarOpen: open }),
  toggleSidebar: () => set((state) => ({ sidebarOpen: !state.sidebarOpen })),
  setTheme: (theme) => set({ theme }),
  setActiveNav: (activeNav) => set({ activeNav }),
}));
