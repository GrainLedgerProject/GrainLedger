import { act, render, screen, waitFor, within } from '@testing-library/react';
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
  if (path === '/api/ledger/setup') return response({ owners: organizations, facilities, locations: [], commodities: [], equipment: [], assignments: [], policies: [], compatibilities: [] });
  if (path === '/api/ledger/receiving-thresholds') return response({ systemDefault: { percentage: '2', version: '00000000-0000-4000-8000-000000000001' }, companyOverrides: [] });
  if (path === '/api/ledger/receiving-thresholds/companies' || path === '/api/ledger/receiving-thresholds/history') return response({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false });
  if (path === '/api/admin/users') return response([]);
  if (path === '/api/admin/organizations') return response(organizations);
  if (path === '/api/commodities' || path === '/api/measurement-units') return response({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
  return response({ title: `Unexpected route ${path}` });
 });
});
it('requires uploaded evidence for scoped balance policy and invalidates it when facility changes', async () => {
 const browser = userEvent.setup();
 const originalFetch = fetchMock.getMockImplementation()!;
 fetchMock.mockImplementation(async (input, init) => {
  const path = String(input).split('?')[0];
  if (path === '/api/ledger/receiving-thresholds') return response({ systemDefault: { percentage: '2', version: '00000000-0000-4000-8000-000000000001' }, companyOverrides: [] });
  if (path === '/api/ledger/receiving-thresholds/companies' || path === '/api/ledger/receiving-thresholds/history') return response({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false });
  return originalFetch(input, init);
 });
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
  if (path === '/api/ledger/receiving-thresholds') return response({ systemDefault: { percentage: '2', version: '00000000-0000-4000-8000-000000000001' }, companyOverrides: [] });
  if (path === '/api/ledger/receiving-thresholds/companies' || path === '/api/ledger/receiving-thresholds/history') return response({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false });
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
 expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/receiving-thresholds'))).toBe(false);
});
it('edits default and company override with reasons and revisions, then removes override while retaining history', async () => {
 const browser = userEvent.setup();
 let settings = { systemDefault: { percentage: '2', version: '00000000-0000-4000-8000-000000000001' }, companyOverrides: [] as { companyId: string; companyName: string; percentage: string; version: string }[] };
 const history: Record<string, unknown>[] = [{ revision: settings.systemDefault.version, companyId: null, companyName: null, scope: 'SYSTEM_DEFAULT', action: 'INITIALIZE', oldPercentage: null, newPercentage: '2', actorId: null, actorName: null, recordedAt: '2026-10-01T00:00:00Z', reason: 'Initial migration default', migrationId: 'receiving-thresholds-v1' }];
 fetchMock.mockImplementation(async (input, init) => {
  const url = new URL(String(input), 'http://localhost'), path = url.pathname;
  if (path === '/api/auth/csrf') return response({ token: 'csrf' });
  if (path === '/api/ledger/setup') return response({ owners: [], facilities, locations: [], commodities: [], equipment: [], assignments: [], policies: [], compatibilities: [] });
  if (path === '/api/admin/users') return response([]);
  if (path === '/api/admin/organizations') return response(organizations);
  if (path === '/api/commodities' || path === '/api/measurement-units') return response({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
  if (path === '/api/ledger/receiving-thresholds' && !init?.method) return response(settings);
  if (path === '/api/ledger/receiving-thresholds/companies') return response({ items: [{ id: 'owner-a', name: 'Owner A' }], page: 1, pageSize: 20, totalCount: 1, hasMore: false });
  if (path === '/api/ledger/receiving-thresholds/history') return response({ items: history, page: 1, pageSize: 20, totalCount: history.length, hasMore: false });
  if (path === '/api/ledger/receiving-thresholds/default' && init?.method === 'PUT') {
   const body = JSON.parse(String(init.body)); expect(body).toMatchObject({ percentage: '3', version: settings.systemDefault.version, reason: 'Approved policy update' });
   settings = { ...settings, systemDefault: { percentage: '3', version: '00000000-0000-4000-8000-000000000002' } };
   history.push({ revision: settings.systemDefault.version, companyId: null, companyName: null, scope: 'SYSTEM_DEFAULT', action: 'UPDATE', oldPercentage: '2', newPercentage: '3', actorId: 'admin', actorName: 'Admin', recordedAt: '2026-10-02T00:00:00Z', reason: body.reason, migrationId: null });
   return response(settings);
  }
  if (path.endsWith('/companies/owner-a') && init?.method === 'POST') {
   const body = JSON.parse(String(init.body)); expect(body).toEqual({ percentage: '0', reason: 'Exact-match agreement' });
   settings.companyOverrides = [{ companyId: 'owner-a', companyName: 'Owner A', percentage: '0', version: '00000000-0000-4000-8000-000000000003' }];
   history.push({ revision: settings.companyOverrides[0].version, companyId: 'owner-a', companyName: 'Owner A', scope: 'COMPANY_OVERRIDE', action: 'CREATE', oldPercentage: null, newPercentage: '0', actorId: 'admin', actorName: 'Admin', recordedAt: '2026-10-03T00:00:00Z', reason: body.reason, migrationId: null });
   return response(settings);
  }
  if (path.endsWith('/companies/owner-a') && init?.method === 'PUT') {
   const body = JSON.parse(String(init.body)); expect(body).toEqual({ percentage: '0.25', version: '00000000-0000-4000-8000-000000000003', reason: 'Updated company agreement' });
   settings.companyOverrides = [{ companyId: 'owner-a', companyName: 'Owner A', percentage: '0.25', version: '00000000-0000-4000-8000-000000000004' }];
   history.push({ revision: settings.companyOverrides[0].version, companyId: 'owner-a', companyName: 'Owner A', scope: 'COMPANY_OVERRIDE', action: 'UPDATE', oldPercentage: '0', newPercentage: '0.25', actorId: 'admin', actorName: 'Admin', recordedAt: '2026-10-04T00:00:00Z', reason: body.reason, migrationId: null });
   return response(settings);
  }
  if (path.endsWith('/companies/owner-a') && init?.method === 'DELETE') {
   const body = JSON.parse(String(init.body)); expect(body).toEqual({ version: '00000000-0000-4000-8000-000000000004', reason: 'Agreement ended' });
   history.push({ revision: '00000000-0000-4000-8000-000000000005', companyId: 'owner-a', companyName: 'Owner A', scope: 'COMPANY_OVERRIDE', action: 'REMOVE', oldPercentage: '0.25', newPercentage: null, actorId: 'admin', actorName: 'Admin', recordedAt: '2026-10-05T00:00:00Z', reason: body.reason, migrationId: null });
   settings = { ...settings, companyOverrides: [] };
   return response(settings);
  }
  return response({ title: `Unexpected route ${path}` });
 });
 vi.stubGlobal('confirm', vi.fn(() => true));
 render(<LedgerSetup user={user} />);
 await browser.click(await screen.findByText('Advanced setup — Receiving loss thresholds', { selector: 'summary' }));
 await browser.clear(await screen.findByLabelText('Default threshold (%)'));
 await browser.type(screen.getByLabelText('Default threshold (%)'), '3');
 await browser.type(screen.getByLabelText('Reason for change').closest('form')!.querySelector('textarea')!, 'Approved policy update');
 await browser.click(screen.getByRole('button', { name: 'Save system default' }));
 await screen.findByText('System default updated.');
 const defaultWrite = fetchMock.mock.calls.find(([input, init]) => String(input).endsWith('/receiving-thresholds/default') && init?.method === 'PUT');
 expect(defaultWrite).toBeDefined();
 await browser.selectOptions(screen.getByLabelText('Customer company'), 'owner-a');
 await browser.clear(screen.getByLabelText('Threshold for selected company (%)'));
 await browser.type(screen.getByLabelText('Threshold for selected company (%)'), '0');
 const reasons = screen.getAllByLabelText('Reason for change');
 await browser.type(reasons[1], 'Exact-match agreement');
 await browser.click(screen.getByRole('button', { name: 'Create company override' }));
 await screen.findByRole('cell', { name: '0%' });
 expect(fetchMock.mock.calls.some(([input, init]) => String(input).endsWith('/companies/owner-a') && init?.method === 'POST')).toBe(true);
 await browser.clear(screen.getByLabelText('Threshold for selected company (%)'));
 await browser.type(screen.getByLabelText('Threshold for selected company (%)'), '0.25');
 await browser.clear(screen.getAllByLabelText('Reason for change')[1]);
 await browser.type(screen.getAllByLabelText('Reason for change')[1], 'Updated company agreement');
 await browser.click(screen.getByRole('button', { name: 'Update company override' }));
 await screen.findByRole('cell', { name: '0.25%' });
 expect(fetchMock.mock.calls.some(([input, init]) => String(input).endsWith('/companies/owner-a') && init?.method === 'PUT' && JSON.parse(String(init.body)).version === '00000000-0000-4000-8000-000000000003')).toBe(true);
 await browser.clear(screen.getAllByLabelText('Reason for change')[1]);
 await browser.type(screen.getAllByLabelText('Reason for change')[1], 'Agreement ended');
 await browser.click(screen.getByRole('button', { name: 'Remove override' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([input, init]) => String(input).endsWith('/companies/owner-a') && init?.method === 'DELETE')).toBe(true));
 await screen.findByText('Initial migration default');
 expect(screen.getByText('Agreement ended')).toBeInTheDocument();
 expect(screen.getAllByText('Admin').length).toBe(4);
 expect(screen.getByText('Migration initialization')).toBeInTheDocument();
 expect(history).toHaveLength(5);
});
it('keeps newest history filter results when older response arrives last', async () => {
 const browser = userEvent.setup();
 const pending: Record<string, (result: Response) => void> = {};
 const item = (scope: string, reason: string) => ({ revision: `${scope}-revision`, companyId: scope === 'COMPANY_OVERRIDE' ? 'owner-a' : null, companyName: scope === 'COMPANY_OVERRIDE' ? 'Owner A' : null, scope, action: 'UPDATE', oldPercentage: '2', newPercentage: '3', actorId: 'admin', actorName: 'Admin', recordedAt: '2026-10-05T00:00:00Z', reason, migrationId: null });
 const page = (items: unknown[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length, hasMore: false });
 fetchMock.mockImplementation(async input => {
  const url = new URL(String(input), 'http://localhost'), path = url.pathname;
  if (path === '/api/auth/csrf') return response({ token: 'csrf' });
  if (path === '/api/ledger/setup') return response({ owners: [], facilities, locations: [], commodities: [], equipment: [], assignments: [], policies: [], compatibilities: [] });
  if (path === '/api/admin/users') return response([]);
  if (path === '/api/admin/organizations') return response(organizations);
  if (path === '/api/commodities' || path === '/api/measurement-units') return response({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
  if (path === '/api/ledger/receiving-thresholds') return response({ systemDefault: { percentage: '2', version: '00000000-0000-4000-8000-000000000001' }, companyOverrides: [] });
  if (path === '/api/ledger/receiving-thresholds/companies') return response(page([{ id: 'owner-a', name: 'Owner A' }]));
  if (path === '/api/ledger/receiving-thresholds/history') {
   const scope = url.searchParams.get('scope') || '';
   if (!scope) return response(page([item('SYSTEM_DEFAULT', 'Initial unfiltered')]));
   let resolveResponse!: (result: Response) => void;
   const pendingResponse = new Promise<Response>(resolve => { resolveResponse = resolve; });
   pending[scope] = resolveResponse;
   return pendingResponse;
  }
  return response({ title: `Unexpected route ${path}` });
 });
 render(<LedgerSetup user={user} />);
 await browser.click(await screen.findByText('Advanced setup — Receiving loss thresholds', { selector: 'summary' }));
 await screen.findByText('Initial unfiltered');
 await browser.selectOptions(screen.getByLabelText('Filter by scope'), 'SYSTEM_DEFAULT');
 await waitFor(() => expect(pending.SYSTEM_DEFAULT).toBeDefined());
 await browser.selectOptions(screen.getByLabelText('Filter by scope'), 'COMPANY_OVERRIDE');
 await waitFor(() => expect(pending.COMPANY_OVERRIDE).toBeDefined());
 await act(async () => { pending.COMPANY_OVERRIDE(response(page([item('COMPANY_OVERRIDE', 'Newest company filter')]))); });
 expect(await screen.findByText('Newest company filter')).toBeInTheDocument();
 await act(async () => { pending.SYSTEM_DEFAULT(response(page([item('SYSTEM_DEFAULT', 'Stale system filter')]))); });
 expect(screen.getByText('Newest company filter')).toBeInTheDocument();
 expect(screen.queryByText('Stale system filter')).not.toBeInTheDocument();
});
it('requires exact-scope confirmation before recording evidence-backed mixed-bulk policy', async () => {
 const organizations = [{ id: 'owner-a', name: 'Owner A', kind: 'Customer' }, { id: 'mill-a', name: 'Mill A', kind: 'Mill' }];
 const browser = userEvent.setup();
 fetchMock.mockImplementation(async (input, init) => {
  const path = String(input).split('?')[0];
  if (path === '/api/auth/csrf') return response({ token: 'csrf' });
  if (path === '/api/ledger/setup') return response({ owners: organizations, facilities, locations: [{ id: 'silo-a', name: 'Silo A', facilityId: 'facility-a', kind: 'SILO', mixedBulk: true }], commodities: [{ id: 'paddy', name: 'Paddy' }], equipment: [], assignments: [], policies: [], compatibilities: [], mixedBulkPolicies: [] });
  if (path === '/api/admin/users') return response([]);
  if (path === '/api/admin/organizations') return response(organizations);
  if (path === '/api/commodities' || path === '/api/measurement-units') return response({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
  if (path === '/api/ledger/receiving-thresholds') return response({ systemDefault: { percentage: '2', version: 'threshold-v1' }, companyOverrides: [] });
  if (path === '/api/ledger/receiving-thresholds/companies' || path === '/api/ledger/receiving-thresholds/history') return response({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false });
  if (path === '/api/ledger/evidence' && init?.method === 'POST') return response({ id: 'evidence-a' });
  return response({});
 });
 render(<LedgerSetup user={user} />);
 await browser.click(await screen.findByText(/Advanced policy/, { selector: 'summary' }));
 const mixedPanel = screen.getByRole('heading', { name: 'Mixed-bulk location policies' }).closest('section')!;
 const policyOwner = within(mixedPanel).getByLabelText('Policy owner');
 expect(within(mixedPanel).getByRole('option', { name: 'Owner A' })).toBeInTheDocument();
 expect(within(mixedPanel).queryByRole('option', { name: 'Mill A' })).not.toBeInTheDocument();
 await browser.selectOptions(within(mixedPanel).getByLabelText('Enabled mixed-bulk location'), 'silo-a');
 await browser.selectOptions(policyOwner, 'owner-a');
 await browser.selectOptions(within(mixedPanel).getByLabelText('Commodity'), 'paddy');
 const record = within(mixedPanel).getByRole('button', { name: 'Record mixed-bulk policy' });
 expect(record).toBeDisabled();
 await browser.upload(within(mixedPanel).getByLabelText('Evidence file'), new File(['approval evidence'], 'policy.pdf', { type: 'application/pdf' }));
 await browser.click(within(mixedPanel).getByRole('button', { name: 'Upload evidence' }));
 expect(record).toBeDisabled();
 await browser.click(within(mixedPanel).getByLabelText(/I confirm this evidence explicitly approves/));
 expect(record).toBeEnabled();
 await browser.click(record);
 await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url) === '/api/ledger/mixed-bulk-policies')).toBe(true));
 const write = fetchMock.mock.calls.find(([url]) => String(url) === '/api/ledger/mixed-bulk-policies')!;
 expect(JSON.parse(String(write[1]?.body))).toMatchObject({ locationId: 'silo-a', ownerId: 'owner-a', commodityId: 'paddy', stage: 'RAW_PADDY', productIdentity: 'RAW_PADDY', evidenceId: 'evidence-a', identityScopeConfirmed: true });
});
