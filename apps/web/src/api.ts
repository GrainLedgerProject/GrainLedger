export type User = {
  id: string;
  email: string;
  displayName: string;
  phoneNumber: string | null;
  role: string | null;
  organizationId: string | null;
  organizationName: string | null;
  isActive: boolean;
  permissions: string[];
  etag?: string;
};
export type Organization = { id: string; name: string; kind: 'Mill' | 'Customer' };
export type Role = { name: string; organizationKind: Organization['kind']; permissions: string[] };
export type RegistrationPending = { registrationId: string; email: string; expiresIn: number };
export type IssuedTokens = { accessToken: string; refreshToken: string; tokenType: 'Bearer'; expiresIn: number; user: User };

export class ApiError extends Error {
  constructor(public status: number, message: string, public code?: string, public errors?: Record<string, string[]>, public traceId?: string) { super(message); }
}

let accessToken: string | null = null;
let refreshToken: string | null = null;
let refreshPromise: Promise<boolean> | null = null;
let authGeneration = 0;

function rememberTokens(tokens: IssuedTokens, startsNewSession = false) {
  if (startsNewSession) {
    authGeneration++;
    refreshPromise = null;
  }
  accessToken = tokens.accessToken;
  refreshToken = tokens.refreshToken;
}

export function clearLocalSession() {
  authGeneration++;
  refreshPromise = null;
  accessToken = null;
  refreshToken = null;
}

export const hasLocalSession = () => accessToken !== null && refreshToken !== null;

async function parseError(response: Response): Promise<ApiError> {
  const problem = await response.json().catch(() => ({}));
  const validation = problem.errors && typeof problem.errors === 'object'
    ? Object.values(problem.errors).flat().filter(value => typeof value === 'string').join(' ') : '';
  const message = response.status === 401 ? 'Your session has ended. Please sign in again.'
    : response.status === 412 ? 'This account changed while you were editing. Reload accounts and try again.'
    : response.status === 429 ? 'Too many requests. Please wait a moment and try again.'
    : validation || problem.detail || problem.title || 'The request could not be completed. Please try again.';
  return new ApiError(response.status, message, problem.code, problem.errors, problem.traceId);
}

async function rotateTokens(failedAccessToken: string, requestGeneration: number): Promise<boolean> {
  if (authGeneration !== requestGeneration) return false;
  if (accessToken && accessToken !== failedAccessToken) return true;
  if (!refreshToken) return false;
  if (!refreshPromise) {
    const tokenToRotate = refreshToken;
    const generationToRotate = authGeneration;
    let pending!: Promise<boolean>;
    pending = (async () => {
      try {
        const response = await fetch('/api/auth/refresh', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken: tokenToRotate }), credentials: 'include',
        });
        if (!response.ok) return false;
        const tokens = await response.json() as IssuedTokens;
        if (authGeneration !== generationToRotate || refreshToken !== tokenToRotate) return false;
        rememberTokens(tokens);
        return true;
      } catch { return false; }
      finally { if (refreshPromise === pending) refreshPromise = null; }
    })();
    refreshPromise = pending;
  }
  return refreshPromise;
}

async function request<T>(path: string, init: RequestInit, anonymous: boolean, allowRefresh: boolean): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body) headers.set('Content-Type', 'application/json');
  const bearer = !anonymous ? accessToken : null;
  const requestGeneration = authGeneration;
  if (bearer) headers.set('Authorization', `Bearer ${bearer}`);
  if (!anonymous && !bearer && init.method && !['GET', 'HEAD'].includes(init.method.toUpperCase())) {
    const csrf = await request<{ token: string }>('/api/auth/csrf', {}, false, false);
    if (authGeneration !== requestGeneration) throw new ApiError(401, 'Your session changed while the request was running.');
    headers.set('X-CSRF-TOKEN', csrf.token);
  }
  const response = await fetch(path, { ...init, headers, credentials: 'include' });
  if (!anonymous && authGeneration !== requestGeneration) {
    throw new ApiError(401, 'Your session changed while the request was running.');
  }
  if (response.status === 401 && bearer && allowRefresh && await rotateTokens(bearer, requestGeneration)) {
    if (authGeneration !== requestGeneration) throw new ApiError(401, 'Your session changed while the request was running.');
    return request<T>(path, init, false, false);
  }
  if (!response.ok) {
    if (response.status === 401 && !anonymous) {
      if (!bearer || authGeneration === requestGeneration) {
        clearLocalSession();
        window.dispatchEvent(new Event('session-ended'));
      }
    }
    const error = await parseError(response);
    if (!anonymous && response.status !== 401 && authGeneration !== requestGeneration) {
      throw new ApiError(401, 'Your session changed while the request was running.');
    }
    throw error;
  }
  if (response.status === 204) return undefined as T;
  const body = await response.text();
  if (!anonymous && authGeneration !== requestGeneration) throw new ApiError(401, 'Your session changed while the request was running.');
  return body ? JSON.parse(body) as T : undefined as T;
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  return request<T>(path, init, false, true);
}

export async function anonymousApi<T>(path: string, init: RequestInit = {}): Promise<T> {
  return request<T>(path, init, true, false);
}

export async function loginWithPassword(email: string, password: string): Promise<User> {
  const tokens = await anonymousApi<IssuedTokens>('/api/auth/login', {
    method: 'POST', body: JSON.stringify({ email, password }),
  });
  rememberTokens(tokens, true);
  return tokens.user;
}

export const roleLabel = (name: string | null) => name
  ? name.replace(/([a-z])([A-Z])/g, '$1 $2') : 'Awaiting assignment';
export const errorMessage = (error: unknown) => error instanceof Error ? error.message : 'Something went wrong. Please try again.';
