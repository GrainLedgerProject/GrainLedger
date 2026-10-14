import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { LedgerWorkspace } from './LedgerWorkspace';
import { type User } from './api';

const actor: User = { id: 'actor', email: 'staff@example.com', displayName: 'Staff', phoneNumber: null, role: 'MillOperator', organizationId: 'mill', organizationName: 'Mill', isActive: true, permissions: [] };
const look = { facilities: [{ id: 'facility', name: 'Mill facility' }], locations: [{ id: 'store', facilityId: 'facility', name: 'Main store', kind: 'STORE' }, { id: 'isolation', facilityId: 'facility', name: 'Isolation', kind: 'ISOLATION' }], commodities: [{ id: 'rice', name: 'Rice' }], equipment: [{ id: 'dryer', facilityId: 'facility', name: 'Dryer' }] };
const lot = { id: 'source', code: 'Source lot', ownerId: 'customer', commodityId: 'rice', stage: 'RAW_PADDY', productIdentity: 'RAW_PADDY', positions: [{ locationId: 'store', facilityId: 'facility', quantityKg: '10.000001', availableKg: '10.000001' }], restrictions: [] };
const draft = { id: 'draft', ownerId: 'customer', facilityId: 'facility', kind: 'DRYING', occurredAt: '2026-01-01T00:00:00Z', equipmentId: 'dryer', inputs: [{ lotId: 'source', locationId: 'store', quantityKg: '10.000001' }], outputs: [{ id: 'dried', code: 'Dried lot', commodityId: 'rice', stage: 'DRIED_PADDY', productIdentity: 'DRIED_PADDY', locationId: 'store', quantityKg: '9.000001' }], declaredLossKg: '1', status: 'DRAFT', version: 'version-1', confirmedVersion: 'version-1', varianceKg: '0', allowedVarianceKg: '0' };
const fetchMock = vi.fn<typeof fetch>();
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const page = (items: unknown[]) => ({ items, page: 1, pageSize: 25, hasMore: false });
let routes: Record<string, unknown>;
beforeEach(() => {
 vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset();
 routes = {
  '/api/auth/csrf': { token: 'csrf' }, '/api/ledger/lookups': look, '/api/ledger/lots': [lot], '/api/ledger/holds': [], '/api/ledger/notifications': [], '/api/ledger/operations': [draft], '/api/ledger/corrections': [],
  '/api/admin/organizations': [{ id: 'company', name: 'Customer', kind: 'Customer' }],
  '/api/farmers': page([{ id: 'farmer', name: 'Farmer' }]), '/api/plots': page([{ id: 'plot', farmerId: 'farmer', code: 'Plot' }]), '/api/crop-seasons': page([{ id: 'season', name: 'Season' }]), '/api/plot-seasons': page([{ id: 'ps', plotId: 'plot', seasonId: 'season', riceVariety: 'IR' }]),
  '/api/procurement-reference-data': { receivingOptions: [{ collectionSiteId: 'site', collectionSiteCode: 'SITE', collectionSiteName: 'Site', receivingLocationId: 'loc', receivingLocationCode: 'Store' }], commodity: { id: 'paddy' }, massUnits: [{ code: 'KG', name: 'kg' }], page: 1, pageSize: 100, hasMore: false, maxLinesPerReceipt: 100 }, '/api/procurements': page([]),
 };
 fetchMock.mockImplementation(async (url, init) => { const path = String(url).split('?')[0]; if (init?.method && init.method !== 'GET') return response(path.endsWith('/commit') ? { id: 'procurement' } : path.endsWith('/evidence') ? { id: 'evidence' } : {}); return path in routes ? response(routes[path]) : response({ title: `Unexpected route ${path}` }, 404); });
});
it('registers sources and commits official procurement envelope with exact mass strings', async () => {
 render(<LedgerWorkspace user={{ ...actor, role: 'PurchasingStaff', organizationId: 'company', permissions: ['farms.write', 'purchases.write'] }} />);
 const browser = userEvent.setup(); await browser.click(await screen.findByRole('button', { name: 'Sources & receiving' }));
 await browser.type(screen.getByLabelText('Farmer name'), 'Farmer Two'); await browser.click(screen.getByRole('button', { name: 'Register farmer' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).startsWith('/api/procurement-reference-data?'))).toBe(true));
 const procurement = within((await screen.findByRole('heading', { name: 'Commit procurement' })).closest('section')!);
 await browser.selectOptions(procurement.getByLabelText('Farmer', { selector: 'select' }), 'farmer');
 await browser.selectOptions(procurement.getByLabelText('Collection site', { selector: 'select' }), 'site');
 await browser.selectOptions(procurement.getByLabelText('Receiving location', { selector: 'select' }), 'loc');
 await browser.selectOptions(procurement.getByLabelText('Plot season', { selector: 'select' }), 'ps');
 await browser.type(procurement.getByLabelText('Measured value (exact decimal string)'), '600.000001');
 await browser.selectOptions(procurement.getByLabelText('Unit', { selector: 'select' }), 'KG');
 await browser.type(procurement.getByLabelText('Moisture percent'), '22.5');
 await browser.type(procurement.getByLabelText('Received at'), '2026-10-14T03:00');
 await browser.click(screen.getByRole('button', { name: 'Commit procurement' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url === '/api/procurements/commit')).toBe(true));
 const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/procurements/commit')!;
 const request = JSON.parse(String(init?.body));
 expect(request).toMatchObject({ deviceId: 'grainledger-web', payloadSchemaVersion: 1, payload: { farmerId: 'farmer', collectionSiteId: 'site', receivingLocationId: 'loc', commodityId: 'paddy', items: [{ lineNo: 1, plotSeasonId: 'ps', measuredValue: '600.000001', measuredUnit: 'KG', moisturePercent: '22.5' }] } });
 expect(request.payload.items[0].lotId).toBeTruthy(); expect(request.payload.items[0].sourceLotId).toBeTruthy();
 expect(new Headers(init?.headers).get('X-CSRF-TOKEN')).toBe('csrf');
 expect(screen.getByText(/Arrival and receipt corrections are not available/)).toBeInTheDocument();
});
it('edits processing draft with decimal strings and supports multiple actual output rows', async () => {
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByText('DRYING — Dried lot — DRAFT'));
 await browser.click(screen.getByRole('button', { name: 'Edit processing draft' }));
 const editor = screen.getByRole('heading', { name: 'Edit processing draft' }).closest('section')!;
 await browser.clear(within(editor).getByLabelText('Actual output (kg)'));
 await browser.type(within(editor).getByLabelText('Actual output (kg)'), '8.000001');
 await browser.clear(within(editor).getByLabelText('Declared loss (kg, nonnegative)'));
 await browser.type(within(editor).getByLabelText('Declared loss (kg, nonnegative)'), '2');
 await browser.click(within(editor).getByRole('button', { name: 'Save draft changes' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/ledger/operations/draft' && init?.method === 'PUT')).toBe(true));
 const [, init] = fetchMock.mock.calls.find(([url, init]) => url === '/api/ledger/operations/draft' && init?.method === 'PUT')!;
 expect(JSON.parse(String(init?.body))).toMatchObject({ id: 'draft', inputs: [{ quantityKg: '10.000001' }], outputs: [{ quantityKg: '8.000001', productIdentity: 'DRIED_PADDY' }], declaredLossKg: '2' });
 await browser.click(screen.getByRole('button', { name: 'Add actual output' }));
 expect(screen.getAllByLabelText('Actual output (kg)')).toHaveLength(2);
});
it('shows HOLD failures instead of swallowing errors or claiming success', async () => {
 let rejected = false;
 fetchMock.mockImplementation(async (url, init) => {
  const path = String(url).split('?')[0];
  if (init?.method === 'POST') { rejected = true; return response({ type: 'about:blank', title: 'Forbidden', status: 403, detail: 'HOLD permission denied.' }, 403); }
  const routes: Record<string, unknown> = { '/api/auth/csrf': { token: 'csrf' }, '/api/ledger/lookups': look, '/api/ledger/lots': [lot], '/api/ledger/holds': [], '/api/ledger/notifications': [] };
  return path in routes ? response(routes[path]) : response({}, 404);
 });
 render(<LedgerWorkspace user={{ ...actor, role: 'CustomerManager', organizationId: 'customer', permissions: ['lots.read.tenant', 'holds.manage'] }} />);
 const browser = userEvent.setup();
 const holdLot = await screen.findByLabelText('Own-company lot to HOLD');
 await within(holdLot).findByRole('option', { name: /Source lot \[source\].*physical 10\.000001 kg, available 10\.000001 kg/ });
 await browser.selectOptions(holdLot, 'source');
 await browser.type(screen.getByLabelText('HOLD reason'), 'Stop use');
 await browser.click(screen.getByRole('button', { name: 'Place customer HOLD' }));
 const alert = await screen.findByRole('alert');
 expect(alert).toHaveTextContent(/HOLD permission denied/);
 expect(rejected).toBe(true);
 expect(fetchMock.mock.calls.some(([url, init]) => String(url).split('?')[0] === '/api/ledger/holds' && init?.method === 'POST')).toBe(true);
 expect(screen.queryByText('HOLD and recipient notifications saved atomically.')).not.toBeInTheDocument();
});
it('shows official receiving tab for read permissions', async () => {
 render(<LedgerWorkspace user={{ ...actor, permissions: ['lots.read.facility', 'farms.read'] }} />);
 expect(await screen.findByRole('heading', { name: 'Scoped stock search' })).toBeInTheDocument();
 expect(screen.getByRole('button', { name: 'Sources & receiving' })).toBeInTheDocument();
});
it('reconciles output positions against selected replacement version with Ledger evidence', async () => {
 routes['/api/ledger/operations'] = [{ ...draft, id: 'original', status: 'COMMITTED' }, { ...draft, id: 'replacement', outputs: [{ ...draft.outputs[0], id: 'new-dried', code: 'Replacement lot' }], version: 'replacement-v2' }];
 routes['/api/ledger/corrections'] = [{ id: 'correction', operationId: 'original', reason: 'Entry error', status: 'REQUESTED', evidenceId: null, reconciliation: null, replacementOperationId: null }];
 routes['/api/ledger/lots/dried'] = { ...lot, id: 'dried', code: 'Dried lot', positions: [{ locationId: 'isolation', facilityId: 'facility', quantityKg: '9.000001', availableKg: '0' }] };
 render(<LedgerWorkspace user={{ ...actor, role: 'MillManager', permissions: ['corrections.reconcile'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByText('DRYING: Dried lot — correction REQUESTED'));
 const review = within(screen.getByText('DRYING: Dried lot — correction REQUESTED').closest('details')!);
 await browser.upload(review.getByLabelText('Evidence file'), new File(['weighbridge'], 'physical.txt', { type: 'text/plain' }));
 await browser.click(review.getByRole('button', { name: 'Upload evidence' }));
 await review.findByRole('link', { name: 'View stored evidence' });
 await browser.type(review.getByLabelText('Physical reconciliation / cancellation reason'), 'Checked physical stock');
 await browser.click(review.getByRole('button', { name: 'Load actual current output positions' }));
 expect(await review.findByLabelText('Verified physical position (kg)')).toHaveValue('9.000001');
 await browser.selectOptions(review.getByLabelText('Replacement draft to review'), 'replacement');
 await browser.click(review.getByRole('checkbox', { name: /I verified every actual physical output position/ }));
 await browser.click(review.getByRole('button', { name: 'Record bound physical reconciliation' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url === '/api/ledger/corrections/correction/reconcile')).toBe(true));
 const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/ledger/corrections/correction/reconcile')!;
 expect(JSON.parse(String(init?.body))).toEqual({ evidenceId: 'evidence', reason: 'Checked physical stock', reconciliation: [{ lotId: 'dried', locationId: 'isolation', quantityKg: '9.000001' }], replacementOperationId: 'replacement', replacementVersion: 'replacement-v2' });
});
it('requires an explicitly selected customer for admin crop-season creation', async () => {
 render(<LedgerWorkspace user={{ ...actor, permissions: ['platform.configure'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByRole('button', { name: 'Sources & receiving' }));
 await browser.selectOptions(screen.getByLabelText('Customer company'), 'company');
 await browser.type(screen.getByLabelText('Season name'), 'Wet season');
 await browser.type(screen.getByLabelText('Starts'), '2026-06-01');
 await browser.type(screen.getByLabelText('Ends'), '2026-12-31');
 await browser.click(screen.getByRole('button', { name: 'Create crop season' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/crop-seasons' && init?.method === 'POST')).toBe(true));
 const [, init] = fetchMock.mock.calls.find(([url, init]) => url === '/api/crop-seasons' && init?.method === 'POST')!;
 expect(JSON.parse(String(init?.body))).toEqual({ companyId: 'company', name: 'Wet season', startsOn: '2026-06-01', endsOn: '2026-12-31' });
});
