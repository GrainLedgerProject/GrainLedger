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

export class ApiError extends Error {
  constructor(public status: number, message: string, public code?: string, public errors?: Record<string, string[]>, public traceId?: string) { super(message); }
}

let authGeneration = 0;

export function clearLocalSession() {
  authGeneration++;
}

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

async function request<T>(path: string, init: RequestInit, anonymous: boolean, onResponse?: (response: Response) => void): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body) headers.set('Content-Type', 'application/json');
  const requestGeneration = authGeneration;
  if (!anonymous && init.method && !['GET', 'HEAD'].includes(init.method.toUpperCase())) {
    const csrf = await request<{ token: string }>('/api/auth/csrf', {}, true);
    if (authGeneration !== requestGeneration) throw new ApiError(401, 'Your session changed while the request was running.');
    headers.set('X-CSRF-TOKEN', csrf.token);
  }
  const response = await fetch(path, { ...init, headers, credentials: 'include' });
  if (!anonymous && authGeneration !== requestGeneration) {
    throw new ApiError(401, 'Your session changed while the request was running.');
  }
  if (!response.ok) {
    if (response.status === 401 && !anonymous) {
      clearLocalSession();
      window.dispatchEvent(new Event('session-ended'));
    }
    const error = await parseError(response);
    if (!anonymous && response.status !== 401 && authGeneration !== requestGeneration) {
      throw new ApiError(401, 'Your session changed while the request was running.');
    }
    throw error;
  }
  onResponse?.(response);
  if (response.status === 204) return undefined as T;
  const body = await response.text();
  if (!anonymous && authGeneration !== requestGeneration) throw new ApiError(401, 'Your session changed while the request was running.');
  return body ? JSON.parse(body) as T : undefined as T;
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  return request<T>(path, init, false);
}

export async function apiWithEtag<T>(path: string, init: RequestInit = {}): Promise<{ data: T; etag: string | null }> {
  let etag: string | null = null;
  const data = await request<T>(path, init, false, response => { etag = response.headers.get('ETag'); });
  return { data, etag };
}

export async function anonymousApi<T>(path: string, init: RequestInit = {}): Promise<T> {
  return request<T>(path, init, true);
}

export async function loginWithPassword(email: string, password: string): Promise<User> {
  const generation = ++authGeneration;
  const csrf = await anonymousApi<{ token: string }>('/api/auth/csrf');
  if (generation !== authGeneration) throw new ApiError(401, 'Your session changed while the request was running.');
  const user = await anonymousApi<User>('/api/auth/web-login', {
    method: 'POST', headers: { 'X-CSRF-TOKEN': csrf.token }, body: JSON.stringify({ email, password }),
  });
  if (generation !== authGeneration) throw new ApiError(401, 'Your session changed while the request was running.');
  return user;
}

export const roleLabel = (name: string | null) => name
  ? name.replace(/([a-z])([A-Z])/g, '$1 $2') : 'Awaiting assignment';
export const errorMessage = (error: unknown) => error instanceof Error ? error.message : 'Something went wrong. Please try again.';
