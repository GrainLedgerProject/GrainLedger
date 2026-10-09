import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { api, clearLocalSession, loginWithPassword, type User } from './api';

const user: User = { id: 'user-1', email: 'staff@example.com', displayName: 'Nguyen An', phoneNumber: null, role: null, organizationId: null, organizationName: null, isActive: true, permissions: [] };
const fetchMock = vi.fn<typeof fetch>();
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  clearLocalSession();
  window.history.replaceState({}, '', '/');
});

describe('Account experience', () => {
  it('offers email/password and Google sign-in to anonymous users', async () => {
    fetchMock.mockResolvedValue(json({}, 401));
    render(<App />);
    expect(await screen.findByRole('link', { name: /Tiếp tục với Google/ })).toHaveAttribute('href', '/api/auth/google');
    expect(screen.getByLabelText('Mật khẩu')).toHaveAttribute('autocomplete', 'current-password');
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
    expect(await screen.findByRole('link', { name: /Tiếp tục với Google/ })).toBeInTheDocument();
  });

  it('revokes all devices using a protected POST then returns to sign-in', async () => {
    fetchMock.mockImplementation(async url => {
      if (url === '/api/auth/csrf') return json({ token: 'csrf' });
      if (url === '/api/auth/logout-all') return new Response(null, { status: 204 });
      return json(user);
    });
    render(<App />);
    await userEvent.click(await screen.findByRole('button', { name: 'Sign out on all devices' }));
    expect(await screen.findByRole('link', { name: /Tiếp tục với Google/ })).toBeInTheDocument();
    expect(fetchMock.mock.calls.find(([url]) => url === '/api/auth/logout-all')?.[1]?.method).toBe('POST');
  });

  it('shows a retry option when the backend is unavailable', async () => {
    fetchMock.mockRejectedValue(new Error('Connection failed'));
    render(<App />);
    expect(await screen.findByRole('alert')).toHaveTextContent('Connection failed');
    expect(screen.getByRole('button', { name: 'Thử kết nối lại' })).toBeInTheDocument();
  });

  it('registers, keeps the OTP tied to its registration, and starts the resend cooldown', async () => {
    fetchMock.mockImplementation(async (url, init) => {
      if (url === '/api/auth/session') return json({}, 401);
      if (url === '/api/auth/register') return json({ registrationId: 'attempt-1', email: 'new@example.com', expiresIn: 600 }, 202);
      if (url === '/api/auth/verify-email') return json(user);
      return json({}, 404);
    });
    render(<App />);
    const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: 'Đăng ký' }));
    await browser.type(screen.getByLabelText('Họ và tên'), 'Nguyen An');
    await browser.type(screen.getByLabelText('Email'), 'new@example.com');
    await browser.type(screen.getByLabelText('Mật khẩu'), 'StrongPass!123');
    await browser.type(screen.getByLabelText('Nhập lại mật khẩu'), 'StrongPass!123');
    await browser.click(screen.getByRole('button', { name: 'Đăng ký' }));
    expect(await screen.findByRole('heading', { name: 'Xác minh email' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Gửi lại sau 60s/ })).toBeDisabled();
    await browser.type(screen.getByLabelText('Mã xác minh'), '123456');
    await browser.click(screen.getByRole('button', { name: 'Xác minh email' }));
    expect(await screen.findByText(/Email đã được xác minh/)).toBeInTheDocument();
    const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/auth/verify-email')!;
    expect(JSON.parse(String(init?.body))).toEqual({ email: 'new@example.com', registrationId: 'attempt-1', otp: '123456' });
  });

  it('accepts an empty 202 forgot-password response and opens reset form', async () => {
    fetchMock.mockImplementation(async url => url === '/api/auth/session' ? json({}, 401)
      : url === '/api/auth/forgot-password' ? new Response(null, { status: 202 }) : json({}, 404));
    render(<App />);
    const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: 'Quên mật khẩu?' }));
    await browser.type(screen.getByLabelText('Email'), 'member@example.com');
    await browser.click(screen.getByRole('button', { name: 'Gửi mã đặt lại' }));
    expect(await screen.findByRole('heading', { name: 'Đặt lại mật khẩu' })).toBeInTheDocument();
    expect(screen.getByText(/Nếu email thuộc tài khoản/)).toBeInTheDocument();
  });

  it('submits the reset code and new password, then returns to login', async () => {
    fetchMock.mockImplementation(async url => url === '/api/auth/session' ? json({}, 401)
      : url === '/api/auth/forgot-password' ? new Response(null, { status: 202 })
      : url === '/api/auth/reset-password' ? new Response(null, { status: 204 }) : json({}, 404));
    render(<App />);
    const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: 'Quên mật khẩu?' }));
    await browser.type(screen.getByLabelText('Email'), 'member@example.com');
    await browser.click(screen.getByRole('button', { name: 'Gửi mã đặt lại' }));
    await browser.type(await screen.findByLabelText('Mã đặt lại'), '654321');
    await browser.type(screen.getByLabelText('Mật khẩu mới'), 'ChangedPass!456');
    await browser.type(screen.getByLabelText('Nhập lại mật khẩu mới'), 'ChangedPass!456');
    await browser.click(screen.getByRole('button', { name: 'Đổi mật khẩu' }));
    expect(await screen.findByText(/Đã đổi mật khẩu/)).toBeInTheDocument();
    const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/auth/reset-password')!;
    expect(JSON.parse(String(init?.body))).toEqual({ email: 'member@example.com', otp: '654321', newPassword: 'ChangedPass!456' });
  });

  it('uses Bearer without CSRF for local profile changes and logout', async () => {
    fetchMock.mockImplementation(async (url, init) => {
      if (url === '/api/auth/session') return json({}, 401);
      if (url === '/api/auth/login') return json({ accessToken: 'local-access', refreshToken: 'local-refresh', tokenType: 'Bearer', expiresIn: 900, user });
      if (url === '/api/profile') return json({ ...user, ...JSON.parse(String(init?.body)) });
      if (url === '/api/auth/logout') return new Response(null, { status: 204 });
      return json({}, 404);
    });
    render(<App />);
    const browser = userEvent.setup();
    await browser.type(await screen.findByLabelText('Email'), 'staff@example.com');
    await browser.type(screen.getByLabelText('Mật khẩu'), 'StrongPass!123');
    await browser.click(screen.getByRole('button', { name: 'Đăng nhập' }));
    await browser.clear(await screen.findByLabelText('Full name'));
    await browser.type(screen.getByLabelText('Full name'), 'Nguyen Binh');
    await browser.click(screen.getByRole('button', { name: 'Save changes' }));
    await screen.findByText('Changes saved.');
    await browser.click(screen.getByRole('button', { name: 'Sign out' }));
    expect(await screen.findByRole('button', { name: 'Đăng nhập' })).toBeInTheDocument();
    for (const path of ['/api/profile', '/api/auth/logout']) {
      const [, init] = fetchMock.mock.calls.find(([url]) => url === path)!;
      expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer local-access');
    }
    expect(fetchMock.mock.calls.some(([url]) => url === '/api/auth/csrf')).toBe(false);
  });

  it('shows a useful email-service message for registration 503', async () => {
    fetchMock.mockImplementation(async url => url === '/api/auth/session' ? json({}, 401) : json({ title: 'Local registration is not configured.' }, 503));
    render(<App />);
    const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: 'Đăng ký' }));
    await browser.type(screen.getByLabelText('Họ và tên'), 'Nguyen An');
    await browser.type(screen.getByLabelText('Email'), 'new@example.com');
    await browser.type(screen.getByLabelText('Mật khẩu'), 'StrongPass!123');
    await browser.type(screen.getByLabelText('Nhập lại mật khẩu'), 'StrongPass!123');
    await browser.click(screen.getByRole('button', { name: 'Đăng ký' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Dịch vụ email hiện chưa sẵn sàng');
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

it('uses one refresh request for concurrent bearer failures and retries with the rotated access token', async () => {
  fetchMock.mockImplementation(async (url, init) => {
    if (url === '/api/auth/login') return json({ accessToken: 'old-access', refreshToken: 'old-refresh', tokenType: 'Bearer', expiresIn: 900, user });
    if (url === '/api/auth/refresh') return json({ accessToken: 'new-access', refreshToken: 'new-refresh', tokenType: 'Bearer', expiresIn: 900, user });
    if (url === '/resource') return new Headers(init?.headers).get('Authorization') === 'Bearer new-access' ? json({ ok: true }) : json({}, 401);
    return json({}, 404);
  });
  await loginWithPassword('staff@example.com', 'StrongPass!123');
  await expect(Promise.all([api('/resource'), api('/resource')])).resolves.toEqual([{ ok: true }, { ok: true }]);
  expect(fetchMock.mock.calls.filter(([url]) => url === '/api/auth/refresh')).toHaveLength(1);
  expect(fetchMock.mock.calls.some(([url]) => url === '/api/auth/csrf')).toBe(false);
});

it('does not let a late 401 from an old login refresh or clear a newer login', async () => {
  let finishOldRequest!: (response: Response) => void;
  const oldRequest = new Promise<Response>(resolve => { finishOldRequest = resolve; });
  let loginCount = 0;
  fetchMock.mockImplementation(async (url, init) => {
    if (url === '/api/auth/login') {
      loginCount++;
      return json({ accessToken: `access-${loginCount}`, refreshToken: `refresh-${loginCount}`, tokenType: 'Bearer', expiresIn: 900, user });
    }
    if (url === '/slow') return oldRequest;
    if (url === '/current') return new Headers(init?.headers).get('Authorization') === 'Bearer access-2' ? json({ ok: true }) : json({}, 401);
    if (url === '/api/auth/refresh') return json({}, 500);
    return json({}, 404);
  });
  await loginWithPassword('first@example.com', 'StrongPass!123');
  const stale = api('/slow');
  await loginWithPassword('second@example.com', 'StrongPass!123');
  finishOldRequest(json({}, 401));
  await expect(stale).rejects.toThrow('session changed');
  await expect(api('/current')).resolves.toEqual({ ok: true });
  expect(fetchMock.mock.calls.filter(([url]) => url === '/api/auth/refresh')).toHaveLength(0);
});

it('ignores a delayed cookie response after a local login starts', async () => {
  clearLocalSession();
  let finishCookieRequest!: (response: Response) => void;
  const cookieRequest = new Promise<Response>(resolve => { finishCookieRequest = resolve; });
  fetchMock.mockImplementation(async url => {
    if (url === '/cookie-session') return cookieRequest;
    if (url === '/api/auth/login') return json({ accessToken: 'local-access', refreshToken: 'local-refresh', tokenType: 'Bearer', expiresIn: 900, user });
    return json({}, 404);
  });
  const stale = api('/cookie-session');
  await loginWithPassword('staff@example.com', 'StrongPass!123');
  finishCookieRequest(json({ ...user, displayName: 'Old cookie user' }));
  await expect(stale).rejects.toThrow('session changed');
});
