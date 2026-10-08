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

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body) headers.set('Content-Type', 'application/json');
  if (init.method && !['GET', 'HEAD'].includes(init.method.toUpperCase())) {
    // Fetch a fresh token: anti-forgery tokens are bound to the current identity.
    const csrf = await api<{ token: string }>('/api/auth/csrf');
    headers.set('X-CSRF-TOKEN', csrf.token);
  }
  const response = await fetch(path, { ...init, headers, credentials: 'include' });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const validation = problem.errors && typeof problem.errors === 'object'
      ? Object.values(problem.errors).flat().filter(value => typeof value === 'string').join(' ') : '';
    const message = response.status === 401 ? 'Your session has ended. Please sign in again.'
      : response.status === 412 ? 'This account changed while you were editing. Reload accounts and try again.'
      : response.status === 429 ? 'Too many requests. Please wait a moment and try again.'
      : validation || problem.detail || problem.title || 'The request could not be completed. Please try again.';
    if (response.status === 401) window.dispatchEvent(new Event('session-ended'));
    throw new ApiError(response.status, message);
  }
  return response.status === 204 ? undefined as T : response.json();
}

export const roleLabel = (name: string | null) => name
  ? name.replace(/([a-z])([A-Z])/g, '$1 $2') : 'Awaiting assignment';
export const errorMessage = (error: unknown) => error instanceof Error ? error.message : 'Something went wrong. Please try again.';
