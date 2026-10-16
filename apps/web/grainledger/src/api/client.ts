/**
 * GrainLedger Core API Client
 * Generated & typed according to OpenAPI 3.1.1 specification
 * Base URL: http://localhost:5080 (or proxied via /api)
 */
import { useAuthStore } from '@/app/store/authStore';
import { normalizeUserRole } from '@/shared/types/roles';
import { COOKIE_KEYS, getCookie } from '@/shared/utils/cookies';
import { logger } from '@/shared/utils/logger';

export interface ProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export class ApiError extends Error {
  public status: number;
  public problem?: ProblemDetails;

  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }
}

/**
 * Check if a JWT is expired or within 15 seconds of expiring.
 * Returns false for mock tokens or unparseable non-JWT tokens.
 */
export function isJwtExpired(token: string | null): boolean {
  if (!token) return true;
  if (token.startsWith('mock-')) return false;

  try {
    const parts = token.split('.');
    if (parts.length !== 3) return false;

    // Decode Base64Url
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split('')
        .map((c) => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    );

    const payload = JSON.parse(jsonPayload);
    if (typeof payload.exp === 'number') {
      // 15-second safety buffer before true expiry
      return Date.now() >= payload.exp * 1000 - 15000;
    }
  } catch {
    return false;
  }
  return false;
}

/** Single-flight mutex for token refresh so concurrent requests share the exact same refresh cycle */
let refreshPromise: Promise<string | null> | null = null;

/**
 * Executes a token refresh cycle against POST /api/auth/refresh.
 * Rotates the single-use refresh token, updates cookies and Zustand store.
 */
export async function requestTokenRefresh(): Promise<string | null> {
  // If a refresh is already in flight, reuse the ongoing promise
  if (refreshPromise) {
    return refreshPromise;
  }

  const { refreshToken: stateRefreshToken, setTokens, setUser, logout } = useAuthStore.getState();
  const refreshToken = stateRefreshToken || getCookie(COOKIE_KEYS.REFRESH_TOKEN);

  if (!refreshToken || refreshToken.startsWith('mock-')) {
    return null;
  }

  refreshPromise = (async () => {
    try {
      logger.info('Performing single-flight refresh token rotation...');
      const res = await fetch('/api/auth/refresh', {
        method: 'POST',
        credentials: 'include',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'application/json',
        },
        body: JSON.stringify({ refreshToken }),
      });

      if (!res.ok) {
        logger.warn(`Token refresh endpoint responded with status ${res.status}`);
        logout();
        return null;
      }

      const data = await res.json();
      const newAccessToken = (data.accessToken || data.AccessToken) as string | undefined;
      const newRefreshToken = (data.refreshToken || data.RefreshToken || refreshToken) as string;

      if (!newAccessToken) {
        logger.warn('Refresh response did not return a valid accessToken');
        logout();
        return null;
      }

      // Persist fresh tokens into cookies and Zustand store
      setTokens(newAccessToken, newRefreshToken);

      const userDto = data.user || data.User;
      if (userDto) {
        setUser({
          id: userDto.id || userDto.Id,
          email: userDto.email || userDto.Email,
          displayName: userDto.displayName || userDto.DisplayName,
          phoneNumber: userDto.phoneNumber || userDto.PhoneNumber || undefined,
          role: normalizeUserRole(userDto.role || userDto.Role),
          organizationId: userDto.organizationId || userDto.OrganizationId || undefined,
          organizationName: userDto.organizationName || userDto.OrganizationName || undefined,
          isActive: userDto.isActive ?? userDto.IsActive ?? true,
          permissions: userDto.permissions || userDto.Permissions || [],
        });
      }

      logger.info('Token refresh successfully completed and cookies updated.');
      return newAccessToken;
    } catch (err) {
      logger.error('Unexpected error during token refresh execution:', err);
      logout();
      return null;
    } finally {
      refreshPromise = null;
    }
  })();

  return refreshPromise;
}

