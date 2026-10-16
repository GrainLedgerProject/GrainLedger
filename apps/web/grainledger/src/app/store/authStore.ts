import type { UserRole } from '@/shared/types/roles';
import { normalizeUserRole, ROLE_DEFINITIONS } from '@/shared/types/roles';
import {
  clearAuthCookies,
  COOKIE_KEYS,
  deleteCookie,
  getCookie,
  setCookie,
} from '@/shared/utils/cookies';
import { create } from 'zustand';

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  phoneNumber?: string;
  role: UserRole;
  organizationId?: string;
  organizationName?: string;
  organizationColor?: string; // e.g. var(--color-company-a)
  assignedFacilities?: string[];
  isActive: boolean;
  permissions: string[];
}

interface AuthState {
  user: UserProfile | null;
  accessToken: string | null;
  refreshToken: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  hasUnsavedDrafts: boolean;

  // Actions
  setUser: (user: UserProfile | null) => void;
  setTokens: (access: string | null, refresh: string | null, rememberMe?: boolean) => void;
  setHasUnsavedDrafts: (hasDrafts: boolean) => void;
  loginSuccess: (user: UserProfile, tokens?: { access: string; refresh: string }, rememberMe?: boolean) => void;
  logout: () => void;
  updateProfile: (updates: Partial<UserProfile>) => void;
}

// Predefined Demo Profiles for all 7 roles to allow immediate testing & verification
export const DEMO_USERS: Record<UserRole, UserProfile> = {
  MILL_ADMIN: {
    id: 'user-ma-01',
    email: 'admin@grainledger.local',
    displayName: 'Lê Văn Quản Trị',
    phoneNumber: '0901 234 567',
    role: 'MILL_ADMIN',
    organizationId: 'org-mill-01',
    organizationName: 'Nhà máy Xay xát Lúa gạo Tiền Giang',
    assignedFacilities: ['Dây chuyền A - Tiền Giang', 'Kho Silo Cụm 1'],
    isActive: true,
    permissions: ['identity.manage', 'platform.configure', 'trace.read.all'],
  },
  MILL_MANAGER: {
    id: 'user-mm-01',
    email: 'mill.manager@grainledger.local',
    displayName: 'Trần Quốc Quản Lý',
    phoneNumber: '0912 345 678',
    role: 'MILL_MANAGER',
    organizationId: 'org-mill-01',
    organizationName: 'Nhà máy Xay xát Lúa gạo Tiền Giang',
    assignedFacilities: ['Dây chuyền A - Tiền Giang'],
    isActive: true,
    permissions: ['platform.read', 'lots.read.all', 'conversions.confirm', 'corrections.reconcile', 'holds.process'],
  },
  MILL_OPERATOR: {
    id: 'user-mo-01',
    email: 'mill.operator@grainledger.local',
    displayName: 'Nguyễn Tấn Vận Hành',
    phoneNumber: '0923 456 789',
    role: 'MILL_OPERATOR',
    organizationId: 'org-mill-01',
    organizationName: 'Nhà máy Xay xát Lúa gạo Tiền Giang',
    assignedFacilities: ['Dây chuyền A - Tiền Giang'],
    isActive: true,
    permissions: ['platform.read', 'lots.read.facility', 'lots.receive', 'conversions.write'],
  },
  CUSTOMER_PROCUREMENT_OFFICER: {
    id: 'user-po-01',
    email: 'purchasing@grainledger.local',
    displayName: 'Võ Minh Thu Mua',
    phoneNumber: '0934 567 890',
    role: 'CUSTOMER_PROCUREMENT_OFFICER',
    organizationId: 'org-cust-01',
    organizationName: 'Công ty CP Xuất nhập khẩu Mekong Rice',
    organizationColor: 'var(--color-company-a)',
    isActive: true,
    permissions: ['farms.write', 'purchases.write', 'history.read'],
  },
  CUSTOMER_MANAGER: {
    id: 'user-cm-01',
    email: 'customer.manager@grainledger.local',
    displayName: 'Đặng Hoàng Giám Đốc',
    phoneNumber: '0945 678 901',
    role: 'CUSTOMER_MANAGER',
    organizationId: 'org-cust-01',
    organizationName: 'Công ty CP Xuất nhập khẩu Mekong Rice',
    organizationColor: 'var(--color-company-a)',
    isActive: true,
    permissions: ['farms.read', 'lots.read.tenant', 'lab.write', 'blend.approve', 'holds.manage', 'trace.read.tenant'],
  },
  CUSTOMER_EXPORT_OFFICER: {
    id: 'user-eo-01',
    email: 'export@grainledger.local',
    displayName: 'Phạm Thanh Xuất Khẩu',
    phoneNumber: '0956 789 012',
    role: 'CUSTOMER_EXPORT_OFFICER',
    organizationId: 'org-cust-01',
    organizationName: 'Công ty CP Xuất nhập khẩu Mekong Rice',
    organizationColor: 'var(--color-company-a)',
    isActive: true,
    permissions: ['mrl.read', 'lots.read.tenant', 'blend.propose', 'containers.write', 'trace.read.tenant'],
  },
  AUTHORIZED_STOCK_VIEWER: {
    id: 'user-sv-01',
    email: 'stock.viewer@grainledger.local',
    displayName: 'Kiểm Toán Viên Độc Lập',
    phoneNumber: '0967 890 123',
    role: 'AUTHORIZED_STOCK_VIEWER',
    organizationId: 'org-cust-01',
    organizationName: 'Công ty CP Xuất nhập khẩu Mekong Rice',
    organizationColor: 'var(--color-company-a)',
    isActive: true,
    permissions: ['stock.view.only'],
  },
};

