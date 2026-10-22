import { create } from 'zustand';
import { UserRole } from '../types/roles';
import { storage } from '../services/storage';
import { api } from '../services/api';

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  phoneNumber?: string;
  role: UserRole;
  organizationId?: string;
  organizationName?: string;
  assignedFacilities?: string[];
  isActive: boolean;
  permissions: string[];
}

export const DEMO_USERS: Record<UserRole, UserProfile> = {
  CUSTOMER_PROCUREMENT_OFFICER: {
    id: 'user-po-01',
    email: 'purchasing@grainledger.local',
    displayName: 'Võ Minh Thu Mua',
    phoneNumber: '0934 567 890',
    role: 'CUSTOMER_PROCUREMENT_OFFICER',
    organizationId: 'org-cust-01',
    organizationName: 'Công ty CP Xuất nhập khẩu Mekong Rice',
    assignedFacilities: ['Trạm thu mua Cái Bè - Tiền Giang'],
    isActive: true,
    permissions: ['farms.write', 'purchases.write', 'history.read'],
  },
  MILL_OPERATOR: {
    id: 'user-mo-01',
    email: 'mill.operator@grainledger.local',
    displayName: 'Nguyễn Tấn Vận Hành',
    phoneNumber: '0923 456 789',
    role: 'MILL_OPERATOR',
    organizationId: 'org-mill-01',
    organizationName: 'Nhà máy Xay xát Lúa gạo Tiền Giang',
    assignedFacilities: ['Dây chuyền A - Tiền Giang', 'Khu Silo số 2'],
    isActive: true,
    permissions: ['platform.read', 'lots.read.facility', 'lots.receive', 'conversions.write'],
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
  CUSTOMER_MANAGER: {
    id: 'user-cm-01',
    email: 'customer.manager@grainledger.local',
    displayName: 'Đặng Hoàng Giám Đốc',
    phoneNumber: '0945 678 901',
    role: 'CUSTOMER_MANAGER',
    organizationId: 'org-cust-01',
    organizationName: 'Công ty CP Xuất nhập khẩu Mekong Rice',
    isActive: true,
    permissions: ['plans.approve', 'lab.write', 'holds.write', 'trace.read.customer'],
  },
  CUSTOMER_EXPORT_OFFICER: {
    id: 'user-eo-01',
    email: 'export@grainledger.local',
    displayName: 'Phạm Thanh Xuất Khẩu',
    phoneNumber: '0956 789 012',
    role: 'CUSTOMER_EXPORT_OFFICER',
    organizationId: 'org-cust-01',
    organizationName: 'Công ty CP Xuất nhập khẩu Mekong Rice',
    isActive: true,
    permissions: ['orders.write', 'fulfillments.write', 'containers.seal'],
  },
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
  AUTHORIZED_STOCK_VIEWER: {
    id: 'user-sv-01',
    email: 'stock.viewer@grainledger.local',
    displayName: 'Bùi Thị Giám Sát Tồn Kho',
    phoneNumber: '0967 890 123',
    role: 'AUTHORIZED_STOCK_VIEWER',
    organizationId: 'org-cust-01',
    organizationName: 'Công ty CP Xuất nhập khẩu Mekong Rice',
    isActive: true,
    permissions: ['stock.read', 'trace.read'],
  },
};

interface AuthState {
  user: UserProfile | null;
  token: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  activeRole: UserRole;

  initAuth: () => Promise<void>;
  loginAsDemo: (role: UserRole) => Promise<void>;
  loginWithCredentials: (email: string, pass: string) => Promise<{ success: boolean; error?: string }>;
  logout: () => Promise<void>;
  switchRole: (role: UserRole) => void;
}

const AUTH_USER_KEY = 'grainledger_auth_user';

export const useAuthStore = create<AuthState>((set, get) => ({
  user: DEMO_USERS.CUSTOMER_PROCUREMENT_OFFICER,
  token: 'mock-jwt-token-po-01',
  isAuthenticated: true,
  isLoading: false,
  activeRole: 'CUSTOMER_PROCUREMENT_OFFICER',

  initAuth: async () => {
    try {
      set({ isLoading: true });
      const savedUser = await storage.get<UserProfile>(AUTH_USER_KEY);
      if (savedUser) {
        set({
          user: savedUser,
          isAuthenticated: true,
          activeRole: savedUser.role,
          isLoading: false,
        });
      } else {
        // Default to field officer for realistic mobile intake flow
        const defaultUser = DEMO_USERS.CUSTOMER_PROCUREMENT_OFFICER;
        set({
          user: defaultUser,
          isAuthenticated: true,
          activeRole: defaultUser.role,
          isLoading: false,
        });
        await storage.set(AUTH_USER_KEY, defaultUser);
      }
    } catch {
      set({ isLoading: false });
    }
  },

  loginAsDemo: async (role: UserRole) => {
    const demo = DEMO_USERS[role] || DEMO_USERS.CUSTOMER_PROCUREMENT_OFFICER;
    set({
      user: demo,
      activeRole: role,
      isAuthenticated: true,
      token: `mock-jwt-token-${demo.id}`,
    });
    api.setToken(`mock-jwt-token-${demo.id}`);
    await storage.set(AUTH_USER_KEY, demo);
  },

  loginWithCredentials: async (email: string, _pass: string) => {
    set({ isLoading: true });
    // Attempt actual API call
    const res = await api.request<{ accessToken: string; user: UserProfile }>('/api/identity/login', {
      method: 'POST',
      body: JSON.stringify({ email, password: _pass }),
    });

    if (res.data && res.data.accessToken) {
      api.setToken(res.data.accessToken);
      set({
        user: res.data.user,
        activeRole: res.data.user.role,
        isAuthenticated: true,
        isLoading: false,
        token: res.data.accessToken,
      });
      await storage.set(AUTH_USER_KEY, res.data.user);
      return { success: true };
    }

    // Match known demo emails if offline
    const matched = Object.values(DEMO_USERS).find((u) => u.email.toLowerCase() === email.toLowerCase());
    if (matched) {
      set({
        user: matched,
        activeRole: matched.role,
        isAuthenticated: true,
        isLoading: false,
        token: `mock-jwt-token-${matched.id}`,
      });
      api.setToken(`mock-jwt-token-${matched.id}`);
      await storage.set(AUTH_USER_KEY, matched);
      return { success: true };
    }

    // Fallback: successful login as procurement officer
    const fallbackUser: UserProfile = {
      id: 'user-custom-' + Date.now(),
      email,
      displayName: email.split('@')[0],
      role: 'CUSTOMER_PROCUREMENT_OFFICER',
      organizationName: 'Mekong Rice Export Co.',
      isActive: true,
      permissions: ['farms.write', 'purchases.write'],
    };

    set({
      user: fallbackUser,
      activeRole: 'CUSTOMER_PROCUREMENT_OFFICER',
      isAuthenticated: true,
      isLoading: false,
      token: 'custom-token',
    });
    await storage.set(AUTH_USER_KEY, fallbackUser);
    return { success: true };
  },

  logout: async () => {
    api.setToken(null);
    await storage.remove(AUTH_USER_KEY);
    set({
      user: null,
      isAuthenticated: false,
      token: null,
    });
  },

  switchRole: (role: UserRole) => {
    get().loginAsDemo(role);
  },
}));
