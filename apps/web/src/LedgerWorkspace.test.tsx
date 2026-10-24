import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { LedgerWorkspace } from './LedgerWorkspace';
import { type User } from './api';
import { type Page, type Row } from './ledger-ui';

const actor: User = { id: 'actor', email: 'staff@example.com', displayName: 'Staff', phoneNumber: null, role: 'MillOperator', organizationId: 'mill', organizationName: 'Mill', isActive: true, permissions: [] };
const look = { owners: [{ id: 'customer', name: 'Customer Company' }], facilities: [{ id: 'facility', name: 'Mill facility' }], locations: [{ id: 'store', facilityId: 'facility', name: 'Main store', kind: 'STORE' }, { id: 'isolation', facilityId: 'facility', name: 'Isolation', kind: 'ISOLATION' }], commodities: [{ id: 'rice', name: 'Rice' }], equipment: [{ id: 'dryer', facilityId: 'facility', name: 'Dryer' }] };
const lot = { id: 'source', code: 'Source lot', ownerId: 'customer', commodityId: 'rice', stage: 'RAW_PADDY', productIdentity: 'RAW_PADDY', positions: [{ locationId: 'store', facilityId: 'facility', quantityKg: '10.000001', availableKg: '10.000001' }], restrictions: [] };
const draft = { id: 'draft', ownerId: 'customer', facilityId: 'facility', kind: 'DRYING', occurredAt: '2026-01-01T00:00:00Z', equipmentId: 'dryer', inputs: [{ lotId: 'source', locationId: 'store', quantityKg: '10.000001' }], outputs: [{ id: 'dried', code: 'Dried lot', commodityId: 'rice', stage: 'DRIED_PADDY', productIdentity: 'DRIED_PADDY', locationId: 'store', quantityKg: '9.000001' }], declaredLossKg: '1', status: 'DRAFT', version: 'version-1', confirmedVersion: 'version-1', varianceKg: '0', allowedVarianceKg: '0', createdBy: 'actor', canEdit: true, canDelete: true };
const receivingTask = { id: 'arrival', code: 'ARR-1', ownerCompanyId: 'customer', ownerCompanyName: 'Customer Company', facilityId: 'facility', facilityName: 'Mill facility', receivingLocationId: 'store', receivingLocationName: 'Main store', status: 'PENDING_ARRIVAL', receivingVersion: 0, effectiveThreshold: { companyId: 'customer', percentage: '2', source: 'SYSTEM_DEFAULT', revision: 'default-r1' }, expectedTotalKg: '10', actualTotalKg: null, usableStockKg: '0', latestMeasurementSetId: null, receivedAt: '2026-10-14T00:00:00Z', items: [{ id: 'item-1', lineNo: 1, sourceLotId: 'source-lot', lotId: 'source', lotCode: 'Source lot', plotSeasonId: 'ps', riceVariety: 'IR', expectedValue: '10', expectedUnit: 'KG', expectedKg: '10', actualValue: null, actualUnit: null, actualKg: null }], measurementSets: [], reviews: [] };
const fetchMock = vi.fn<typeof fetch>();
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const page = (items: unknown[]) => ({ items, page: 1, pageSize: 25, totalCount: items.length, hasMore: false });
let routes: Record<string, unknown>;
function readResponse(url: string, source = routes) {
 const request = new URL(url, 'http://localhost'), path = request.pathname, body = source[path];
 if (!(path in source)) return response({ title: `Unexpected route ${path}` }, 404);
 if (!body || typeof body !== 'object' || !('items' in body) || !('totalCount' in body)) return response(body);
 let items = (body as Page<Row>).items;
 const query = request.searchParams, ids = query.get('ids')?.split(',');
 if (ids) items = items.filter(row => ids.includes(row.id));
 if (path.startsWith('/api/ledger/')) {
  for (const field of ['status', 'kind', 'stage']) if (query.get(field)) items = items.filter(row => row[field] === query.get(field));
  if (query.get('ownerId')) items = items.filter(row => (row.ownerId || row.ownerCompanyId) === query.get('ownerId'));
  if (query.get('facilityId')) items = items.filter(row => row.facilityId === query.get('facilityId') || row.positions?.some((position: Record<string, string>) => position.facilityId === query.get('facilityId')));
  if (query.get('queueOnly') === 'true') items = items.filter(row => row.status === 'DRAFT' && row.varianceKg != null && !/^[-+]?0+(?:\.0+)?$/.test(row.varianceKg) && row.confirmedVersion !== row.version);
 }
 if (query.get('search')) items = items.filter(row => JSON.stringify(row).toLowerCase().includes(query.get('search')!.toLowerCase()));
 const current = Number(query.get('page') || 1), size = Number(query.get('pageSize') || 25), totalCount = items.length;
 return response({ items: items.slice((current - 1) * size, current * size), page: current, pageSize: size, totalCount, hasMore: current * size < totalCount });
}
beforeEach(() => {
 vi.restoreAllMocks(); vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset();
 routes = {
  '/api/auth/csrf': { token: 'csrf' }, '/api/ledger/trace/starts': page([]), '/api/ledger/lookups': look, '/api/ledger/lots': page([lot]), '/api/ledger/lots/source': lot, '/api/ledger/holds': page([]), '/api/ledger/notifications': page([]), '/api/ledger/history': page([]), '/api/ledger/operations': page([draft]), '/api/ledger/corrections': page([]),
  '/api/ledger/setup': { ...look, assignments: [], policies: [], compatibilities: [] }, '/api/admin/users': [], '/api/commodities': page([]), '/api/measurement-units': page([]),
  '/api/admin/organizations': [{ id: 'company', name: 'Customer', kind: 'Customer' }], '/api/ledger/receiving-thresholds/own-company': { companyId: 'company', percentage: '2', source: 'SYSTEM_DEFAULT', revision: 'default-r1' },
  '/api/farmers': page([{ id: 'farmer', name: 'Farmer', createdBy: 'actor', version: 1, canEdit: true, canDelete: true }]), '/api/plots': page([{ id: 'plot', farmerId: 'farmer', code: 'Plot', createdBy: 'actor', version: 1, canEdit: true, canDelete: true }]), '/api/crop-seasons': page([{ id: 'season', name: 'Season', createdBy: 'actor', version: 1, canEdit: true, canDelete: true }]), '/api/plot-seasons': page([{ id: 'ps', plotId: 'plot', seasonId: 'season', riceVariety: 'IR', createdBy: 'actor', version: 1, canEdit: true, canDelete: true }]),
  '/api/procurement-reference-data': { receivingOptions: [{ collectionSiteId: 'site', collectionSiteCode: 'SITE', collectionSiteName: 'Site', receivingLocationId: 'loc', receivingLocationCode: 'Store' }], commodity: { id: 'paddy' }, massUnits: [{ code: 'KG', name: 'kg' }], page: 1, pageSize: 100, totalCount: 1, hasMore: false, maxLinesPerReceipt: 100 }, '/api/procurements': page([]),
  '/api/ledger/receiving-tasks': page([receivingTask]), '/api/ledger/receiving/arrival': receivingTask,
 };
 fetchMock.mockImplementation(async (url, init) => {
  const path = String(url).split('?')[0];
  if (init?.method && init.method !== 'GET') return response(path.endsWith('/commit') ? { id: 'procurement' } : path.endsWith('/evidence') ? { id: 'evidence' } : {});
  return readResponse(String(url));
 });
});
it('registers source procurement with exact expected quantity', async () => {
 render(<LedgerWorkspace user={{ ...actor, role: 'PurchasingStaff', organizationId: 'company', permissions: ['farms.write', 'purchases.write'] }} />);
 const browser = userEvent.setup(); await browser.click(await screen.findByRole('button', { name: 'Receive stock' })); await browser.click(screen.getByRole('button', { name: 'Source setup' }));
 const farmerForm = within(screen.getByRole('button', { name: 'Register farmer' }).closest('form')!);
 await browser.click(farmerForm.getByLabelText('Farmer name')); await browser.paste('Farmer Two');
 await browser.click(farmerForm.getByRole('button', { name: 'Register farmer' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).startsWith('/api/procurement-reference-data?'))).toBe(true));
 await browser.click(screen.getByRole('button', { name: 'Procurement' }));
 const procurement = within((await screen.findByRole('button', { name: 'Register procurement' })).closest('form')!);
 await within(procurement.getByLabelText('Farmer', { selector: 'select' })).findByRole('option', { name: 'Farmer' });
 await browser.selectOptions(procurement.getByLabelText('Farmer', { selector: 'select' }), 'farmer');
 await browser.selectOptions(procurement.getByLabelText('Collection site', { selector: 'select' }), 'site');
 await browser.selectOptions(procurement.getByLabelText('Receiving location', { selector: 'select' }), 'loc');
 const plotChoice = await procurement.findByLabelText('Plot', { selector: 'select' });
 await within(plotChoice).findByRole('option', { name: 'Plot' });
 await browser.selectOptions(plotChoice, 'plot');
 await within(await procurement.findByLabelText('Plot season', { selector: 'select' })).findByRole('option', { name: /IR/ });
 await browser.selectOptions(procurement.getByLabelText('Plot season', { selector: 'select' }), 'ps');
 await browser.click(procurement.getByLabelText('Expected source quantity (exact decimal string)'));
 await browser.paste('600.000001');
 await browser.selectOptions(procurement.getByLabelText('Unit', { selector: 'select' }), 'KG');
 await browser.click(procurement.getByLabelText('Moisture percent')); await browser.paste('22.5');
 await browser.type(procurement.getByLabelText('Received at'), '2026-10-14T03:00');
 await browser.click(screen.getByRole('button', { name: 'Register procurement' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url === '/api/procurements')).toBe(true));
 const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/procurements')!;
 const request = JSON.parse(String(init?.body));
 expect(request).toMatchObject({ deviceId: 'grainledger-web', payloadSchemaVersion: 2, payload: { farmerId: 'farmer', collectionSiteId: 'site', receivingLocationId: 'loc', commodityId: 'paddy', items: [{ lineNo: 1, plotSeasonId: 'ps', measuredValue: '600.000001', measuredUnit: 'KG', moisturePercent: '22.5' }] } });
 expect(request.payload.items[0].lotId).toBeTruthy(); expect(request.payload.items[0].sourceLotId).toBeTruthy();
 expect(new Headers(init?.headers).get('X-CSRF-TOKEN')).toBe('csrf');
});
it('submits complete operator measurements with exact decimals and explicit arrival confirmation', async () => {
 render(<LedgerWorkspace user={{ ...actor, permissions: ['lots.receive'] }} />);
 const browser = userEvent.setup();
 await browser.selectOptions(await screen.findByLabelText('Assigned facility'), 'facility');
 await browser.click(await screen.findByRole('button', { name: /ARR-1.*Pending mill arrival/ }));
 await browser.type(await screen.findByLabelText('Actual received quantity'), '10.000001');
 await browser.selectOptions(screen.getByLabelText('Actual unit'), 'KG');
 await browser.click(screen.getByLabelText(/I confirm all actual quantities/));
 const originalFetch = fetchMock.getMockImplementation()!;
 let failFirstMeasurement = true;
 fetchMock.mockImplementation(async (url, init) => {
  if (failFirstMeasurement && String(url).endsWith('/measurements')) { failFirstMeasurement = false; return response({ title: 'Temporary request failure' }, 503); }
  return originalFetch(url, init);
 });
 await browser.click(screen.getByRole('button', { name: 'Submit arrival measurements' }));
 expect(await screen.findByRole('alert')).toBeInTheDocument();
 const firstCall = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/measurements'))!;
 const firstRequest = JSON.parse(String(firstCall[1]?.body));
 await browser.click(screen.getByRole('button', { name: 'Submit arrival measurements' }));
 await waitFor(() => expect(fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/measurements'))).toHaveLength(2));
 const lastCall = fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/measurements'))[1];
 const request = JSON.parse(String(lastCall[1]?.body));
 expect(request).toMatchObject({ expectedReceivingVersion: 0, observations: '', items: [{ procurementItemId: 'item-1', measuredValue: '10.000001', measuredUnit: 'KG' }] });
 expect(request.operationId).toBe(firstRequest.operationId);
 expect(request.capturedAt).toBe(firstRequest.capturedAt);
});
it('previews exact threshold boundary with decimal arithmetic and marks one micro-kg beyond for review', async () => {
 const task = { ...receivingTask, effectiveThreshold: { companyId: 'customer', percentage: '2', source: 'SYSTEM_DEFAULT', revision: 'default-r1' }, expectedTotalKg: '100', items: [{ ...receivingTask.items[0], expectedValue: '100', expectedKg: '100' }] };
 routes['/api/ledger/receiving-tasks'] = page([task]); routes['/api/ledger/receiving/arrival'] = task;
 render(<LedgerWorkspace user={{ ...actor, permissions: ['lots.receive'] }} />);
 const browser = userEvent.setup();
 await browser.selectOptions(await screen.findByLabelText('Assigned facility'), 'facility');
 await browser.click(await screen.findByRole('button', { name: /ARR-1.*Pending mill arrival/ }));
 await browser.type(await screen.findByLabelText('Actual received quantity'), '97.999999');
 await browser.selectOptions(screen.getByLabelText('Actual unit'), 'KG');
 expect(await screen.findByText(/Manager review — over threshold/)).toBeInTheDocument();
 await browser.clear(screen.getByLabelText('Actual received quantity'));
 await browser.type(screen.getByLabelText('Actual received quantity'), '98');
 expect(await screen.findByText('Within threshold')).toBeInTheDocument();
 expect(screen.getByText(/Preview is advisory/)).toBeInTheDocument();
});
it('shows stored measurement threshold rather than changed current setting and labels legacy sets pre-feature', async () => {
 const task = {
  ...receivingTask, status: 'RECEIVED_WITHIN_TOLERANCE', effectiveThreshold: { companyId: 'customer', percentage: '3', source: 'COMPANY_OVERRIDE', revision: 'current-r2' },
  actualTotalKg: '98', latestMeasurementSetId: 'set-current',
  items: [{ ...receivingTask.items[0], expectedValue: '100', expectedKg: '100', actualValue: '98', actualUnit: 'KG', actualKg: '98' }],
  measurementSets: [
   { id: 'set-legacy', recordedAt: '2026-10-13T00:00:00Z', observations: '', threshold: null, items: [{ procurementItemId: 'item-1', measuredValue: '99', measuredUnit: 'KG', actualKg: '99' }] },
   { id: 'set-current', recordedAt: '2026-10-14T00:00:00Z', observations: '', threshold: { companyId: 'customer', percentage: '2', source: 'SYSTEM_DEFAULT', revision: 'old-r1' }, items: [{ procurementItemId: 'item-1', measuredValue: '98', measuredUnit: 'KG', actualKg: '98' }] },
  ],
 };
 routes['/api/ledger/receiving-tasks'] = page([task]); routes['/api/ledger/receiving/arrival'] = task;
 render(<LedgerWorkspace user={{ ...actor, permissions: ['lots.receive'] }} />);
 const browser = userEvent.setup();
 await browser.selectOptions(await screen.findByLabelText('Assigned facility'), 'facility');
 await browser.click(await screen.findByRole('button', { name: /ARR-1.*Received — within tolerance/ }));
 expect(screen.getByText(/Effective threshold for new measurements: 3% · Company override/)).toBeInTheDocument();
 const disclosures = screen.getAllByText('View 1 measured lines');
 await browser.click(disclosures[0]);
 const legacyHistory = within(disclosures[0].closest('details')!);
 expect(await legacyHistory.findByText(/threshold not recorded/)).toBeInTheDocument();
 await browser.click(disclosures[1]);
 const currentHistory = within(disclosures[1].closest('details')!);
 expect(await currentHistory.findByText('Within threshold')).toBeInTheDocument();
 expect(await currentHistory.findByText(/Applied threshold: 2% · System default · revision old-r1/)).toBeInTheDocument();
});
it('disables shortage approval whenever any received line has surplus', async () => {
 const surplusTask = { ...receivingTask, status: 'AWAITING_MANAGER_REVIEW', actualTotalKg: '10.5', latestMeasurementSetId: 'set-1', items: [{ ...receivingTask.items[0], actualValue: '10.5', actualUnit: 'KG', actualKg: '10.5' }], measurementSets: [{ id: 'set-1', operationId: 'op-1', operatorId: 'operator', capturedAt: '2026-10-14T01:00:00Z', recordedAt: '2026-10-14T01:01:00Z', observations: 'Scale checked', items: [{ procurementItemId: 'item-1', measuredValue: '10.5', measuredUnit: 'KG', actualKg: '10.5' }] }], reviews: [] };
 routes['/api/ledger/receiving-tasks'] = page([surplusTask]); routes['/api/ledger/receiving/arrival'] = surplusTask;
 render(<LedgerWorkspace user={{ ...actor, permissions: ['receiving.reconcile'] }} />);
 const browser = userEvent.setup(); await browser.selectOptions(await screen.findByLabelText('Assigned facility'), 'facility');
 await browser.click(await screen.findByRole('button', { name: /ARR-1.*Awaiting manager review/ }));
 expect(await screen.findByText(/Surplus detected. Shortage approval unavailable/)).toBeInTheDocument();
 expect(screen.getByRole('option', { name: /Approve shortage and post actual stock/ })).toBeDisabled();
 expect(screen.getByRole('option', { name: /Request remeasurement/ })).toBeEnabled();
});
it.each([false, true])('shows historical equipment labels without adding retired equipment to new processing choices (retired=%s)', async retired => {
 const equipmentName = retired ? 'Old dryer (retired)' : 'Dryer';
 routes['/api/ledger/operations'] = page([{ ...draft, id: 'committed', status: 'COMMITTED', equipmentId: retired ? 'old-dryer' : 'dryer', equipmentName, canEdit: false, canDelete: false }]);
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByRole('button', { name: 'Process & correct' }));
 await browser.click(screen.getByRole('button', { name: 'Committed list' }));
 const summary = await screen.findByText(new RegExp(`DRYING:.*${retired ? 'Old dryer \\(retired\\)' : 'Dryer'}.*COMMITTED`), { selector: 'summary' });
 await browser.click(summary);
 expect(within(summary.closest('details')!).getByText(equipmentName, { selector: 'dd' })).toBeInTheDocument();
 await browser.click(screen.getByText('Create processing draft', { selector: 'summary' }));
 const form = within(screen.getByRole('heading', { name: 'New processing draft' }).closest('section')!);
 await browser.selectOptions(form.getByLabelText('Processing facility'), 'facility');
 const choices = within(form.getByLabelText('Equipment'));
 expect(choices.getByRole('option', { name: 'Dryer' })).toHaveValue('dryer');
 expect(choices.queryByRole('option', { name: /Old dryer|\(retired\)/ })).not.toBeInTheDocument();
});
it('edits processing draft with decimal strings and supports multiple actual output rows', async () => {
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByText(/DRYING:.*DRAFT/));
 await browser.click(screen.getByRole('button', { name: 'Edit processing draft' }));
 const editor = screen.getByRole('heading', { name: 'Edit processing draft' }).closest('section')!;
 await browser.clear(within(editor).getByLabelText('Actual output (kg)'));
 await browser.type(within(editor).getByLabelText('Actual output (kg)'), '8.000001');
 await browser.clear(within(editor).getByLabelText('Declared loss (kg, nonnegative)'));
 await browser.type(within(editor).getByLabelText('Declared loss (kg, nonnegative)'), '2');
 await browser.click(within(editor).getByRole('button', { name: 'Save draft changes' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/ledger/operations/draft' && init?.method === 'PUT')).toBe(true));
 const [, init] = fetchMock.mock.calls.find(([url, init]) => url === '/api/ledger/operations/draft' && init?.method === 'PUT')!;
 expect(JSON.parse(String(init?.body))).toMatchObject({ id: 'draft', version: 'version-1', inputs: [{ quantityKg: '10.000001' }], outputs: [{ quantityKg: '8.000001', productIdentity: 'DRIED_PADDY' }], declaredLossKg: '2' });
 await browser.click(await screen.findByText('Create processing draft', { selector: 'summary' }));
 await browser.click(screen.getByRole('button', { name: 'Add actual output' }));
 expect(screen.getAllByLabelText('Actual output (kg)')).toHaveLength(2);
});
it('requires explicit refresh and reselect for stale mixed-bulk draft inputs', async () => {
 const historicalLot = { ...lot, positions: lot.positions.map(position => ({ ...position, quantityKg: '0', availableKg: '0' })), mixedBulk: true, isCurrentPool: false, poolLocationId: 'store', poolRevision: 1 };
 const initialPool = { ...lot, id: 'pool-v2', code: 'Pool v2', mixedBulk: true, isCurrentPool: true, poolLocationId: 'store', poolRevision: 2 };
 routes['/api/ledger/lots'] = page([initialPool, historicalLot]);
 routes['/api/ledger/lots/source'] = historicalLot;
 routes['/api/ledger/operations'] = page([{ ...draft, inputs: [{ ...draft.inputs[0], poolRevision: 1 }] }]);
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByText(/DRYING:.*DRAFT/));
 await browser.click(screen.getByRole('button', { name: 'Edit processing draft' }));
 const editor = screen.getByRole('heading', { name: 'Edit processing draft' }).closest('section')!;
 expect(within(editor).getByRole('button', { name: 'Save draft changes' })).toBeDisabled();
 const picker = within(editor).getByLabelText('Input lot');
 await waitFor(() => {
  const historicalOption = picker.querySelector('option[value="source"]');
  expect(historicalOption).toBeInTheDocument();
  expect(historicalOption).toBeDisabled();
 });
 const currentPool = { ...initialPool, id: 'pool-v3', code: 'Pool v3', poolRevision: 3 };
 routes['/api/ledger/lots'] = page([currentPool]);
 await browser.click(within(editor).getByRole('button', { name: 'Reload stock options and reselect' }));
 await within(editor).findByRole('option', { name: /current pool revision 3/ });
 await browser.selectOptions(picker, 'pool-v3');
 await browser.selectOptions(within(editor).getByLabelText('Input stock position'), 'store');
 expect(within(editor).getByText(/Draft revision: 3 · current revision: 3/)).toBeInTheDocument();
 expect(within(editor).getByRole('button', { name: 'Save draft changes' })).toBeEnabled();
 await browser.click(within(editor).getByRole('button', { name: 'Save draft changes' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/ledger/operations/draft' && init?.method === 'PUT')).toBe(true));
 const saved = JSON.parse(String(fetchMock.mock.calls.find(([url, init]) => url === '/api/ledger/operations/draft' && init?.method === 'PUT')![1]?.body));
 expect(saved.inputs[0]).toMatchObject({ lotId: 'pool-v3', poolRevision: 3 });
});
it('derives processing output payloads by kind while preserving exact strings and correction locations', async () => {
 const { } = render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByText(/DRYING:.*DRAFT/));
 await browser.click(screen.getByRole('button', { name: 'Edit processing draft' }));
 const editor = screen.getByRole('heading', { name: 'Edit processing draft' }).closest('section')!;
 expect(within(editor).getByLabelText('Output commodity (derived from first input)')).toHaveValue('Rice');
 expect(within(editor).queryByLabelText('Output stage')).not.toBeInTheDocument();
 await browser.selectOptions(within(editor).getByLabelText('Processing kind'), 'MILLING');
 await browser.selectOptions(within(editor).getByLabelText('Produced fraction'), 'BRAN');
 await browser.click(within(editor).getByRole('button', { name: 'Add actual output' }));
 await browser.selectOptions(within(editor).getAllByLabelText('Produced fraction')[1], 'HUSK');
 const outputs = editor.querySelectorAll('fieldset');
 await browser.selectOptions(within(outputs[2] as HTMLElement).getByLabelText('Output physical location'), 'store');
 await browser.type(within(outputs[2] as HTMLElement).getByLabelText('Actual output (kg)'), '1');
 await browser.click(within(editor).getByRole('button', { name: 'Save draft changes' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/ledger/operations/draft' && init?.method === 'PUT')).toBe(true));
 let init = fetchMock.mock.calls.find(([url, init]) => url === '/api/ledger/operations/draft' && init?.method === 'PUT')![1];
 let request = JSON.parse(String(init?.body));
 expect(request.outputs.map((row: Record<string, string>) => [row.stage, row.productIdentity, row.commodityId])).toEqual([['BRAN', 'BRAN', 'rice'], ['HUSK', 'HUSK', 'rice']]);
 expect(request.outputs[0].quantityKg).toBe('9.000001');
 expect(request.inputs[0]).toMatchObject({ lotId: 'source', locationId: 'store', quantityKg: '10.000001' });
 expect(request.outputs[0].id).not.toBe('dried');
 expect(request.outputs[0].code).toBe('');
});
it('retains bagging identity and blend commodity choice in saved payload', async () => {
 const riceLot = { ...lot, stage: 'HEAD_RICE', productIdentity: 'HEAD_RICE' };
 routes['/api/ledger/lots'] = page([riceLot]);
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByRole('button', { name: 'Process & correct' }));
 await browser.click(await screen.findByText('Create processing draft', { selector: 'summary' }));
 const form = screen.getByRole('heading', { name: 'New processing draft' }).closest('section')!;
 await browser.selectOptions(within(form).getByLabelText('Stock owner'), 'customer');
 await browser.selectOptions(within(form).getByLabelText('Processing facility'), 'facility');
 await browser.selectOptions(within(form).getByLabelText('Processing kind'), 'BAGGING');
 await browser.selectOptions(within(form).getByLabelText('Equipment'), 'dryer');
 await within(within(form).getByLabelText('Input lot')).findByRole('option', { name: /Source lot/ });
 await browser.selectOptions(within(form).getByLabelText('Input lot'), 'source');
 await browser.selectOptions(within(form).getByLabelText('Input stock position'), 'store');
 await browser.type(within(form).getByLabelText('Allocated input (kg)'), '10');
 await browser.selectOptions(within(form).getByLabelText('Output physical location'), 'store');
 await browser.type(within(form).getByLabelText('Actual output (kg)'), '10');
 await browser.click(within(form).getByRole('button', { name: 'Save processing draft' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/ledger/operations' && init?.method === 'POST')).toBe(true));
 const request = JSON.parse(String(fetchMock.mock.calls.find(([url, init]) => url === '/api/ledger/operations' && init?.method === 'POST')![1]?.body));
 expect(request.outputs[0]).toMatchObject({ stage: 'BAGGED_RICE', productIdentity: 'HEAD_RICE', commodityId: 'rice' });
});
it('shows the server-assigned processing lot code after saving a draft', async () => {
 const originalFetch = fetchMock.getMockImplementation()!;
 fetchMock.mockImplementation(async (url, init) => {
  if (url === '/api/ledger/operations' && init?.method === 'POST') {
   const request = JSON.parse(String(init.body));
   expect(request.outputs[0].code).toBe('');
   return response({ ...request, outputs: [{ ...request.outputs[0], code: 'DRY-20261018-000123' }] });
  }
  return originalFetch(url, init);
 });
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByRole('button', { name: 'Process & correct' }));
 await browser.click(await screen.findByText('Create processing draft', { selector: 'summary' }));
 const form = screen.getByRole('heading', { name: 'New processing draft' }).closest('section')!;
 await browser.selectOptions(within(form).getByLabelText('Stock owner'), 'customer');
 await browser.selectOptions(within(form).getByLabelText('Processing facility'), 'facility');
 await browser.selectOptions(within(form).getByLabelText('Equipment'), 'dryer');
 await within(within(form).getByLabelText('Input lot')).findByRole('option', { name: /Source lot/ });
 await browser.selectOptions(within(form).getByLabelText('Input lot'), 'source');
 await browser.selectOptions(within(form).getByLabelText('Input stock position'), 'store');
 await browser.type(within(form).getByLabelText('Allocated input (kg)'), '10');
 await browser.selectOptions(within(form).getByLabelText('Output physical location'), 'store');
 await browser.type(within(form).getByLabelText('Actual output (kg)'), '9');
 await browser.click(within(form).getByRole('button', { name: 'Save processing draft' }));
 expect(await screen.findByText(/Output lot code assigned by the server: DRY-20261018-000123/)).toBeInTheDocument();
});
it('drops a reserved output code when changing the first input changes its product identity', async () => {
 routes['/api/ledger/operations'] = page([{ ...draft, kind: 'MERGE', outputs: [{ ...draft.outputs[0], code: 'RAW-20261018-000123', stage: 'RAW_PADDY', productIdentity: 'RAW_PADDY' }] }]);
 routes['/api/ledger/lots'] = page([lot, { ...lot, id: 'head', code: 'HDR-20261018-000124', stage: 'HEAD_RICE', productIdentity: 'HEAD_RICE' }]);
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByText(/MERGE:.*DRAFT/));
 await browser.click(screen.getByRole('button', { name: 'Edit processing draft' }));
 const editor = screen.getByRole('heading', { name: 'Edit processing draft' }).closest('section')!;
 expect(within(editor).getByText('Output lot code: RAW-20261018-000123')).toBeInTheDocument();
 await within(editor).findByRole('option', { name: /HDR-20261018-000124/ });
 await browser.selectOptions(within(editor).getByLabelText('Input lot'), 'head');
 expect(await within(editor).findByText('Output lot code: Assigned when draft is saved')).toBeInTheDocument();
 await browser.selectOptions(within(editor).getByLabelText('Input stock position'), 'store');
 await browser.click(within(editor).getByRole('button', { name: 'Save draft changes' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/ledger/operations/draft' && init?.method === 'PUT')).toBe(true));
 const request = JSON.parse(String(fetchMock.mock.calls.find(([url, init]) => url === '/api/ledger/operations/draft' && init?.method === 'PUT')![1]?.body));
 expect(request.outputs[0]).toMatchObject({ code: '', stage: 'HEAD_RICE', productIdentity: 'HEAD_RICE' });
 expect(request.outputs[0].id).not.toBe('dried');
});
it('keeps selected blend commodity and fixed blended identity in payload', async () => {
 routes['/api/ledger/lots'] = page([{ ...lot, stage: 'HEAD_RICE', productIdentity: 'HEAD_RICE' }]);
 routes['/api/ledger/lookups'] = { ...look, commodities: [{ id: 'rice', name: 'Rice' }, { id: 'blend', name: 'Blend commodity' }] };
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByRole('button', { name: 'Process & correct' }));
 await browser.click(await screen.findByText('Create processing draft', { selector: 'summary' }));
 const form = screen.getByRole('heading', { name: 'New processing draft' }).closest('section')!;
 await browser.selectOptions(within(form).getByLabelText('Stock owner'), 'customer');
 await browser.selectOptions(within(form).getByLabelText('Processing facility'), 'facility');
 await browser.selectOptions(within(form).getByLabelText('Processing kind'), 'BLENDING');
 await browser.selectOptions(within(form).getByLabelText('Equipment'), 'dryer');
 await within(within(form).getByLabelText('Input lot')).findByRole('option', { name: /Source lot/ });
 await browser.selectOptions(within(form).getByLabelText('Input lot'), 'source');
 await browser.selectOptions(within(form).getByLabelText('Input stock position'), 'store');
 await browser.type(within(form).getByLabelText('Allocated input (kg)'), '10');
 await browser.selectOptions(within(form).getByLabelText('Output commodity'), 'blend');
 await browser.selectOptions(within(form).getByLabelText('Output physical location'), 'store');
 await browser.type(within(form).getByLabelText('Actual output (kg)'), '10');
 await browser.click(within(form).getByRole('button', { name: 'Save processing draft' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/ledger/operations' && init?.method === 'POST')).toBe(true));
 const request = JSON.parse(String(fetchMock.mock.calls.find(([url, init]) => url === '/api/ledger/operations' && init?.method === 'POST')![1]?.body));
 expect(request.outputs[0]).toMatchObject({ stage: 'BLENDED_RICE', productIdentity: 'BLENDED_RICE', commodityId: 'blend' });
});
it('derives merge stage and underlying identity separately for bagged input', async () => {
 routes['/api/ledger/lots'] = page([{ ...lot, stage: 'BAGGED_RICE', productIdentity: 'HEAD_RICE' }]);
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByRole('button', { name: 'Process & correct' }));
 await browser.click(await screen.findByText('Create processing draft', { selector: 'summary' }));
 const form = screen.getByRole('heading', { name: 'New processing draft' }).closest('section')!;
 await browser.selectOptions(within(form).getByLabelText('Stock owner'), 'customer');
 await browser.selectOptions(within(form).getByLabelText('Processing facility'), 'facility');
 await browser.selectOptions(within(form).getByLabelText('Processing kind'), 'MERGE');
 await browser.selectOptions(within(form).getByLabelText('Equipment'), 'dryer');
 await within(within(form).getByLabelText('Input lot')).findByRole('option', { name: /Source lot/ });
 await browser.selectOptions(within(form).getByLabelText('Input lot'), 'source');
 await browser.selectOptions(within(form).getByLabelText('Input stock position'), 'store');
 await browser.type(within(form).getByLabelText('Allocated input (kg)'), '10');
 await browser.selectOptions(within(form).getByLabelText('Output physical location'), 'store');
 await browser.type(within(form).getByLabelText('Actual output (kg)'), '10');
 await browser.click(within(form).getByRole('button', { name: 'Save processing draft' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/ledger/operations' && init?.method === 'POST')).toBe(true));
 const request = JSON.parse(String(fetchMock.mock.calls.find(([url, init]) => url === '/api/ledger/operations' && init?.method === 'POST')![1]?.body));
 expect(request.outputs[0]).toMatchObject({ stage: 'BAGGED_RICE', productIdentity: 'HEAD_RICE', commodityId: 'rice' });
});
it('shows HOLD failures instead of swallowing errors or claiming success', async () => {
 let rejected = false;
 fetchMock.mockImplementation(async (url, init) => {
  if (init?.method === 'POST') { rejected = true; return response({ type: 'about:blank', title: 'Forbidden', status: 403, detail: 'HOLD permission denied.' }, 403); }
  return readResponse(String(url));
 });
 render(<LedgerWorkspace user={{ ...actor, role: 'CustomerManager', organizationId: 'customer', permissions: ['lots.read.tenant', 'holds.manage'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByRole('button', { name: 'HOLDs & alerts' }));
 const holdLot = await screen.findByLabelText('Own-company lot to HOLD');
 await within(holdLot).findByRole('option', { name: /Source lot/ });
 await browser.selectOptions(holdLot, 'source');
 await browser.type(screen.getByLabelText('HOLD reason'), 'Stop use');
 await browser.click(screen.getByRole('button', { name: 'Place customer HOLD' }));
 const alert = await screen.findByRole('alert');
 expect(alert).toHaveTextContent(/HOLD permission denied/);
 expect(rejected).toBe(true);
 expect(fetchMock.mock.calls.some(([url, init]) => String(url).split('?')[0] === '/api/ledger/holds' && init?.method === 'POST')).toBe(true);
});
it('shows official receiving tab for read permissions', async () => {
 render(<LedgerWorkspace user={{ ...actor, permissions: ['lots.read.facility', 'farms.read'] }} />);
 await userEvent.setup().click(screen.getByRole('button', { name: 'Receive stock' }));
 expect(screen.queryByRole('button', { name: 'Commit procurement' })).not.toBeInTheDocument();
 expect(screen.getByRole('button', { name: 'Receive stock' })).toBeInTheDocument();
});
it('gates Catalog and submits commodity and unit DTOs', async () => {
 const { rerender } = render(<LedgerWorkspace user={{ ...actor, permissions: [] }} />);
 expect(screen.queryByRole('button', { name: 'Setup' })).not.toBeInTheDocument();
 rerender(<LedgerWorkspace user={{ ...actor, permissions: ['platform.configure'] }} />);
 await userEvent.setup().click(await screen.findByRole('button', { name: 'Setup' }));
 await screen.findByLabelText('Search commodities');
 await userEvent.click(screen.getByRole('button', { name: '+ Add commodity' }));
 const catalog = within(screen.getByRole('dialog', { name: 'Add commodity' }));
 await userEvent.type(catalog.getByLabelText('Code'), 'PADDY');
 await userEvent.type(catalog.getByLabelText('Name'), 'Paddy');
 await userEvent.click(screen.getByRole('button', { name: 'Create commodity' }));
 await userEvent.click(screen.getByRole('button', { name: 'Measurement units' }));
 await screen.findByLabelText('Search measurement units');
 await userEvent.click(screen.getByRole('button', { name: '+ Add unit' }));
 const units = within(screen.getByRole('dialog', { name: 'Add measurement unit' }));
 await userEvent.type(units.getByLabelText('Code'), 'MASS');
 await userEvent.type(units.getByLabelText('Name'), 'Kilogram');
 await userEvent.click(screen.getByRole('button', { name: 'Create measurement unit' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url === '/api/commodities')).toBe(true));
 const writes = fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST');
 expect(JSON.parse(String(writes.find(([url]) => url === '/api/commodities')?.[1]?.body))).toEqual({ code: 'PADDY', name: 'Paddy' });
 expect(JSON.parse(String(writes.find(([url]) => url === '/api/measurement-units')?.[1]?.body))).toEqual({ code: 'MASS', name: 'Kilogram', quantityKind: 'MASS' });
});
it('shows setup resources without configuration controls for read-only users', async () => {
 render(<LedgerWorkspace user={{ ...actor, permissions: ['platform.read'] }} />);
 await userEvent.setup().click(await screen.findByRole('button', { name: 'Setup' }));
 const facilities = await screen.findByRole('table', { name: 'Facilities' });
 expect(within(facilities).getByRole('cell', { name: 'Mill facility' })).toBeInTheDocument();
 expect(screen.queryByRole('button', { name: 'Add facility' })).not.toBeInTheDocument();
 expect(screen.queryByRole('button', { name: 'Create commodity' })).not.toBeInTheDocument();
});
it('reconciles corrections and replacement candidates beyond page one with exact Ledger evidence', async () => {
 const fillerDrafts = Array.from({ length: 25 }, (_, index) => ({ ...draft, id: `filler-${index}`, outputs: [{ ...draft.outputs[0], id: `filler-output-${index}`, code: `Filler ${index}` }] }));
 const correction = { id: 'correction', operationId: 'original', ownerId: 'customer', facilityId: 'facility', reason: 'Entry error', status: 'REQUESTED', evidenceId: null, reconciliation: null, replacementOperationId: null };
 routes['/api/ledger/operations'] = page([...fillerDrafts, { ...draft, id: 'original', status: 'COMMITTED', canEdit: false, canDelete: false }, { ...draft, id: 'replacement', outputs: [{ ...draft.outputs[0], id: 'new-dried', code: 'Replacement lot' }], version: 'replacement-v2' }]);
 routes['/api/ledger/corrections'] = page([...Array.from({ length: 25 }, (_, index) => ({ ...correction, id: `old-${index}`, status: 'CANCELLED' })), correction]);
 routes['/api/ledger/lots'] = page([lot, { ...lot, id: 'dried', code: 'Dried lot', positions: [{ locationId: 'isolation', facilityId: 'facility', quantityKg: '9.000001', availableKg: '0' }] }]);
 render(<LedgerWorkspace user={{ ...actor, role: 'MillManager', permissions: ['corrections.reconcile'] }} />);
 const browser = userEvent.setup();
 await browser.click(screen.getByRole('button', { name: 'Corrections' }));
 await waitFor(() => expect(screen.getByRole('button', { name: 'Next processing corrections page' })).toBeEnabled());
 await browser.click(screen.getByRole('button', { name: 'Next processing corrections page' }));
 await browser.click(await screen.findByText(/Customer Company — DRYING:.*correction REQUESTED/));
 const review = within(screen.getByText(/Customer Company — DRYING:.*correction REQUESTED/).closest('details')!);
 await browser.upload(review.getByLabelText('Evidence file'), new File(['weighbridge'], 'physical.txt', { type: 'text/plain' }));
 await browser.click(review.getByRole('button', { name: 'Upload evidence' }));
 await review.findByRole('link', { name: 'View stored evidence' });
 await browser.type(review.getByLabelText('Physical reconciliation / cancellation reason'), 'Checked physical stock');
 await browser.click(review.getByRole('button', { name: 'Load actual current output positions' }));
 expect(await review.findByLabelText('Verified physical position (kg)')).toHaveValue('9.000001');
 await waitFor(() => expect(review.getByRole('button', { name: 'Next Replacement draft to review page' })).toBeEnabled());
 await browser.click(review.getByRole('button', { name: 'Next Replacement draft to review page' }));
 await within(review.getByLabelText('Replacement draft to review')).findByRole('option', { name: /Replacement lot/ });
 await browser.selectOptions(review.getByLabelText('Replacement draft to review'), 'replacement');
 await browser.click(review.getByRole('checkbox', { name: /I verified every actual physical output position/ }));
 await browser.click(review.getByRole('button', { name: 'Record bound physical reconciliation' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url === '/api/ledger/corrections/correction/reconcile')).toBe(true));
 const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/ledger/corrections/correction/reconcile')!;
 expect(JSON.parse(String(init?.body))).toEqual({ evidenceId: 'evidence', reason: 'Checked physical stock', reconciliation: [{ lotId: 'dried', locationId: 'isolation', quantityKg: '9.000001' }], replacementOperationId: 'replacement', replacementVersion: 'replacement-v2' });
});
it('uses scoped owner lookup names in processing and stock details without admin organization fetches', async () => {
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write', 'lots.read.tenant'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByRole('button', { name: 'Process & correct' }));
 const operationOwner = await screen.findByLabelText('Stock owner');
 expect(within(operationOwner).getByRole('option', { name: 'Customer Company' })).toBeInTheDocument();
 await browser.click(await screen.findByText(/Customer Company — DRYING:.*Dried lot.*DRAFT/));
 expect(screen.getByText('Customer Company', { selector: 'dd' })).toBeInTheDocument();
 await browser.click(screen.getByRole('button', { name: 'Stock & HOLDs' }));
 expect(await screen.findByLabelText('Company filter')).toHaveValue('');
 expect(within(screen.getByLabelText('Company filter')).getByRole('option', { name: 'Customer Company' })).toBeInTheDocument();
 await browser.click(screen.getByRole('button', { name: 'Open lot detail for Source lot' }));
 const stockDetail = (await screen.findByRole('button', { name: 'Close lot detail' })).closest('section')!;
 expect(within(stockDetail).getByText('Customer Company', { selector: 'dd' })).toBeInTheDocument();
 expect(fetchMock.mock.calls.some(([url]) => String(url).startsWith('/api/admin/organizations'))).toBe(false);
});
it('limits ledger navigation and processing actions to assigned permissions', async () => {
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.confirm'] }} />);
 const navigation = screen.getByRole('navigation', { name: 'Ledger sections' });
 expect(within(navigation).getByRole('button', { name: 'Process & correct' })).toHaveAttribute('aria-current', 'page');
 expect(within(navigation).queryByRole('button', { name: 'Setup' })).not.toBeInTheDocument();
 expect(within(navigation).queryByRole('button', { name: 'Receive stock' })).not.toBeInTheDocument();
 expect(within(navigation).queryByRole('button', { name: 'Stock & HOLDs' })).not.toBeInTheDocument();
 await userEvent.setup().click(await screen.findByText(/DRYING:.*DRAFT/));
 expect(await screen.findByRole('button', { name: 'Confirm reviewed operation version' })).toBeInTheDocument();
 expect(screen.queryByRole('button', { name: 'Commit processing' })).not.toBeInTheDocument();
 expect(screen.queryByRole('button', { name: 'Edit processing draft' })).not.toBeInTheDocument();
});
it('shows receiving section to operators and managers through receiving permissions', () => {
 const { rerender } = render(<LedgerWorkspace user={{ ...actor, permissions: ['lots.receive'] }} />);
 expect(within(screen.getByRole('navigation', { name: 'Ledger sections' })).getByRole('button', { name: 'Receive stock' })).toBeInTheDocument();
 rerender(<LedgerWorkspace user={{ ...actor, permissions: ['receiving.reconcile'] }} />);
 expect(within(screen.getByRole('navigation', { name: 'Ledger sections' })).getByRole('button', { name: 'Receive stock' })).toBeInTheDocument();
});
it('requires an explicitly selected customer for admin crop-season creation', async () => {
 render(<LedgerWorkspace user={{ ...actor, permissions: ['platform.configure'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByRole('button', { name: 'Receive stock' }));
 await browser.selectOptions(screen.getByLabelText('Customer company'), 'company');
 const seasonForm = within(screen.getByRole('button', { name: 'Create crop season' }).closest('form')!);
 await browser.type(seasonForm.getByLabelText('Season name'), 'Wet season');
 await browser.type(seasonForm.getByLabelText('Starts'), '2026-06-01');
 await browser.type(seasonForm.getByLabelText('Ends'), '2026-12-31');
 await browser.click(screen.getByRole('button', { name: 'Create crop season' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/crop-seasons' && init?.method === 'POST')).toBe(true));
 const [, init] = fetchMock.mock.calls.find(([url, init]) => url === '/api/crop-seasons' && init?.method === 'POST')!;
 expect(JSON.parse(String(init?.body))).toEqual({ companyId: 'company', name: 'Wet season', startsOn: '2026-06-01', endsOn: '2026-12-31' });
});
it('preserves unsaved processing inputs across task and Ledger section navigation', async () => {
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write', 'lots.receive'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByRole('button', { name: 'Process & correct' }));
 await browser.click(await screen.findByText('Create processing draft', { selector: 'summary' }));
 const form = screen.getByRole('heading', { name: 'New processing draft' }).closest('section')!;
 await browser.selectOptions(within(form).getByLabelText('Stock owner'), 'customer');
 await browser.click(screen.getByRole('button', { name: 'Corrections' }));
 await browser.click(screen.getByRole('button', { name: 'Processing' }));
 await browser.click(screen.getByRole('button', { name: 'Receive stock' }));
 await browser.click(screen.getByRole('button', { name: 'Process & correct' }));
 expect(within(form).getByLabelText('Stock owner')).toHaveValue('customer');
 expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/ledger/operations' && init?.method === 'POST')).toBe(false);
});

it('filters mixed processing queues on server pages and preserves unsaved editor values', async () => {
 const drying = Array.from({ length: 26 }, (_, index) => ({ ...draft, id: `dry-${index}`, outputs: [{ ...draft.outputs[0], code: `Drying ${index}` }], varianceKg: '0.5', confirmedVersion: null }));
 routes['/api/ledger/lookups'] = { ...look, owners: [...look.owners, { id: 'other', name: 'Other Company' }] };
 routes['/api/ledger/operations'] = page([
  ...drying,
  { ...draft, id: 'mill', kind: 'MILLING', outputs: [{ ...draft.outputs[0], code: 'Milling row' }] },
  { ...draft, id: 'other', ownerId: 'other', outputs: [{ ...draft.outputs[0], code: 'Other owner row' }] },
  { ...draft, id: 'committed', status: 'COMMITTED', canEdit: false, canDelete: false, outputs: [{ ...draft.outputs[0], code: 'Immutable row' }] },
 ]);
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write', 'conversions.confirm'] }} />);
 const browser = userEvent.setup();
 expect(await screen.findByText(/DRYING: Drying 0.*DRAFT/)).toBeInTheDocument();
 expect(screen.queryByText(/DRYING: Immutable row/)).not.toBeInTheDocument();
 await browser.click(screen.getByText('Create processing draft', { selector: 'summary' }));
 const editor = within(screen.getByLabelText('Stock owner').closest('section')!);
 await browser.selectOptions(editor.getByLabelText('Stock owner'), 'customer');
 await browser.type(editor.getByLabelText('Actual output (kg)'), '7');
 await browser.selectOptions(screen.getByLabelText('Processing owner filter'), 'customer');
 await browser.selectOptions(screen.getByLabelText('Processing kind filter'), 'DRYING');
 await browser.selectOptions(screen.getByLabelText('Draft queue filter'), 'true');
 await browser.type(screen.getByLabelText('Search processing drafts'), 'Drying');
 await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => {
  const request = new URL(String(url), 'http://localhost');
  return request.pathname === '/api/ledger/operations' && request.searchParams.get('status') === 'DRAFT'
   && request.searchParams.get('ownerId') === 'customer' && request.searchParams.get('kind') === 'DRYING'
   && request.searchParams.get('queueOnly') === 'true' && request.searchParams.get('search') === 'Drying';
 })).toBe(true));
 await waitFor(() => expect(screen.getByRole('button', { name: 'Next processing drafts page' })).toBeEnabled());
 await browser.click(screen.getByRole('button', { name: 'Next processing drafts page' }));
 expect(await screen.findByText(/DRYING: Drying 25.*DRAFT/)).toBeInTheDocument();
 expect(screen.queryByText(/DRYING: Drying 0.*DRAFT/)).not.toBeInTheDocument();
 expect(screen.queryByText(/Milling row.*DRAFT/)).not.toBeInTheDocument();
 expect(screen.queryByText(/Other owner row.*DRAFT/)).not.toBeInTheDocument();
 expect(editor.getByLabelText('Actual output (kg)')).toHaveValue('7');
 expect(editor.getByLabelText('Stock owner')).toHaveValue('customer');
 await browser.selectOptions(screen.getByLabelText('Sort processing drafts by'), 'kind');
 await waitFor(() => expect(screen.getByRole('button', { name: 'Page 1 of processing drafts' })).toHaveAttribute('aria-current', 'page'));
 expect(editor.getByLabelText('Actual output (kg)')).toHaveValue('7');
});

it('deletes own drafts with expected version, clamps last page, and protects committed records', async () => {
 const drafts = Array.from({ length: 26 }, (_, index) => ({ ...draft, id: `own-${index}`, outputs: [{ ...draft.outputs[0], code: `Owned ${index}` }] }));
 const committed = { ...draft, id: 'immutable', status: 'COMMITTED', canEdit: false, canDelete: false, outputs: [{ ...draft.outputs[0], code: 'Committed stock' }] };
 routes['/api/ledger/operations'] = page([...drafts, committed]);
 const originalFetch = fetchMock.getMockImplementation()!;
 fetchMock.mockImplementation(async (url, init) => {
  if (String(url) === '/api/ledger/operations/own-25' && init?.method === 'DELETE') {
   routes['/api/ledger/operations'] = page([...drafts.slice(0, 25), committed]);
   return new Response(null, { status: 204 });
  }
  return originalFetch(url, init);
 });
 const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValue(true);
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await screen.findByText(/DRYING: Owned 0.*DRAFT/);
 await browser.click(screen.getByRole('button', { name: 'Last processing drafts page' }));
 await browser.click(await screen.findByText(/DRYING: Owned 25.*DRAFT/));
 await browser.click(await screen.findByRole('button', { name: 'Delete processing draft' }));
 expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'DELETE')).toBe(false);
 expect(screen.getByText(/DRYING: Owned 25.*DRAFT/)).toBeInTheDocument();
 await browser.click(screen.getByRole('button', { name: 'Delete processing draft' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/ledger/operations/own-25' && init?.method === 'DELETE')).toBe(true));
 const deletion = fetchMock.mock.calls.find(([url, init]) => url === '/api/ledger/operations/own-25' && init?.method === 'DELETE')!;
 expect(JSON.parse(String(deletion[1]?.body))).toEqual({ version: 'version-1' });
 expect(confirm).toHaveBeenCalledTimes(2);
 expect(await screen.findByText(/DRYING: Owned 0.*DRAFT/)).toBeInTheDocument();
 expect(screen.getByRole('button', { name: 'Next processing drafts page' })).toBeDisabled();
 await browser.click(screen.getByRole('button', { name: 'Committed list' }));
 await browser.click(await screen.findByText(/DRYING: Committed stock.*COMMITTED/));
 expect(screen.queryByRole('button', { name: 'Delete processing draft' })).not.toBeInTheDocument();
 expect(screen.queryByRole('button', { name: 'Edit processing draft' })).not.toBeInTheDocument();
 confirm.mockRestore();
});

it('keeps other creators and legacy drafts read-only despite write permission', async () => {
 routes['/api/ledger/operations'] = page([
  { ...draft, id: 'other', createdBy: 'someone-else', canEdit: false, canDelete: false, outputs: [{ ...draft.outputs[0], code: 'Other draft' }] },
  { ...draft, id: 'legacy', createdBy: null, canEdit: false, canDelete: false, outputs: [{ ...draft.outputs[0], code: 'Legacy draft' }] },
 ]);
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByText(/DRYING: Other draft.*DRAFT/));
 await browser.click(screen.getByText(/DRYING: Legacy draft.*DRAFT/));
 expect(screen.queryByRole('button', { name: 'Edit processing draft' })).not.toBeInTheDocument();
 expect(screen.queryByRole('button', { name: 'Delete processing draft' })).not.toBeInTheDocument();
});

it('moves committed drafts out of queue and exposes corrected originals as immutable history', async () => {
 const corrected = { ...draft, id: 'corrected-original', status: 'CORRECTED', canEdit: false, canDelete: false, outputs: [{ ...draft.outputs[0], code: 'Corrected original' }] };
 routes['/api/ledger/operations'] = page([draft, corrected]);
 const originalFetch = fetchMock.getMockImplementation()!;
 fetchMock.mockImplementation(async (url, init) => {
  if (url === '/api/ledger/operations/draft/commit' && init?.method === 'POST') {
   routes['/api/ledger/operations'] = page([{ ...draft, status: 'COMMITTED', canEdit: false, canDelete: false }, corrected]);
   return response({});
  }
  return originalFetch(url, init);
 });
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(await screen.findByText(/DRYING: Dried lot.*DRAFT/));
 await browser.click(screen.getByRole('button', { name: 'Commit processing' }));
 expect(await screen.findByText('No processing drafts match these filters.')).toBeInTheDocument();
 expect(screen.queryByText(/DRYING: Dried lot.*DRAFT/)).not.toBeInTheDocument();
 await browser.click(screen.getByRole('button', { name: 'Committed list' }));
 expect(await screen.findByText(/DRYING: Dried lot.*COMMITTED/)).toBeInTheDocument();
 await browser.selectOptions(screen.getByLabelText('Committed status filter'), 'CORRECTED');
 await browser.click(await screen.findByText(/DRYING: Corrected original.*CORRECTED/));
 expect(screen.queryByRole('button', { name: 'Delete processing draft' })).not.toBeInTheDocument();
 expect(screen.queryByRole('button', { name: 'Edit processing draft' })).not.toBeInTheDocument();
});

it('searches and pages committed correction candidates without mixing drafts', async () => {
 const committed = Array.from({ length: 26 }, (_, index) => ({ ...draft, id: `committed-${index}`, status: 'COMMITTED', canEdit: false, canDelete: false, outputs: [{ ...draft.outputs[0], code: `Committed ${index}` }] }));
 routes['/api/ledger/operations'] = page([draft, ...committed]);
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup();
 await browser.click(screen.getByRole('button', { name: 'Corrections' }));
 await browser.click(screen.getByText('Request processing correction', { selector: 'summary' }));
 const candidates = await screen.findByLabelText('Committed operation to correct');
 await within(candidates).findByRole('option', { name: /Committed 0/ });
 expect(within(candidates).queryByRole('option', { name: /Dried lot/ })).not.toBeInTheDocument();
 await browser.click(screen.getByRole('button', { name: 'Next Committed operation to correct page' }));
 await within(candidates).findByRole('option', { name: /Committed 25/ });
 await browser.selectOptions(candidates, 'committed-25');
 await browser.type(screen.getByLabelText('Search Committed operation to correct'), 'Committed 3');
 await within(candidates).findByRole('option', { name: /Committed 3/ });
 expect(candidates).toHaveValue('committed-25');
 await browser.type(screen.getByLabelText('Processing correction reason'), 'Incorrect output');
 await browser.click(screen.getByRole('button', { name: 'Request processing correction' }));
 await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/ledger/corrections' && init?.method === 'POST')).toBe(true));
 const request = fetchMock.mock.calls.find(([url, init]) => url === '/api/ledger/corrections' && init?.method === 'POST')!;
 expect(JSON.parse(String(request[1]?.body))).toMatchObject({ operationId: 'committed-25', reason: 'Incorrect output' });
});

it('does not let stale processing searches replace newer server results', async () => {
 const old = { ...draft, id: 'old', outputs: [{ ...draft.outputs[0], code: 'Old result' }] };
 const current = { ...draft, id: 'current', outputs: [{ ...draft.outputs[0], code: 'New result' }] };
 routes['/api/ledger/operations'] = page([old, current]);
 const originalFetch = fetchMock.getMockImplementation()!;
 let releaseOld: ((value: Response) => void) | undefined;
 fetchMock.mockImplementation(async (url, init) => {
  const request = new URL(String(url), 'http://localhost');
  if (request.pathname === '/api/ledger/operations' && request.searchParams.get('search') === 'Old result')
   return new Promise<Response>(resolve => { releaseOld = resolve; });
  return originalFetch(url, init);
 });
 render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
 const browser = userEvent.setup(), search = screen.getByLabelText('Search processing drafts');
 await screen.findByText(/DRYING: Old result.*DRAFT/);
 await browser.type(search, 'Old result');
 await waitFor(() => expect(releaseOld).toBeDefined());
 await browser.clear(search);
 await browser.type(search, 'New result');
 expect(await screen.findByText(/DRYING: New result.*DRAFT/)).toBeInTheDocument();
 await act(async () => { releaseOld?.(response(page([old]))); });
 expect(screen.getByText(/DRYING: New result.*DRAFT/)).toBeInTheDocument();
 expect(screen.queryByText(/DRYING: Old result.*DRAFT/)).not.toBeInTheDocument();
});

it('opens tracing from the selected stock lot without automatically executing an audited trace', async () => {
  render(<LedgerWorkspace user={{ ...actor, role: 'CustomerManager', organizationId: 'company', permissions: ['lots.read.tenant', 'trace.read.tenant'] }} initialLotId="source" />);
  const browser = userEvent.setup();
  await browser.click(await screen.findByRole('button', { name: 'Trace this lot' }));
  expect(await screen.findByRole('textbox', { name: 'Ledger lot ID' })).toHaveValue('source');
  expect(screen.getByRole('button', { name: 'Traceability' })).toHaveAttribute('aria-current', 'page');
  expect(fetchMock.mock.calls.some(([url, init]) => String(url) === '/api/ledger/trace' && init?.method === 'POST')).toBe(false);
});

it('replaces a laboratory entry when tracing a stock lot in the same workspace', async () => {
  render(<LedgerWorkspace user={{ ...actor, role: 'CustomerManager', organizationId: 'company', permissions: ['lots.read.tenant', 'trace.read.tenant'] }} initialLotId="source" initialLabOrigin={{ reportId: 'report', resultId: 'row' }} />);
  const browser = userEvent.setup();
  expect(screen.getByRole('button', { name: 'Traceability' })).toHaveAttribute('aria-current', 'page');
  expect(screen.getByText(/Starting from a confirmed laboratory result/)).toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: 'Stock & HOLDs' }));
  await browser.click(await screen.findByRole('button', { name: 'Trace this lot' }));
  expect(await screen.findByRole('textbox', { name: 'Ledger lot ID' })).toHaveValue('source');
  expect(screen.queryByText(/Starting from a confirmed laboratory result/)).not.toBeInTheDocument();
  expect(screen.getByRole('combobox', { name: 'Trace direction' })).toBeEnabled();
  expect(fetchMock.mock.calls.some(([url, init]) => String(url).startsWith('/api/ledger/trace') && init?.method === 'POST')).toBe(false);
});