// Immediate cleanup of any legacy auth data from localStorage
if (typeof window !== 'undefined') {
  try {
    localStorage.removeItem('grainledger_user');
    localStorage.removeItem('grainledger_access_token');
    localStorage.removeItem('grainledger_refresh_token');
    localStorage.removeItem('user');
    localStorage.removeItem('token');
    localStorage.removeItem('access_token');
    localStorage.removeItem('refresh_token');
    localStorage.removeItem('auth');
  } catch {
    // Ignore restricted local storage environments
  }
}

// Initial state: load user and tokens exclusively from cookies
const parseUserFromCookie = (): UserProfile | null => {
  const cookieStr = getCookie(COOKIE_KEYS.USER);
  if (!cookieStr) return null;
  try {
    return JSON.parse(cookieStr) as UserProfile;
  } catch {
    return null;
  }
};

const initialUser = parseUserFromCookie();
const initialAccess =
  getCookie(COOKIE_KEYS.ACCESS_TOKEN) ||
  (initialUser?.id?.startsWith('user-') ? `mock-access-token-${initialUser.role.toLowerCase()}` : null);
const initialRefresh =
  getCookie(COOKIE_KEYS.REFRESH_TOKEN) ||
  (initialUser?.id?.startsWith('user-') ? `mock-refresh-token-${initialUser.role.toLowerCase()}` : null);

export const useAuthStore = create<AuthState>((set) => ({
  user: initialUser,
  accessToken: initialAccess,
  refreshToken: initialRefresh,
  isAuthenticated: !!(initialUser && (initialAccess || initialRefresh)),
  isLoading: false,
  hasUnsavedDrafts: false,

  setUser: (user) => {
    if (user) {
      setCookie(COOKIE_KEYS.USER, JSON.stringify(user), 7);
    } else {
      deleteCookie(COOKIE_KEYS.USER);
    }
    set({ user, isAuthenticated: !!user });
  },

  setTokens: (access, refresh, rememberMe = true) => {
    const accessDays = rememberMe ? 7 : undefined;
    const refreshDays = rememberMe ? 30 : undefined;

    if (access) {
      setCookie(COOKIE_KEYS.ACCESS_TOKEN, access, accessDays);
    } else {
      deleteCookie(COOKIE_KEYS.ACCESS_TOKEN);
    }

    if (refresh) {
      setCookie(COOKIE_KEYS.REFRESH_TOKEN, refresh, refreshDays);
    } else {
      deleteCookie(COOKIE_KEYS.REFRESH_TOKEN);
    }

    set({ accessToken: access, refreshToken: refresh });
  },

  setHasUnsavedDrafts: (hasDrafts) => set({ hasUnsavedDrafts: hasDrafts }),

  loginSuccess: (user, tokens, rememberMe = true) => {
    const access = tokens?.access || `mock-access-token-${user.role.toLowerCase()}`;
    const refresh = tokens?.refresh || `mock-refresh-token-${user.role.toLowerCase()}`;

    const accessDays = rememberMe ? 7 : undefined;
    const refreshDays = rememberMe ? 30 : undefined;

    // Store exclusively in Cookies
    setCookie(COOKIE_KEYS.USER, JSON.stringify(user), accessDays);
    setCookie(COOKIE_KEYS.ACCESS_TOKEN, access, accessDays);
    setCookie(COOKIE_KEYS.REFRESH_TOKEN, refresh, refreshDays);

    set({
      user,
      isAuthenticated: true,
      accessToken: access,
      refreshToken: refresh,
      isLoading: false,
    });
  },

  logout: () => {
    // Clear cookies
    clearAuthCookies();

    // Complete cleanup of any lingering localStorage tokens
    if (typeof window !== 'undefined') {
      try {
        localStorage.removeItem('grainledger_user');
        localStorage.removeItem('grainledger_access_token');
        localStorage.removeItem('grainledger_refresh_token');
        localStorage.removeItem('user');
        localStorage.removeItem('token');
        localStorage.removeItem('access_token');
        localStorage.removeItem('refresh_token');
        localStorage.removeItem('auth');
      } catch {
        // ignore
      }
    }

    set({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
      hasUnsavedDrafts: false,
    });
  },

  updateProfile: (updates) => {
    set((state) => {
      if (!state.user) return state;
      const updatedUser = { ...state.user, ...updates };
      setCookie(COOKIE_KEYS.USER, JSON.stringify(updatedUser), 7);
      return { user: updatedUser };
    });
  },
}));
