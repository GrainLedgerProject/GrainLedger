/**
 * GrainLedger Core API Client
 * Generated & typed according to OpenAPI 3.1.1 specification
 * Base URL: http://localhost:5080 (or proxied via /api)
 */
import { useAuthStore } from '@/app/store/authStore';
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

/** Base fetch wrapper with Bearer token injection, CSRF and refresh handling */
export async function apiFetch<T>(
  endpoint: string,
  options: RequestInit = {}
): Promise<T> {
  const { accessToken, setTokens, logout } = useAuthStore.getState();

  const headers = new Headers(options.headers || {});
  if (!headers.has('Content-Type') && !(options.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json');
  }

  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }

  const url = endpoint.startsWith('/') ? endpoint : `/${endpoint}`;

  let response: Response;
  try {
    response = await fetch(url, { ...options, headers });
  } catch (netErr) {
    logger.warn(`Network error on ${url}:`, netErr);
    throw new ApiError(0, 'Không thể kết nối đến máy chủ backend (Port 5080).');
  }

  // Handle 401 Unauthorized with token refresh single-flight
  if (response.status === 401 && endpoint !== '/api/auth/login' && endpoint !== '/api/auth/refresh') {
    const { refreshToken } = useAuthStore.getState();
    if (refreshToken) {
      try {
        const refreshRes = await fetch('/api/auth/refresh', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken }),
        });

        if (refreshRes.ok) {
          const newTokens = await refreshRes.json();
          setTokens(newTokens.accessToken, newTokens.refreshToken);

          // Retry original request with fresh access token
          headers.set('Authorization', `Bearer ${newTokens.accessToken}`);
          response = await fetch(url, { ...options, headers });
        } else {
          logout();
          throw new ApiError(401, 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
        }
      } catch (refreshErr) {
        logout();
        throw new ApiError(401, 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
      }
    } else {
      logout();
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