/** Base fetch wrapper with Bearer token injection, CSRF and refresh handling */
export async function apiFetch<T>(
  endpoint: string,
  options: RequestInit = {}
): Promise<T> {
  const isAuthEndpoint =
    endpoint.includes('/api/auth/login') ||
    endpoint.includes('/api/auth/refresh') ||
    endpoint.includes('/api/auth/register') ||
    endpoint.includes('/api/auth/verify-email') ||
    endpoint.includes('/api/auth/resend-verification') ||
    endpoint.includes('/api/auth/forgot-password') ||
    endpoint.includes('/api/auth/reset-password');

  let accessToken = useAuthStore.getState().accessToken || getCookie(COOKIE_KEYS.ACCESS_TOKEN);
  const refreshToken = useAuthStore.getState().refreshToken || getCookie(COOKIE_KEYS.REFRESH_TOKEN);

  // Proactive Token Refresh:
  // If access token is expired (or missing) but a valid real refresh token exists, refresh before sending
  if (
    !isAuthEndpoint &&
    refreshToken &&
    !refreshToken.startsWith('mock-') &&
    isJwtExpired(accessToken)
  ) {
    logger.info(`Access token expired or missing for ${endpoint}, proactively refreshing...`);
    const refreshedToken = await requestTokenRefresh();
    if (refreshedToken) {
      accessToken = refreshedToken;
    }
  }

  const headers = new Headers(options.headers || {});
  if (!headers.has('Content-Type') && !(options.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json');
  }

  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }

  // Include cookies automatically in cross-origin / same-origin API requests
  const fetchOptions: RequestInit = {
    credentials: 'include',
    ...options,
    headers,
  };

  const url = endpoint.startsWith('/') ? endpoint : `/${endpoint}`;

  let response: Response;
  try {
    response = await fetch(url, fetchOptions);
  } catch (netErr) {
    logger.warn(`Network error on ${url}:`, netErr);
    throw new ApiError(0, 'Không thể kết nối đến máy chủ backend (Port 5080).');
  }

  // Reactive Token Refresh:
  // If 401 Unauthorized is returned from backend on a protected endpoint, execute single-flight refresh
  if (response.status === 401 && !isAuthEndpoint) {
    const isMock = accessToken?.startsWith('mock-') || refreshToken?.startsWith('mock-');

    if (refreshToken && !isMock) {
      logger.info(`Endpoint ${url} returned 401, triggering single-flight token refresh...`);
      const newAccessToken = await requestTokenRefresh();

      if (newAccessToken) {
        // Retry original request with fresh access token and credentials
        headers.set('Authorization', `Bearer ${newAccessToken}`);
        try {
          response = await fetch(url, { ...options, headers, credentials: 'include' });
        } catch (retryErr) {
          logger.warn(`Retry failed after token refresh on ${url}:`, retryErr);
          throw new ApiError(0, 'Lỗi kết nối khi gửi lại yêu cầu sau khi cấp mới token.');
        }
      } else {
        useAuthStore.getState().logout();
        throw new ApiError(401, 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
      }
    } else if (isMock) {
      logger.info(`Endpoint ${url} returned 401 for mock session, falling back safely.`);
      throw new ApiError(401, 'Phiên cục bộ chưa kết nối máy chủ backend. Chuyển sang dữ liệu mô phỏng.');
    } else {
      useAuthStore.getState().logout();
      throw new ApiError(401, 'Yêu cầu đăng nhập để tiếp tục.');
    }
  }

  // Handle successful empty response (204 No Content)
  if (response.status === 204) {
    return {} as T;
  }

  // Parse JSON response or problem details
  const contentType = response.headers.get('content-type') || '';
  const isJson = contentType.includes('application/json') || contentType.includes('application/problem+json');
  const data = isJson ? await response.json() : null;

  if (!response.ok) {
    const problem = data as ProblemDetails;
    const msg = problem?.detail || problem?.title || `Lỗi máy chủ (${response.status})`;
    throw new ApiError(response.status, msg, problem);
  }

  return data as T;
}

