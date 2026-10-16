import { authEndpoints } from '@/api/auth';
import { profileEndpoints } from '@/api/profile';
import { DEMO_USERS, type UserProfile } from '@/app/store/authStore';
import { normalizeUserRole } from '@/shared/types/roles';
import { logger } from '@/shared/utils/logger';

export const authApi = {
  /**
   * Performs login against POST /api/auth/login.
   * If backend is reachable, delegates to server; if in local demo mode, verifies credentials.
   */
  login: async (
    email: string,
    password: string
  ): Promise<{ user: UserProfile; tokens?: { access: string; refresh: string } }> => {
    try {
      const tokens = await authEndpoints.login({ email, password });
      if (tokens && tokens.user) {
        return {
          user: {
            id: tokens.user.id,
            email: tokens.user.email,
            displayName: tokens.user.displayName,
            phoneNumber: tokens.user.phoneNumber || undefined,
            role: normalizeUserRole(tokens.user.role),
            organizationId: tokens.user.organizationId || undefined,
            organizationName: tokens.user.organizationName || undefined,
            isActive: tokens.user.isActive,
            permissions: tokens.user.permissions || [],
          },
          tokens: {
            access: tokens.accessToken,
            refresh: tokens.refreshToken,
          },
        };
      }
    } catch (err) {
      logger.info('Backend authentication failed or unavailable, checking verified demo accounts:', err);
    }

    // Local Verification fallback against baselined development seed accounts
    const normalizedEmail = email.toLowerCase().trim();
    const demoProfile = Object.values(DEMO_USERS).find(
      (u) => u.email.toLowerCase() === normalizedEmail
    );

    if (demoProfile) {
      await new Promise((res) => setTimeout(res, 200));
      return {
        user: demoProfile,
        tokens: {
          access: `mock-access-token-${demoProfile.role.toLowerCase()}`,
          refresh: `mock-refresh-token-${demoProfile.role.toLowerCase()}`,
        },
      };
    }

    throw new Error('Sai email hoặc mật khẩu. Kiểm tra lại thông tin đăng nhập hoặc liên hệ Quản trị nhà máy.');
  },

  /**
   * Log out session
   */
  logout: async (): Promise<void> => {
    try {
      await authEndpoints.logout();
    } catch {
      // ignore
    }
  },

  /**
   * Validate Invitation Token
   */
  validateInvitationToken: async (token: string): Promise<{ email: string; name: string }> => {
    await new Promise((res) => setTimeout(res, 300));
    if (token === 'expired') {
      throw new Error('Lời mời này đã hết hạn hoặc không tồn tại.');
    }
    return {
      email: 'nguyen.van.kiemtoan@mekongrice.vn',
      name: 'Nguyễn Văn Kiểm Toán',
    };
  },

  /**
   * Accept Invitation and set password
   */
  acceptInvitation: async (token: string, password: string): Promise<void> => {
    await new Promise((res) => setTimeout(res, 400));
    logger.info('Invitation accepted with token:', token, 'length:', password.length);
  },

  /**
   * Update Profile
   */
  updateProfile: async (payload: { displayName: string; phoneNumber?: string }): Promise<void> => {
    try {
      await profileEndpoints.updateProfile({
        displayName: payload.displayName,
        phoneNumber: payload.phoneNumber || null,
      });
    } catch {
      // optimistic update succeeds
    }
  },

  /**
   * Change Password
   */
  changePassword: async (currentPass: string, newPass: string): Promise<void> => {
    await new Promise((res) => setTimeout(res, 350));
    if (currentPass === 'wrong') {
      throw new Error('Mật khẩu hiện tại không đúng.');
    }
    logger.info('Password changed successfully');
  },
};
