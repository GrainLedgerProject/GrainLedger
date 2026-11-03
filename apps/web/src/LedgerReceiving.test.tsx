import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { LedgerWorkspace } from './LedgerWorkspace';
import { LedgerReceiving } from './LedgerReceiving';
import { type User } from './api';
const user: User = { id: 'farmer-user', email: 'farmer@example.com', displayName: 'Farmer', phoneNumber: null, role: 'CustomerStaff', organizationId: 'company-1', organizationName: 'Company', isActive: true, permissions: ['farms.read', 'farms.write'] };
const fetchMock = vi.fn<typeof fetch>();
const response = (body: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const farmer = { id: 'farmer-1', entityId: 'entity-1', name: 'Farmer One', code: null, contact: null, version: 3, createdBy: user.id, canEdit: true, canDelete: true };
beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  fetchMock.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname === '/api/auth/csrf') return response({ token: 'test-token' });
    if (url.pathname === '/api/farmers') return url.searchParams.get('search') ? response({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false }) : response({ items: [farmer], page: 1, pageSize: 25, totalCount: 1, hasMore: false });
    if (url.pathname === '/api/plots' || url.pathname === '/api/crop-seasons' || url.pathname === '/api/plot-seasons') return response({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
    if (url.pathname === '/api/farmers/farmer-1' && init?.method === 'DELETE') return response({ title: 'Farmer is referenced by a plot.' }, 409);
    if (url.pathname === '/api/farmers/farmer-1') return response(farmer);
    return response({ title: `Unexpected route ${url.pathname}` }, 404);
  });
});
it('reads own-company threshold through scoped endpoint only', async () => {
  fetchMock.mockImplementation(async input => {
    const path = new URL(String(input), 'http://localhost').pathname;
    if (path === '/api/ledger/receiving-thresholds/own-company') return response({ companyId: 'company-1', percentage: '3', source: 'COMPANY_OVERRIDE', revision: 'company-revision' });
    return response({ title: `Unexpected route ${path}` }, 404);
  });
  render(<LedgerWorkspace user={{ ...user, role: 'CustomerManager', permissions: [] }} />);
  expect(await screen.findByText('3%')).toBeInTheDocument();
  expect(screen.getByText(/Company override/)).toBeInTheDocument();
  expect(screen.getByText(/Revision company-revision/)).toBeInTheDocument();
  expect(fetchMock.mock.calls.map(([input]) => String(input))).toEqual(['/api/ledger/receiving-thresholds/own-company']);
});

it('keeps server dependency conflicts visible when deleting own source record', async () => {
  const browser = userEvent.setup();
  vi.spyOn(window, 'confirm').mockReturnValue(true);
  render(<LedgerReceiving user={user} />);
  const farmers = screen.getByRole('heading', { name: 'Farmers' }).closest('section')!;
  const archive = await within(farmers).findByRole('button', { name: 'Archive Farmer One' });
  await browser.click(archive);
  expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining('Stored record and history will be retained'));
  expect(await screen.findByRole('alert')).toHaveTextContent('Farmer is referenced by a plot.');
  expect(within(farmers).getByRole('button', { name: 'Archive Farmer One' })).toBeInTheDocument();
  expect(fetchMock.mock.calls.some(([input, init]) => String(input).includes('/api/farmers/farmer-1') && init?.method === 'DELETE' && JSON.parse(String(init.body)).version === 3)).toBe(true);
});
it('keeps selected source choice pinned after server search changes result page', async () => {
  const browser = userEvent.setup();
  render(<LedgerReceiving user={user} />);
  await browser.click(screen.getByText('Step 2 — register plot'));
  const select = await screen.findByRole('combobox', { name: 'Farmer' });
  await browser.selectOptions(select, 'farmer-1');
  await browser.type(screen.getByRole('searchbox', { name: 'Search Farmer' }), 'no match');
  expect(select).toHaveValue('farmer-1');
  expect(await screen.findByRole('option', { name: 'Farmer One' })).toBeInTheDocument();
});
