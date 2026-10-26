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
  window.sessionStorage.clear();
  window.localStorage.clear();
  window.history.replaceState({}, '', '/');
});

describe('Account experience', () => {
  it('opens company farmers, searches, and reads a farmer without exposing the menu to unassigned users', async () => {
    const staff = { ...user, role: 'PurchasingStaff', organizationId: 'company-1', organizationName: 'Customer A', permissions: ['farms.write'] };
    const farmer = { id: '11111111-1111-4111-8111-111111111111', code: 'ND-111111111111', name: 'Nguyen Van A', contact: '0900000000', createdAt: '2026-10-10T00:00:00Z' };
    fetchMock.mockImplementation(async url => {
      if (url === '/api/auth/session') return json(staff);
      if (url === `/api/farmers/${farmer.id}`) return json(farmer);
      if (url === '/api/plots/plot-1') return json({ id: 'plot-1', farmerId: farmer.id, code: 'TR-001', areaHa: 1.25, geometry: { type: 'Point', coordinates: [106.35, 10.4] } });
      if (url === '/api/plot-seasons/plot-season-1') return json({ id: 'plot-season-1', plotId: 'plot-1', seasonId: 'season-1', riceVariety: 'ST25' });
      if (String(url).startsWith('/api/plot-seasons?')) return json({ items: [{ id: 'plot-season-1', plotId: 'plot-1', seasonId: 'season-1', riceVariety: 'ST25' }], page: 1, pageSize: 10, hasMore: false });
      if (String(url).startsWith('/api/crop-seasons?')) return json({ items: [{ id: 'season-1', companyId: 'company-1', name: 'Đông Xuân 2026–2027', startsOn: '2026-11-01', endsOn: '2027-03-31' }], page: 1, pageSize: 100, hasMore: false });
      if (String(url).startsWith('/api/plots?')) return json({ items: [{ id: 'plot-1', farmerId: farmer.id, code: 'TR-001', areaHa: 1.25 }], page: 1, pageSize: 10, hasMore: false });
      if (String(url).startsWith('/api/farmers?')) return json({ items: [farmer], page: 1, pageSize: 20, hasMore: false });
      return json({}, 404);
    });
    render(<App />);
    const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: 'Farmers' }));
    expect(await screen.findByRole('button', { name: farmer.name })).toBeInTheDocument();
    await browser.type(screen.getByLabelText('Search by name or code'), 'Nguyen');
    await browser.click(screen.getByRole('button', { name: 'Search' }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).includes('q=Nguyen'))).toBe(true));
    await browser.click(screen.getByRole('button', { name: farmer.name }));
    expect(await screen.findByText(farmer.contact)).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /TR-001/ })).toBeInTheDocument();
    expect(screen.getByText('1.25 ha')).toBeInTheDocument();
    await browser.click(screen.getByRole('button', { name: /TR-001/ }));
    expect(await screen.findByRole('heading', { name: 'TR-001' })).toBeInTheDocument();
    expect(screen.getByText('106.35')).toBeInTheDocument();
    expect(window.location.pathname).toBe('/capture/plots/plot-1');
    await browser.click(await screen.findByRole('button', { name: /Đông Xuân 2026–2027/ }));
    expect(await screen.findByRole('heading', { name: 'Đông Xuân 2026–2027' })).toBeInTheDocument();
    expect(screen.getByText('ST25')).toBeInTheDocument();
    expect(window.location.pathname).toBe('/capture/plot-seasons/plot-season-1');
    await browser.click(screen.getByRole('button', { name: /Back to plot/ }));
    expect(await screen.findByRole('heading', { name: 'TR-001' })).toBeInTheDocument();
    await browser.click(screen.getByRole('button', { name: /Back to farmer/ }));
    expect(await screen.findByRole('button', { name: /TR-001/ })).toBeInTheDocument();
    expect(window.location.pathname).toBe(`/capture/farmers/${farmer.id}`);
    await browser.click(screen.getByRole('button', { name: /Back to farmers/ }));
    expect(window.location.pathname).toBe('/capture/farmers');
  });

  it('registers a farmer and retries the exact same request after a lost response', async () => {
    const staff = { ...user, role: 'PurchasingStaff', organizationId: 'company-1', organizationName: 'Customer A', permissions: ['farms.write'] };
    let attempts = 0;
    fetchMock.mockImplementation(async url => {
      if (url === '/api/auth/session') return json(staff);
      if (url === '/api/auth/csrf') return json({ token: 'csrf-value' });
      if (url === '/api/farmers') {
        attempts++;
        if (attempts === 1) throw new Error('Connection interrupted');
        const body = JSON.parse(String(fetchMock.mock.calls.filter(([path]) => path === '/api/farmers')[0][1]?.body));
        return json({ id: body.payload.entityId, code: 'ND-TEST', name: body.payload.name, contact: body.payload.contact, createdAt: '2026-10-14T00:00:00Z' }, 201);
      }
      if (String(url).startsWith('/api/farmers/')) return json({ id: String(url).split('/').at(-1), code: 'ND-TEST', name: 'Nguyễn Văn C', contact: '0900000000', createdAt: '2026-10-14T00:00:00Z' });
      if (String(url).startsWith('/api/plots?')) return json({ items: [], page: 1, pageSize: 10, hasMore: false });
      if (String(url).startsWith('/api/farmers?')) return json({ items: [], page: 1, pageSize: 20, hasMore: false });
      return json({}, 404);
    });
    render(<App />);
    const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: 'Farmers' }));
    await browser.click(await screen.findByRole('button', { name: /Add farmer/ }));
    expect(screen.getByRole('dialog', { name: 'Add farmer' })).toBeInTheDocument();
    expect(window.location.pathname).toBe('/capture/farmers');
    await browser.type(screen.getByLabelText(/Full name/), 'Nguyễn Văn C');
    await browser.type(screen.getByLabelText(/Contact/), '0900000000');
    await browser.click(screen.getByRole('button', { name: 'Save farmer' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Connection interrupted');
    await browser.click(screen.getByRole('button', { name: 'Retry save' }));
    expect(await screen.findByRole('heading', { name: 'Nguyễn Văn C' })).toBeInTheDocument();
    const posts = fetchMock.mock.calls.filter(([url]) => url === '/api/farmers');
    expect(posts).toHaveLength(2);
    expect(posts[0][1]?.body).toBe(posts[1][1]?.body);
    const submitted = JSON.parse(String(posts[0][1]?.body));
    expect(submitted.payload).toMatchObject({ name: 'Nguyễn Văn C', contact: '0900000000' });
    expect(submitted.payload.code).toBeUndefined();
    expect(submitted.clientOperationId).toBeTruthy();
    expect(submitted.payload.entityId).toBeTruthy();
  });

  it('registers a point plot for the selected farmer and retries one operation after a lost response', async () => {
    const staff = { ...user, role: 'PurchasingStaff', organizationId: 'company-1', organizationName: 'Customer A', permissions: ['farms.write'] };
    const farmer = { id: '11111111-1111-4111-8111-111111111111', code: 'ND-001', name: 'Nguyen Van A', contact: null, createdAt: '2026-10-10T00:00:00Z' };
    let attempts = 0;
    let rejectFirst: ((reason?: unknown) => void) | undefined;
    fetchMock.mockImplementation(async (url, init) => {
      if (url === '/api/auth/session') return json(staff);
      if (url === '/api/auth/csrf') return json({ token: 'csrf-value' });
      if (url === '/api/plots') {
        attempts++;
        if (attempts === 1) return new Promise<Response>((_resolve, reject) => { rejectFirst = reject; });
        const request = JSON.parse(String(init?.body));
        return json({ id: request.payload.entityId, farmerId: farmer.id, code: 'TR-TEST', areaHa: 1.25, geometry: request.payload.geometry }, 201);
      }
      if (url === '/api/plots/duplicate-check') return json({ items: [], hasMore: false });
      if (String(url).startsWith('/api/plots/')) return json({ id: String(url).split('/').at(-1), farmerId: farmer.id, code: 'TR-TEST', areaHa: 1.25, geometry: { type: 'Point', coordinates: [106.35, 10.4] } });
      if (String(url).startsWith('/api/plot-seasons?')) return json({ items: [], page: 1, pageSize: 10, hasMore: false });
      if (String(url).startsWith('/api/crop-seasons?')) return json({ items: [], page: 1, pageSize: 100, hasMore: false });
      if (String(url).startsWith('/api/plots?')) return json({ items: [], page: 1, pageSize: 10, hasMore: false });
      if (url === `/api/farmers/${farmer.id}`) return json(farmer);
      if (String(url).startsWith('/api/farmers?')) return json({ items: [farmer], page: 1, pageSize: 20, hasMore: false });
      return json({}, 404);
    });
    render(<App />);
    const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: 'Farmers' }));
    await browser.click(await screen.findByRole('button', { name: farmer.name }));
    await browser.click(await screen.findByRole('button', { name: /Add plot/ }));
    expect(screen.getByRole('dialog', { name: 'Add plot' })).toBeInTheDocument();
    expect(window.location.pathname).toBe(`/capture/farmers/${farmer.id}`);
    await browser.type(screen.getByLabelText(/Longitude/), '106.35');
    await browser.type(screen.getByLabelText(/Latitude/), '10.4');
    await browser.type(screen.getByLabelText(/Area \(hectares\)/), '1.25');
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url === '/api/plots/duplicate-check')).toBe(true));
    await browser.click(screen.getByRole('button', { name: 'Save plot' }));
    expect(await screen.findByRole('button', { name: 'Saving…' })).toBeDisabled();
    await waitFor(() => expect(rejectFirst).toBeDefined());
    expect(screen.queryByText(/The previous save did not return a result/)).not.toBeInTheDocument();
    rejectFirst?.(new Error('Connection interrupted'));
    expect(await screen.findByRole('alert')).toHaveTextContent('Connection interrupted');
    expect(screen.getByText(/The previous save did not return a result/)).toBeInTheDocument();
    await browser.click(screen.getByRole('button', { name: 'Retry save' }));
    expect(await screen.findByRole('heading', { name: 'TR-TEST' })).toBeInTheDocument();
    const posts = fetchMock.mock.calls.filter(([url]) => url === '/api/plots');
    expect(posts).toHaveLength(2);
    expect(posts[0][1]?.body).toBe(posts[1][1]?.body);
    const submitted = JSON.parse(String(posts[0][1]?.body));
    expect(submitted.payload).toMatchObject({ farmerId: farmer.id, geometry: { type: 'Point', coordinates: [106.35, 10.4] }, areaHa: 1.25 });
    expect(submitted.payload).not.toHaveProperty('code');
    expect(submitted.payloadSchemaVersion).toBe(2);
    expect(submitted.payload.entityId).toBeTruthy();
    expect(submitted.clientOperationId).toBeTruthy();
  });

  it('registers an existing company crop season for a plot and retries the same request', async () => {
    const plotId = '11408862-f173-49d8-9f02-fe57483bd6e6';
    const farmerId = '6114dc9c-f08c-4b25-a90f-91c7c88a05b0';
    const seasonId = '81504cfe-cf51-4bce-9eb2-44159e97fb03';
    const recordId = '06d238e3-d388-4a91-b142-24b90b5dba25';
    const staff = { ...user, role: 'PurchasingStaff', organizationId: 'company-1', organizationName: 'Customer A', permissions: ['farms.write'] };
    let attempts = 0;
    fetchMock.mockImplementation(async (url, init) => {
      if (url === '/api/auth/session') return json(staff);
      if (url === '/api/auth/csrf') return json({ token: 'csrf-value' });
      if (url === `/api/plots/${plotId}`) return json({ id: plotId, farmerId, code: 'TR-001', areaHa: 1.25, geometry: { type: 'Point', coordinates: [106.35, 10.4] } });
      if (url === `/api/farmers/${farmerId}`) return json({ id: farmerId, code: 'ND-001', name: 'Nguyen Van A' });
      if (url === `/api/plot-seasons/${recordId}`) return json({ id: recordId, plotId, seasonId, riceVariety: 'ST25' });
      if (url === '/api/plot-seasons') {
        attempts++;
        if (attempts === 1) throw new Error('Connection interrupted');
        return json({ id: recordId, plotId, seasonId, riceVariety: 'ST25' }, 201);
      }
      if (String(url).startsWith('/api/plot-seasons?')) return json({ items: [], page: 1, pageSize: 10, hasMore: false });
      if (String(url).startsWith('/api/crop-seasons?page=1')) return json({ items: [{ id: 'other-season', companyId: 'company-1', name: 'Hè Thu', startsOn: '2026-04-01', endsOn: '2026-08-31' }], page: 1, pageSize: 100, hasMore: true });
      if (String(url).startsWith('/api/crop-seasons?page=2')) return json({ items: [{ id: seasonId, companyId: 'company-1', name: 'Đông Xuân', startsOn: '2026-11-01', endsOn: '2027-03-31' }], page: 2, pageSize: 100, hasMore: false });
      return json({}, 404);
    });
    window.history.replaceState({}, '', `/capture/plots/${plotId}`);
    render(<App />);
    const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: /Register season/ }));
    expect(screen.getByRole('dialog', { name: 'Register crop season' })).toBeInTheDocument();
    await browser.selectOptions(await screen.findByLabelText(/Crop season/), seasonId);
    await browser.type(screen.getByLabelText(/Rice variety/), 'ST25');
    await browser.click(screen.getByRole('button', { name: 'Register season' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Connection interrupted');
    await browser.click(screen.getByRole('button', { name: 'Retry save' }));
    expect(await screen.findByRole('heading', { name: 'Đông Xuân' })).toBeInTheDocument();
    expect(screen.getByText('ST25')).toBeInTheDocument();
    expect(window.location.pathname).toBe(`/capture/plot-seasons/${recordId}`);
    const posts = fetchMock.mock.calls.filter(([url]) => url === '/api/plot-seasons');
    expect(posts).toHaveLength(2);
    expect(posts[0][1]?.body).toBe(posts[1][1]?.body);
    const submitted = JSON.parse(String(posts[0][1]?.body));
    expect(submitted.payload).toMatchObject({ plotId, seasonId, riceVariety: 'ST25' });
    expect(submitted.payload.entityId).toBeTruthy();
    expect(submitted.clientOperationId).toBeTruthy();
  });

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
    expect(screen.queryByRole('button', { name: 'Farmers' })).not.toBeInTheDocument();
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

  it('uses cookie and CSRF for password login, profile changes and logout', async () => {
    fetchMock.mockImplementation(async (url, init) => {
      if (url === '/api/auth/session') return json({}, 401);
      if (url === '/api/auth/csrf') return json({ token: 'csrf' });
      if (url === '/api/auth/web-login') return json(user);
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
    for (const path of ['/api/auth/web-login', '/api/profile', '/api/auth/logout']) {
      const [, init] = fetchMock.mock.calls.find(([url]) => url === path)!;
      expect(new Headers(init?.headers).get('Authorization')).toBeNull();
      expect(new Headers(init?.headers).get('X-CSRF-TOKEN')).toBe('csrf');
      expect(init?.credentials).toBe('include');
    }
    expect(fetchMock.mock.calls.some(([url]) => url === '/api/auth/csrf')).toBe(true);
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

it('restores a password session after remount and stays signed out after logout and remount', async () => {
  let signedIn = false;
  fetchMock.mockImplementation(async url => {
    if (url === '/api/auth/csrf') return json({ token: 'csrf' });
    if (url === '/api/auth/web-login') { signedIn = true; return json(user); }
    if (url === '/api/auth/session') return signedIn ? json(user) : json({}, 401);
    if (url === '/api/auth/logout') { signedIn = false; return new Response(null, { status: 204 }); }
    return json({}, 404);
  });
  await loginWithPassword('staff@example.com', 'StrongPass!123');
  clearLocalSession(); // Simulate losing all JS session state on reload; the browser retains its cookie.
  const first = render(<App />);
  expect(await screen.findByLabelText('Full name')).toHaveValue('Nguyen An');
  first.unmount();
  const reloaded = render(<App />);
  await userEvent.click(await screen.findByRole('button', { name: 'Sign out' }));
  await screen.findByRole('button', { name: 'Đăng nhập' });
  reloaded.unmount();
  render(<App />);
  expect(await screen.findByRole('button', { name: 'Đăng nhập' })).toBeInTheDocument();
  expect(fetchMock.mock.calls.some(([url]) => url === '/api/auth/refresh')).toBe(false);
  for (const [, init] of fetchMock.mock.calls) expect(new Headers(init?.headers).has('Authorization')).toBe(false);
});

it('does not let a late 401 from an old session clear a newer login', async () => {
  let finishOldRequest!: (response: Response) => void;
  const oldRequest = new Promise<Response>(resolve => { finishOldRequest = resolve; });
  fetchMock.mockImplementation(async url => {
    if (url === '/api/auth/csrf') return json({ token: 'csrf' });
    if (url === '/api/auth/web-login') return json(user);
    if (url === '/slow') return oldRequest;
    if (url === '/current') return json({ ok: true });
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
    if (url === '/api/auth/csrf') return json({ token: 'csrf' });
    if (url === '/api/auth/web-login') return json(user);
    return json({}, 404);
  });
  const stale = api('/cookie-session');
  await loginWithPassword('staff@example.com', 'StrongPass!123');
  finishCookieRequest(json({ ...user, displayName: 'Old cookie user' }));
  await expect(stale).rejects.toThrow('session changed');
});
