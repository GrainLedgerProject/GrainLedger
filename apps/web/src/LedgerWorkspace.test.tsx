import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { LedgerWorkspace } from './LedgerWorkspace';
import { type User } from './api';

const actor: User = { id: 'actor', email: 'staff@example.com', displayName: 'Staff', phoneNumber: null, role: 'MillOperator', organizationId: 'mill', organizationName: 'Mill', isActive: true, permissions: [] };
const look = { facilities: [{ id: 'facility', name: 'Mill facility' }], locations: [{ id: 'store', facilityId: 'facility', name: 'Main store', kind: 'STORE' }, { id: 'isolation', facilityId: 'facility', name: 'Isolation', kind: 'ISOLATION' }], commodities: [{ id: 'rice', name: 'Rice' }], equipment: [{ id: 'dryer', facilityId: 'facility', name: 'Dryer' }] };
const lot = { id: 'source', code: 'Source lot', ownerId: 'customer', commodityId: 'rice', stage: 'RAW_PADDY', productIdentity: 'RAW_PADDY', positions: [{ locationId: 'store', facilityId: 'facility', quantityKg: '10.000001', availableKg: '10.000001' }], restrictions: [] };
const draft = { id: 'draft', ownerId: 'customer', facilityId: 'facility', kind: 'DRYING', occurredAt: '2026-01-01T00:00:00Z', equipmentId: 'dryer', inputs: [{ lotId: 'source', locationId: 'store', quantityKg: '10.000001' }], outputs: [{ id: 'dried', code: 'Dried lot', commodityId: 'rice', stage: 'DRIED_PADDY', productIdentity: 'DRIED_PADDY', locationId: 'store', quantityKg: '9.000001' }], declaredLossKg: '1', status: 'DRAFT', version: 'version-1', confirmedVersion: 'version-1', varianceKg: '0', allowedVarianceKg: '0' };
const receipt = { id: 'receipt', ownerOrganizationId: 'customer', farmerId: 'farmer', seasonId: 'season', locationId: 'store', occurredAt: '2026-01-01T00:00:00Z', lines: [{ id: 'line', plotSeasonId: 'plot-season', commodityId: 'rice', lotCode: 'Procured lot', quantity: '10.000001', unit: 'kg', quantityKg: '10.000001', lotId: 'source' }], status: 'COMMITTED', version: 'receipt-v1', replacementForReceiptId: null };
const fetchMock = vi.fn<typeof fetch>();
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
let routes: Record<string, unknown>;

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset();
  routes = {
    '/api/auth/csrf': { token: 'csrf' }, '/api/ledger/lookups': look, '/api/ledger/lots': [lot], '/api/ledger/holds': [], '/api/ledger/notifications': [], '/api/ledger/operations': [draft], '/api/ledger/corrections': [],
    '/api/capture-mock/status': { synthetic: true }, '/api/capture-mock/receipts': [receipt], '/api/capture-mock/arrivals': [], '/api/capture-mock/receipt-corrections': [], '/api/capture-mock/farmers': [{ id: 'farmer', name: 'Farmer' }], '/api/capture-mock/plots': [{ id: 'plot', name: 'Plot', farmerId: 'farmer' }], '/api/capture-mock/seasons': [{ id: 'season', name: 'Season' }], '/api/capture-mock/plot-seasons': [{ id: 'plot-season', plotId: 'plot', seasonId: 'season' }],
  };
  fetchMock.mockImplementation(async (url, init) => {
    const path = String(url).split('?')[0];
    if (init?.method && init.method !== 'GET') return response(path.endsWith('/evidence') ? { id: 'evidence' } : {});
    return path in routes ? response(routes[path]) : response({ title: `Unexpected route ${path}` }, 404);
  });
});

