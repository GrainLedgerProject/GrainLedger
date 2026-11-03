import { api, ApiError } from './api';

export type Envelope<T> = { deviceId: string; clientOperationId: string; capturedAt: string; payloadSchemaVersion: number; payload: T };

function deviceId() {
  const key = 'grainledger:capture:device-id';
  try {
    const existing = window.localStorage.getItem(key);
    if (existing) return existing;
    const created = `web-${crypto.randomUUID()}`;
    window.localStorage.setItem(key, created);
    return created;
  } catch { return `web-${crypto.randomUUID()}`; }
}

export function pendingRequest<T>(key: string): Envelope<T> | null {
  try {
    const raw = window.sessionStorage.getItem(key);
    if (!raw) return null;
    const value = JSON.parse(raw) as Envelope<T>;
    return value.deviceId && value.clientOperationId && value.payload ? value : null;
  } catch { return null; }
}

export function newRequest<T>(payload: T, payloadSchemaVersion = 1): Envelope<T> {
  return { deviceId: deviceId(), clientOperationId: crypto.randomUUID(), capturedAt: new Date().toISOString(), payloadSchemaVersion, payload };
}

export async function submitCapture<T, R>(key: string, path: string, request: Envelope<T>): Promise<R> {
  try { window.sessionStorage.setItem(key, JSON.stringify(request)); } catch { /* The current tab still holds the request for retry. */ }
  try {
    const result = await api<R>(path, { method: 'POST', body: JSON.stringify(request) });
    try { window.sessionStorage.removeItem(key); } catch { /* Storage may be disabled. */ }
    return result;
  } catch (cause) {
    if (wasRejected(cause)) {
      try { window.sessionStorage.removeItem(key); } catch { /* Storage may be disabled. */ }
    }
    throw cause;
  }
}

export function wasRejected(cause: unknown) {
  return cause instanceof ApiError && ([400, 422].includes(cause.status) ||
    cause.status === 409 && !!cause.code && cause.code !== 'OPERATION_CONFLICT');
}

export function discardPending(key: string) {
  try { window.sessionStorage.removeItem(key); } catch { /* Storage may be disabled. */ }
}
