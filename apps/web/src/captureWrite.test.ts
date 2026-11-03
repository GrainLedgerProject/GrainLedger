import { beforeEach, describe, expect, it, vi } from 'vitest';
import { submitCapture, newRequest, pendingRequest, wasRejected } from './captureWrite';
import { clearLocalSession } from './api';

const fetchMock = vi.fn<typeof fetch>();
const json = (body: unknown, status: number) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  clearLocalSession();
  window.sessionStorage.clear();
});

describe('Capture request retry', () => {
  it('keeps the exact operation after an unresolved operation conflict', async () => {
    const request = newRequest({ entityId: crypto.randomUUID(), name: 'Farmer A' });
    fetchMock.mockImplementation(async url => url === '/api/auth/csrf'
      ? json({ token: 'csrf' }, 200)
      : json({ title: 'Operation conflict', code: 'OPERATION_CONFLICT' }, 409));
    await expect(submitCapture('test:conflict', '/api/farmers', request)).rejects.toMatchObject({ code: 'OPERATION_CONFLICT' });
    expect(pendingRequest('test:conflict')).toEqual(request);
  });

  it('clears a request after a confirmed validation rejection', async () => {
    const request = newRequest({ entityId: crypto.randomUUID(), name: '' });
    fetchMock.mockImplementation(async url => url === '/api/auth/csrf'
      ? json({ token: 'csrf' }, 200)
      : json({ title: 'Validation failed', errors: { name: ['Name is required'] } }, 400));
    let caught: unknown;
    try { await submitCapture('test:rejected', '/api/farmers', request); } catch (cause) { caught = cause; }
    expect(wasRejected(caught)).toBe(true);
    expect(pendingRequest('test:rejected')).toBeNull();
  });
});
