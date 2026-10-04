import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { api, type User } from './api';

const user: User = { id: 'user-1', email: 'staff@example.com', displayName: 'Nguyen An', phoneNumber: null, role: null, organizationId: null, organizationName: null, isActive: true, permissions: [] };
const fetchMock = vi.fn<typeof fetch>();
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  window.history.replaceState({}, '', '/');
});

describe('Account experience', () => {
  it('offers Google sign-in to anonymous users', async () => {
    fetchMock.mockResolvedValue(json({}, 401));
    render(<App />);
    expect(await screen.findByRole('link', { name: /Continue with Google/ })).toHaveAttribute('href', '/api/auth/google');
    expect(screen.queryByLabelText(/password/i)).not.toBeInTheDocument();
  });

  it('shows pending access and saves a profile using CSRF protection', async () => {
    fetchMock.mockImplementation(async (url, init) => {
      if (url === '/api/auth/csrf') return json({ token: 'csrf-value' });
      if (url === '/api/profile') return json({ ...user, ...JSON.parse(String(init?.body)) });
      return json(user);
    });
    render(<App />);
    expect(await screen.findByText('Your account is ready. Workspace access is next.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Administration/ })).not.toBeInTheDocument();
    const browser = userEvent.setup();
    await browser.clear(screen.getByLabelText('Full name'));
    await browser.type(screen.getByLabelText('Full name'), 'Nguyen Binh');
    await browser.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText('Changes saved.')).toBeInTheDocument();
    const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/profile')!;
    expect(new Headers(init?.headers).get('X-CSRF-TOKEN')).toBe('csrf-value');
    expect(init?.credentials).toBe('include');
    expect(JSON.parse(String(init?.body))).toEqual({ displayName: 'Nguyen Binh', phoneNumber: null });
  });

  it('handles expired sessions during a profile save', async () => {
    fetchMock.mockImplementation(async url => {
      if (url === '/api/auth/csrf') return json({ token: 'csrf' });
      if (url === '/api/profile') return json({}, 401);
      return json(user);
    });
    render(<App />);
    await screen.findByLabelText('Full name');
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByRole('link', { name: /Continue with Google/ })).toBeInTheDocument();
  });

  it('revokes all devices using a protected POST then returns to sign-in', async () => {
    fetchMock.mockImplementation(async url => {
      if (url === '/api/auth/csrf') return json({ token: 'csrf' });
      if (url === '/api/auth/logout-all') return new Response(null, { status: 204 });
      return json(user);
    });
    render(<App />);
    await userEvent.click(await screen.findByRole('button', { name: 'Sign out on all devices' }));
    expect(await screen.findByRole('link', { name: /Continue with Google/ })).toBeInTheDocument();
    expect(fetchMock.mock.calls.find(([url]) => url === '/api/auth/logout-all')?.[1]?.method).toBe('POST');
  });

  it('shows a retry option when the backend is unavailable', async () => {
    fetchMock.mockRejectedValue(new Error('Connection failed'));
    render(<App />);
    expect(await screen.findByRole('alert')).toHaveTextContent('Connection failed');
    expect(screen.getByRole('button', { name: 'Retry connection' })).toBeInTheDocument();
  });

  it('filters roles by organization kind and submits the version for admin changes', async () => {
    const admin = { ...user, id: 'admin', role: 'Administrator', permissions: ['identity.manage'] };
    fetchMock.mockImplementation(async (url, init) => {
      if (url === '/api/auth/session') return json(admin);
      if (url === '/api/admin/users') return json([{ ...user, etag: 'version-1' }]);
      if (url === '/api/admin/organizations') return json([{ id: 'customer', name: 'Rice Export', kind: 'Customer' }, { id: 'mill', name: 'Tien Giang', kind: 'Mill' }]);
      if (url === '/api/admin/roles') return json([{ name: 'CustomerManager', organizationKind: 'Customer', permissions: [] }, { name: 'Administrator', organizationKind: 'Mill', permissions: [] }]);
      if (url === '/api/auth/csrf') return json({ token: 'csrf' });
      if (init?.method === 'PUT') return json({});
      return json({}, 404);
    });
    render(<App />);
    const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: /Administration/ }));
    await browser.click(await screen.findByRole('button', { name: 'Manage access for Nguyen An' }));
    await browser.selectOptions(screen.getByLabelText('Organization'), 'customer');
    expect(screen.queryByRole('option', { name: 'Administrator' })).not.toBeInTheDocument();
    await browser.selectOptions(screen.getByLabelText('Role'), 'CustomerManager');
    await browser.click(screen.getByRole('button', { name: 'Save access' }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url === '/api/admin/users/user-1/access')).toBe(true));
    const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/admin/users/user-1/access')!;
    expect(new Headers(init?.headers).get('If-Match')).toBe('version-1');
    expect(JSON.parse(String(init?.body))).toEqual({ role: 'CustomerManager', organizationId: 'customer', isActive: true });
  });
});

it('explains stale admin edits instead of claiming success', async () => {
  fetchMock.mockImplementation(async url => url === '/api/auth/csrf' ? json({ token: 'csrf' }) : json({}, 412));
  await expect(api('/api/admin/users/1/access', { method: 'PUT', body: '{}' })).rejects.toThrow('This account changed');
});
