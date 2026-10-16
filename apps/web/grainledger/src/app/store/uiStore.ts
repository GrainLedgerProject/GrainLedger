import i18n from '@/app/i18n';
import { create } from 'zustand';

interface UiState {
  theme: 'light' | 'dark';
  language: 'vi' | 'en';
  isSidebarCollapsed: boolean;
  isSearchOpen: boolean;
  isNotificationsOpen: boolean;
  isOnline: boolean;
  pendingOfflineCount: number;

  // Actions
  toggleTheme: () => void;
  setTheme: (theme: 'light' | 'dark') => void;
  setLanguage: (lang: 'vi' | 'en') => void;
  toggleSidebar: () => void;
  setSidebarCollapsed: (collapsed: boolean) => void;
  setSearchOpen: (open: boolean) => void;
  setNotificationsOpen: (open: boolean) => void;
  setOnline: (online: boolean) => void;
  setPendingOfflineCount: (count: number) => void;
}

// Read initial stored values
const storedTheme = (localStorage.getItem('grainledger_theme') as 'light' | 'dark') ||
  (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');

const storedCollapsed = localStorage.getItem('grainledger_sidebar_collapsed') === 'true';
const storedLang = (localStorage.getItem('grainledger_lang') as 'vi' | 'en') || 'vi';

if (typeof document !== 'undefined') {
  document.documentElement.lang = storedLang;
}

// Apply initial theme class to HTML element
if (storedTheme === 'dark') {
  document.documentElement.classList.add('dark');
} else {
  document.documentElement.classList.remove('dark');
}

export const useUiStore = create<UiState>((set) => ({
  theme: storedTheme,
  language: storedLang,
  isSidebarCollapsed: storedCollapsed,
  isSearchOpen: false,
  isNotificationsOpen: false,
  isOnline: navigator.onLine,
  pendingOfflineCount: 3, // 3 pending offline records in field queue

  toggleTheme: () => {
    set((state) => {
      const next = state.theme === 'light' ? 'dark' : 'light';
      localStorage.setItem('grainledger_theme', next);
      if (next === 'dark') {
        document.documentElement.classList.add('dark');
      } else {
        document.documentElement.classList.remove('dark');
      }
      return { theme: next };
    });
  },

  setTheme: (theme) => {
    localStorage.setItem('grainledger_theme', theme);
    if (theme === 'dark') {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
    set({ theme });
  },

  setLanguage: (lang) => {
    localStorage.setItem('grainledger_lang', lang);
    if (typeof document !== 'undefined') {
      document.documentElement.lang = lang;
    }
    void i18n.changeLanguage(lang);
    set({ language: lang });
  },

  toggleSidebar: () => {
    set((state) => {
      const next = !state.isSidebarCollapsed;
      localStorage.setItem('grainledger_sidebar_collapsed', String(next));
      return { isSidebarCollapsed: next };
    });
  },

  setSidebarCollapsed: (collapsed) => {
    localStorage.setItem('grainledger_sidebar_collapsed', String(collapsed));
    set({ isSidebarCollapsed: collapsed });
  },

  setSearchOpen: (open) => set({ isSearchOpen: open }),
  setNotificationsOpen: (open) => set({ isNotificationsOpen: open }),
  setOnline: (online) => set({ isOnline: online }),
  setPendingOfflineCount: (count) => set({ pendingOfflineCount: count }),
}));

// Listen to network online/offline events
window.addEventListener('online', () => useUiStore.getState().setOnline(true));
window.addEventListener('offline', () => useUiStore.getState().setOnline(false));

// Bidirectional synchronization when i18n changes language externally
i18n.on('languageChanged', (lng) => {
  const normalized = lng?.startsWith('en') ? 'en' : 'vi';
  if (useUiStore.getState().language !== normalized) {
    useUiStore.setState({ language: normalized });
  }
  if (typeof document !== 'undefined') {
    document.documentElement.lang = normalized;
  }
});