it('loads purchasing lookups without restricted setup and preserves receipt correction provenance', async () => {
  render(<LedgerWorkspace user={{ ...actor, role: 'PurchasingStaff', organizationId: 'customer', permissions: ['farms.write', 'purchases.write'] }} />);
  const browser = userEvent.setup();
  await browser.click(await screen.findByRole('button', { name: 'Sources & receiving' }));
  expect(fetchMock.mock.calls.some(([url]) => url === '/api/capture-mock/status')).toBe(true);
  expect(await screen.findByText(/Durable synthetic mock sources/)).toBeInTheDocument();
  await browser.click(await screen.findByText('Procured lot — COMMITTED'));
  await browser.click(screen.getByRole('button', { name: 'Request unused receipt correction' }));
  const editor = within(screen.getByRole('heading', { name: 'Request unused receipt correction' }).closest('section')!);
  expect(editor.queryByLabelText('Receiving location')).not.toBeInTheDocument();
  expect(editor.queryByLabelText('Receipt farmer')).not.toBeInTheDocument();
  await browser.clear(editor.getByLabelText('Original measured quantity'));
  await browser.type(editor.getByLabelText('Original measured quantity'), '9.999999');
  await browser.type(editor.getByLabelText('Correction reason'), 'Verified entry error');
  await browser.click(editor.getByRole('button', { name: 'Request receipt correction' }));
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url === '/api/capture-mock/receipts/receipt/corrections')).toBe(true));
  const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/capture-mock/receipts/receipt/corrections')!;
  const payload = JSON.parse(String(init?.body));
  expect(payload.replacement).toMatchObject({ ownerOrganizationId: 'customer', farmerId: 'farmer', seasonId: 'season', locationId: 'store', lines: [{ plotSeasonId: 'plot-season', commodityId: 'rice', quantity: '9.999999', unit: 'kg' }] });
  expect(payload.replacement.id).not.toBe('receipt'); expect(payload.replacement.lines[0].id).not.toBe('line');
  expect(new Headers(init?.headers).get('X-CSRF-TOKEN')).toBe('csrf');
  expect(fetchMock.mock.calls.some(([url]) => url === '/api/ledger/setup')).toBe(false);
});
it('hides synthetic receiving when mock status is unavailable without blocking ledger stock', async () => {
  delete routes['/api/capture-mock/status'];
  render(<LedgerWorkspace user={{ ...actor, permissions: ['lots.read.facility', 'farms.read'] }} />);
  expect(await screen.findByRole('heading', { name: 'Scoped stock search' })).toBeInTheDocument();
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url === '/api/capture-mock/status')).toBe(true));
  expect(screen.queryByRole('button', { name: 'Sources & receiving' })).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Stock & HOLD inbox' })).toBeInTheDocument();
});

it('edits processing draft with decimal strings and supports multiple actual output rows', async () => {
  render(<LedgerWorkspace user={{ ...actor, permissions: ['conversions.write'] }} />);
  const browser = userEvent.setup();
  await browser.click(await screen.findByText('DRYING — Dried lot — DRAFT'));
  await browser.click(screen.getByRole('button', { name: 'Edit processing draft' }));
  const editor = within(screen.getByRole('heading', { name: 'Edit processing draft' }).closest('section')!);
  await browser.clear(editor.getByLabelText('Actual output (kg)'));
  await browser.type(editor.getByLabelText('Actual output (kg)'), '8.000001');
  await browser.clear(editor.getByLabelText('Declared loss (kg, nonnegative)'));
  await browser.type(editor.getByLabelText('Declared loss (kg, nonnegative)'), '2');
  await browser.click(editor.getByRole('button', { name: 'Save draft changes' }));
  await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/ledger/operations/draft' && init?.method === 'PUT')).toBe(true));
  const [, init] = fetchMock.mock.calls.find(([url, init]) => url === '/api/ledger/operations/draft' && init?.method === 'PUT')!;
  expect(JSON.parse(String(init?.body))).toMatchObject({ id: 'draft', inputs: [{ quantityKg: '10.000001' }], outputs: [{ quantityKg: '8.000001', productIdentity: 'DRIED_PADDY' }], declaredLossKg: '2' });
  await browser.click(screen.getByRole('button', { name: 'Add actual output' }));
  expect(screen.getAllByLabelText('Actual output (kg)')).toHaveLength(2);
});

it('reconciles actual output positions against selected replacement version using uploaded evidence', async () => {
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

it('shows HOLD failures instead of swallowing errors or claiming success', async () => {
  fetchMock.mockImplementation(async (url, init) => {
    if (url === '/api/ledger/holds' && init?.method === 'POST') return response({ title: 'HOLD authority denied' }, 403);
    const path = String(url).split('?')[0]; return path in routes ? response(routes[path]) : response({}, 404);
  });
  routes['/api/ledger/lots'] = [lot, { ...lot, id: 'source-old', restrictions: ['SUPERSEDED'], positions: [{ locationId: 'store', facilityId: 'facility', quantityKg: '0.000000', availableKg: '0.000000' }] }];
  render(<LedgerWorkspace user={{ ...actor, role: 'CustomerManager', organizationId: 'customer', permissions: ['lots.read.tenant', 'holds.manage'] }} />);
  const browser = userEvent.setup();
  const holdLot = await screen.findByLabelText('Own-company lot to HOLD');
  await within(holdLot).findByRole('option', { name: /Source lot \[source\].*physical 10\.000001 kg, available 10\.000001 kg/ });
  expect(within(holdLot).getByRole('option', { name: /Source lot \[source-o\].*SUPERSEDED.*physical 0\.000000 kg, available 0\.000000 kg/ })).toBeInTheDocument();
  await browser.selectOptions(holdLot, 'source');
  await browser.type(screen.getByLabelText('HOLD reason'), 'Stop use');
  await browser.click(screen.getByRole('button', { name: 'Place customer HOLD' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('HOLD authority denied');
  expect(screen.queryByText('HOLD and recipient notifications saved atomically.')).not.toBeInTheDocument();
});
