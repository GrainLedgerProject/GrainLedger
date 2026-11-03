import type { UserRole } from '@/shared/types/roles';
import { normalizeUserRole, ROLE_DEFINITIONS } from '@/shared/types/roles';
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
  setTokens: (access: string | null, refresh: string | null) => void;
  setHasUnsavedDrafts: (hasDrafts: boolean) => void;
  loginSuccess: (user: UserProfile, tokens?: { access: string; refresh: string }) => void;
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

// Initial state checks localStorage for session persistence
const storedUser = localStorage.getItem('grainledger_user');
const initialUser: UserProfile | null = storedUser ? JSON.parse(storedUser) : null;

export const useAuthStore = create<AuthState>((set) => ({
  user: initialUser,
  accessToken: null,
  refreshToken: null,
  isAuthenticated: !!initialUser,
  isLoading: false,
  hasUnsavedDrafts: false,

  setUser: (user) => {
    if (user) {
      localStorage.setItem('grainledger_user', JSON.stringify(user));
    } else {
      localStorage.removeItem('grainledger_user');
    }
    set({ user, isAuthenticated: !!user });
  },

  setTokens: (access, refresh) => {
    set({ accessToken: access, refreshToken: refresh });
  },

  setHasUnsavedDrafts: (hasDrafts) => set({ hasUnsavedDrafts: hasDrafts }),

  loginSuccess: (user, tokens) => {
    localStorage.setItem('grainledger_user', JSON.stringify(user));
    set({
      user,
      isAuthenticated: true,
      accessToken: tokens?.access || 'mock-access-token',
      refreshToken: tokens?.refresh || 'mock-refresh-token',
      isLoading: false,
    });
  },

  logout: () => {
    localStorage.removeItem('grainledger_user');
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
      localStorage.setItem('grainledger_user', JSON.stringify(updatedUser));
      return { user: updatedUser };
    });
  },
}));
