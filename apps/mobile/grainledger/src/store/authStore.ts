import { create } from 'zustand';
import { UserRole, normalizeUserRole } from '../types/roles';
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

export interface SessionUserDto {
  id: string;
  email: string;
  displayName: string;
  phoneNumber?: string | null;
  role?: string | null;
  organizationId?: string | null;
  organizationName?: string | null;
  isActive: boolean;
  permissions: string[];
}

export interface IssuedTokens {
  accessToken: string;
  refreshToken: string;
  tokenType: string;
  expiresIn: number;
  user: SessionUserDto;
}

interface AuthState {
  user: UserProfile | null;
  token: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  activeRole: UserRole | null;

  initAuth: () => Promise<void>;
  loginWithCredentials: (email: string, pass: string) => Promise<{ success: boolean; error?: string }>;
  logout: () => Promise<void>;
}

const AUTH_USER_KEY = 'grainledger_auth_user';

export const useAuthStore = create<AuthState>((set) => ({
  user: null,
  token: null,
  isAuthenticated: false,
  isLoading: true,
  activeRole: null,

  initAuth: async () => {
    try {
      set({ isLoading: true });
      const savedToken = await storage.get<string>('grainledger_token');
      if (savedToken) {
        api.setToken(savedToken);
        const res = await api.request<SessionUserDto>('/api/auth/session');
        if (res.data && !res.isMock) {
          const role = normalizeUserRole(res.data.role);
          const user: UserProfile = {
            id: res.data.id,
            email: res.data.email,
            displayName: res.data.displayName,
            phoneNumber: res.data.phoneNumber || undefined,
            role,
            organizationId: res.data.organizationId || undefined,
            organizationName: res.data.organizationName || undefined,
            assignedFacilities: [],
            isActive: res.data.isActive,
            permissions: res.data.permissions || [],
          };
          set({
            user,
            token: savedToken,
            activeRole: role,
            isAuthenticated: true,
            isLoading: false,
          });
          await storage.set(AUTH_USER_KEY, user);
          return;
        }

        // If offline, check saved user
        const savedUser = await storage.get<UserProfile>(AUTH_USER_KEY);
        if (savedUser && !res.error?.includes('401')) {
          set({
            user: savedUser,
            token: savedToken,
            activeRole: savedUser.role,
            isAuthenticated: true,
            isLoading: false,
          });
          return;
        }

        // Token invalid / 401
        api.setToken(null);
        await storage.remove(AUTH_USER_KEY);
      }

      set({
        user: null,
        token: null,
        activeRole: null,
        isAuthenticated: false,
        isLoading: false,
      });
    } catch {
      set({
        user: null,
        token: null,
        activeRole: null,
        isAuthenticated: false,
        isLoading: false,
      });
    }
  },

  loginWithCredentials: async (email: string, _pass: string) => {
    set({ isLoading: true });

    const res = await api.request<IssuedTokens>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email: email.trim(), password: _pass }),
    });

    if (res.data && res.data.accessToken && res.data.user) {
      const accessToken = res.data.accessToken;
      api.setToken(accessToken);
      const role = normalizeUserRole(res.data.user.role);
      const user: UserProfile = {
        id: res.data.user.id,
        email: res.data.user.email,
        displayName: res.data.user.displayName,
        phoneNumber: res.data.user.phoneNumber || undefined,
        role,
        organizationId: res.data.user.organizationId || undefined,
        organizationName: res.data.user.organizationName || undefined,
        assignedFacilities: [],
        isActive: res.data.user.isActive,
        permissions: res.data.user.permissions || [],
      };

      set({
        user,
        token: accessToken,
        activeRole: role,
        isAuthenticated: true,
        isLoading: false,
      });

      await storage.set(AUTH_USER_KEY, user);
      return { success: true };
    }

    set({ isLoading: false });
    return {
      success: false,
      error: res.error || 'Đăng nhập không thành công. Vui lòng kiểm tra lại tài khoản và mật khẩu.',
    };
  },

  logout: async () => {
    try {
      await api.request('/api/auth/logout', { method: 'POST' });
    } catch {
      // Ignore network errors on logout
    }
    api.setToken(null);
    await storage.remove(AUTH_USER_KEY);
    set({
      user: null,
      token: null,
      activeRole: null,
      isAuthenticated: false,
      isLoading: false,
    });
  },
}));
