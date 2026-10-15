import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { LedgerSetup } from './LedgerSetup';
import { type User } from './api';

const user: User = { id: 'admin', email: 'admin@example.com', displayName: 'Admin', phoneNumber: null, role: 'Administrator', organizationId: null, organizationName: null, isActive: true, permissions: ['platform.configure'] };
const facilities = [{ id: 'facility-a', name: 'Mill A' }, { id: 'facility-b', name: 'Mill B' }];
const organizations = [{ id: 'owner-a', name: 'Owner A' }];
const fetchMock = vi.fn<typeof fetch>();
const response = (body: unknown) => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } });
beforeEach(() => {
 vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset();
 fetchMock.mockImplementation(async (url, init) => {
  const path = String(url).split('?')[0];
  if (init?.method && init.method !== 'GET') return response(path.endsWith('/evidence') ? { id: 'stored-evidence' } : {});
  if (path === '/api/auth/csrf') return response({ token: 'csrf' });
  if (path === '/api/ledger/setup') return response({ owners: [], facilities, locations: [], commodities: [], equipment: [], assignments: [], policies: [], compatibilities: [] });
  if (path === '/api/admin/users') return response([]);
  if (path === '/api/admin/organizations') return response(organizations);
  if (path === '/api/commodities' || path === '/api/measurement-units') return response({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
  return response({ title: `Unexpected route ${path}` });
 });
});
it('requires uploaded evidence for scoped balance policy and invalidates it when facility changes', async () => {
 const browser = userEvent.setup();
 render(<LedgerSetup user={user} />);
 await browser.click(await screen.findByText(/Advanced policy/, { selector: 'summary' }));
 const panel = screen.getByRole('heading', { name: 'Confirmed balance policy' }).closest('section')!;
 await browser.selectOptions(panel.querySelectorAll('select')[0], 'facility-a');
 await browser.selectOptions(panel.querySelectorAll('select')[1], 'owner-a');
 await browser.type(screen.getByLabelText('Confirmed tolerance (%)'), '0');
 await browser.click(screen.getByRole('button', { name: 'Record policy version' }));
 expect(fetchMock.mock.calls.some(([url]) => String(url) === '/api/ledger/policies')).toBe(false);
 const file = new File(['approved evidence'], 'approval.pdf', { type: 'application/pdf' });
 await browser.upload(within(panel).getByLabelText('Evidence file'), file);
 const upload = within(panel).getByRole('button', { name: 'Upload evidence' });
 await browser.click(upload);
 await waitFor(() => expect(within(panel).getByRole('link', { name: 'View stored evidence' })).toBeInTheDocument());
 await browser.click(screen.getByRole('button', { name: 'Record policy version' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url) === '/api/ledger/policies')).toBe(true));
 const write = fetchMock.mock.calls.find(([url]) => String(url) === '/api/ledger/policies')!;
 expect(JSON.parse(String(write[1]?.body))).toMatchObject({ facilityId: 'facility-a', evidenceId: 'stored-evidence', tolerancePercent: '0' });
 await browser.selectOptions(screen.getAllByLabelText('Facility for setup')[0], 'facility-b');
 expect(within(panel).getByRole('button', { name: 'Record policy version' })).toBeEnabled();
 await browser.selectOptions(within(panel).getByLabelText('Facility for policy'), 'facility-b');
 await waitFor(() => expect(within(panel).queryByRole('link', { name: 'View stored evidence' })).not.toBeInTheDocument());
 await browser.click(within(panel).getByRole('button', { name: 'Record policy version' }));
 expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/api/ledger/policies')).toHaveLength(1);
});
it('shows setup lists and performs creator-authorized edit and confirmed delete with version', async () => {
 const browser = userEvent.setup();
 const rows = [{ id: 'facility-a', name: 'Mill A', version: '7', createdBy: 'admin', canEdit: true, canDelete: true }];
 fetchMock.mockImplementation(async (url, init) => {
  const path = String(url).split('?')[0];
  if (init?.method && init.method !== 'GET') return response({});
  if (path === '/api/auth/csrf') return response({ token: 'csrf' });
  if (path === '/api/ledger/setup') return response({ owners: [], facilities: rows, locations: [], commodities: [], equipment: [], assignments: [], policies: [], compatibilities: [] });
  if (path === '/api/admin/users' || path === '/api/admin/organizations') return response([]);
  if (path === '/api/commodities' || path === '/api/measurement-units') return response({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
  return response({});
 });
 const confirmMock = vi.fn(() => true);
 vi.stubGlobal('confirm', confirmMock);
 render(<LedgerSetup user={user} />);
 await screen.findByRole('heading', { name: 'Facilities' });
 await browser.click(await screen.findByRole('button', { name: 'Edit Mill A' }));
 await browser.clear(screen.getByLabelText('Facility name'));
 await browser.type(screen.getByLabelText('Facility name'), 'Mill Updated');
 await browser.click(screen.getByRole('button', { name: 'Save facility' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => String(url) === '/api/ledger/facilities/facility-a' && init?.method === 'PUT' && JSON.parse(String(init.body)).version === '7')).toBe(true));
 await browser.click(screen.getByRole('button', { name: 'Delete Mill A' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => String(url) === '/api/ledger/facilities/facility-a' && init?.method === 'DELETE' && JSON.parse(String(init.body)).version === '7')).toBe(true));
 expect(confirmMock).toHaveBeenCalledTimes(1);
});
it('keeps scoped setup rows visible but hides unauthorized management actions', async () => {
 const readOnly: User = { ...user, permissions: [] };
 fetchMock.mockImplementation(async url => {
  const path = String(url).split('?')[0];
  if (path === '/api/ledger/setup') return response({ owners: [], facilities: [{ id: 'f', name: 'Read-only Mill', version: '1', createdBy: 'other', canEdit: false, canDelete: false }], locations: [], commodities: [], equipment: [], assignments: [], policies: [{ id: 'p', kind: 'DRYING', tolerancePercent: '0', effectiveFrom: '2026-01-01', facilityId: 'f', evidenceId: 'e', canEdit: false, canDelete: false }], compatibilities: [] });
  return response([]);
 });
 render(<LedgerSetup user={readOnly} />);
 const facilitiesTable = screen.getByRole('region', { name: 'Facilities' });
 await within(facilitiesTable).findByRole('cell', { name: 'Read-only Mill' });
 expect(within(facilitiesTable).queryByRole('button', { name: /Edit|Delete/ })).not.toBeInTheDocument();
});
