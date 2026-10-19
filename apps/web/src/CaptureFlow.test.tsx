import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { clearLocalSession } from './api';

const fetchMock = vi.fn<typeof fetch>();
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const staff = { id: 'staff-id', email: 'staff@example.com', displayName: 'Purchasing Staff', phoneNumber: null, role: 'PurchasingStaff', organizationId: 'company-id', organizationName: 'Customer A', isActive: true, permissions: ['farms.write', 'purchases.write'] };
const farmerId = '6114dc9c-f08c-4b25-a90f-91c7c88a05b0';
const plotId = '11408862-f173-49d8-9f02-fe57483bd6e6';
const plotSeasonId = '06d238e3-d388-4a91-b142-24b90b5dba25';

beforeEach(() => { vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset(); clearLocalSession(); window.sessionStorage.clear(); window.localStorage.clear(); window.history.replaceState({}, '', '/'); });

describe('Capture journey', () => {
  it('does not warn that the outcome is uncertain while a pesticide declaration is saving', async () => {
    let finishSave: ((response: Response) => void) | undefined;
    fetchMock.mockImplementation(async url => {
      if (url === '/api/auth/session') return json(staff);
      if (url === '/api/auth/csrf') return json({ token: 'csrf' });
      if (url === `/api/plot-seasons/${plotSeasonId}`) return json({ id: plotSeasonId, plotId, seasonId: 'season-1', riceVariety: 'ST25' });
      if (url === `/api/plots/${plotId}`) return json({ id: plotId, farmerId, code: 'TR-001' });
      if (String(url).startsWith('/api/crop-seasons?')) return json({ items: [], page: 1, pageSize: 100, hasMore: false });
      if (String(url).startsWith('/api/active-substances?')) return json({ items: [], page: 1, pageSize: 100, hasMore: false });
      if (String(url).startsWith('/api/spray-declarations?')) return json({ items: [], page: 1, pageSize: 10, hasMore: false });
      if (url === '/api/spray-declarations') return new Promise<Response>(resolve => { finishSave = resolve; });
      return json({}, 404);
    });
    window.history.replaceState({}, '', `/capture/plot-seasons/${plotSeasonId}`);
    render(<App />); const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: /Record application/ }));
    await browser.type(screen.getByLabelText('Application date'), '2020-01-01');
    await browser.type(screen.getByLabelText('Information source'), 'Farmer notebook');
    await browser.type(screen.getByLabelText(/Product name/), 'Pesticide A');
    await browser.click(screen.getByRole('button', { name: 'Save declaration' }));
    expect(await screen.findByRole('button', { name: 'Saving…' })).toBeDisabled();
    expect(screen.queryByText(/The outcome is uncertain/)).not.toBeInTheDocument();
    finishSave?.(json({ id: 'spray-1', plotSeasonId, appliedOn: '2020-01-01', informationSource: 'Farmer notebook', evidenceRef: null, capturedAt: '2020-01-01T00:00:00Z', recordedAt: '2020-01-01T00:00:00Z', items: [] }, 201));
    expect(await screen.findByRole('heading', { name: 'Pesticide declaration' })).toBeInTheDocument();
  });

  it('records and reopens a pesticide declaration without spray rate', async () => {
    let saved: unknown = null;
    fetchMock.mockImplementation(async (url, init) => {
      if (url === '/api/auth/session') return json(staff);
      if (url === '/api/auth/csrf') return json({ token: 'csrf' });
      if (url === `/api/plot-seasons/${plotSeasonId}`) return json({ id: plotSeasonId, plotId, seasonId: 'season-1', riceVariety: 'ST25' });
      if (url === `/api/plots/${plotId}`) return json({ id: plotId, farmerId, code: 'TR-001' });
      if (String(url).startsWith('/api/crop-seasons?')) return json({ items: [], page: 1, pageSize: 100, hasMore: false });
      if (String(url).startsWith('/api/active-substances?')) return json({ items: [], page: 1, pageSize: 100, hasMore: false });
      if (url === '/api/spray-declarations') {
        const request = JSON.parse(String(init?.body));
        saved = { id: request.payload.entityId, ...request.payload, capturedAt: request.capturedAt, recordedAt: request.capturedAt, items: request.payload.items.map((item: object) => ({ ...item, id: 'item-1' })) };
        return json(saved, 201);
      }
      if (String(url).startsWith('/api/spray-declarations?')) return json({ items: [], page: 1, pageSize: 10, hasMore: false });
      if (String(url).startsWith('/api/spray-declarations/')) return json(saved);
      return json({}, 404);
    });
    window.history.replaceState({}, '', `/capture/plot-seasons/${plotSeasonId}`);
    render(<App />); const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: /Record application/ }));
    await browser.type(screen.getByLabelText('Application date'), '2020-01-01');
    await browser.type(screen.getByLabelText('Information source'), 'Farmer notebook');
    await browser.type(screen.getByLabelText(/Product name/), 'Pesticide A');
    await browser.click(screen.getByRole('button', { name: 'Save declaration' }));
    expect(await screen.findByRole('heading', { name: 'Pesticide declaration' })).toBeInTheDocument();
    expect(screen.getByText('Farmer notebook')).toBeInTheDocument();
    const post = fetchMock.mock.calls.find(([url]) => url === '/api/spray-declarations')!;
    const request = JSON.parse(String(post[1]?.body));
    expect(request.payloadSchemaVersion).toBe(2);
    expect(request.payload.items[0]).toMatchObject({ productName: 'Pesticide A', substanceId: null });
    expect(JSON.stringify(request)).not.toMatch(/rateUnit|"rate"/);
  });

  it('shows the Ledger blocker without pretending a source registration was accepted', async () => {
    fetchMock.mockImplementation(async url => {
      if (url === '/api/auth/session') return json(staff);
      if (String(url).startsWith('/api/procurements?')) return json({ items: [], page: 1, pageSize: 20, hasMore: false });
      if (String(url).startsWith('/api/procurement-reference-data?')) return json({ title: 'Ledger integration unavailable', status: 503, code: 'LEDGER_STOCK_UNAVAILABLE' }, 503);
      if (String(url).startsWith('/api/farmers?')) return json({ items: [], page: 1, pageSize: 100, hasMore: false });
      return json({}, 404);
    });
    render(<App />); const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: /Paddy intake/ }));
    await browser.click(await screen.findByRole('button', { name: /Record intake/ }));
    expect(await screen.findByText(/Receiving options are unavailable/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Register procurement' })).toBeDisabled();
    expect(fetchMock.mock.calls.some(([url]) => url === '/api/procurements')).toBe(false);
  });

  it('identifies the receipt line and field when moisture includes a percent sign', async () => {
    const siteId = '11111111-1111-4111-8111-111111111111';
    const locationId = '22222222-2222-4222-8222-222222222222';
    fetchMock.mockImplementation(async url => {
      const path = String(url);
      if (path === '/api/auth/session') return json(staff);
      if (path.startsWith('/api/procurements?')) return json({ items: [], page: 1, pageSize: 20, hasMore: false });
      if (path.startsWith('/api/procurement-reference-data?')) return json({ receivingOptions: [{ collectionSiteId: siteId, collectionSiteCode: 'SITE-1', collectionSiteName: 'Site One', receivingLocationId: locationId, receivingLocationCode: 'LOC-1' }], commodity: { id: 'commodity-id', code: 'PADDY', name: 'Paddy' }, massUnits: [{ code: 'KG', name: 'Kilogram', kgFactor: '1' }], page: 1, pageSize: 100, hasMore: false, maxLinesPerReceipt: 100 });
      if (path.startsWith('/api/farmers?')) return json({ items: [{ id: farmerId, code: 'ND-1', name: 'Farmer A' }], page: 1, pageSize: 100, hasMore: false });
      if (path.startsWith('/api/plots?')) return json({ items: [{ id: plotId, farmerId, code: 'TR-1' }], page: 1, pageSize: 100, hasMore: false });
      if (path.startsWith('/api/plot-seasons?')) return json({ items: [{ id: plotSeasonId, plotId, seasonId: 'season-1', riceVariety: 'ST25' }], page: 1, pageSize: 100, hasMore: false });
      return json({}, 404);
    });
    render(<App />); const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: /Paddy intake/ }));
    await browser.click(await screen.findByRole('button', { name: /Record intake/ }));
    expect(screen.getByLabelText('Receiving location')).toBeDisabled();
    expect(screen.getByText('Select a collection site first.')).toBeInTheDocument();
    expect(screen.getByLabelText('Plot season')).toBeDisabled();
    expect(screen.getByText('Select a farmer first.')).toBeInTheDocument();
    await browser.selectOptions(await screen.findByLabelText('Farmer'), farmerId);
    expect(screen.getByLabelText('Plot season')).toBeEnabled();
    await browser.selectOptions(await screen.findByLabelText('Collection site'), siteId);
    expect(screen.getByLabelText('Receiving location')).toBeEnabled();
    await browser.selectOptions(screen.getByLabelText('Receiving location'), locationId);
    await browser.selectOptions(await screen.findByLabelText('Plot season'), plotSeasonId);
    await browser.type(screen.getByLabelText('Measured mass'), '200');
    await browser.type(screen.getByLabelText('Moisture (%)'), '30%');
    await browser.click(screen.getByRole('button', { name: 'Register procurement' }));
    expect(screen.getByText('Line 1: enter moisture from 0 to 100 as a number, without the % sign.')).toBeInTheDocument();
    expect(screen.getByLabelText(/Moisture \(%\)/)).toHaveAttribute('aria-invalid', 'true');
    expect(fetchMock.mock.calls.some(([url]) => url === '/api/procurements')).toBe(false);
    await browser.clear(screen.getByLabelText(/Moisture \(%\)/));
    await browser.type(screen.getByLabelText(/Moisture \(%\)/), '30');
    expect(screen.queryByText(/Line 1: enter moisture/)).not.toBeInTheDocument();
  });

  it('connects procurement, source lot, sample, and handover when the backend accepts them', async () => {
    const siteId = '11111111-1111-4111-8111-111111111111';
    const locationId = '22222222-2222-4222-8222-222222222222';
    const commodityId = '33333333-3333-4333-8333-333333333333';
    let receipt: Record<string, unknown> | null = null;
    let sourceLotId = '';
    let sample: Record<string, unknown> | null = null;
    let finishSample: ((response: Response) => void) | undefined;
    let finishHandover: ((response: Response) => void) | undefined;
    const handovers: object[] = [];
    fetchMock.mockImplementation(async (url, init) => {
      const path = String(url);
      if (path === '/api/auth/session') return json(staff);
      if (path === '/api/auth/csrf') return json({ token: 'csrf' });
      if (path.startsWith('/api/procurements?')) return json({ items: [], page: 1, pageSize: 20, hasMore: false });
      if (path.startsWith('/api/procurement-reference-data?')) return json({ receivingOptions: [{ collectionSiteId: siteId, collectionSiteCode: 'SITE-1', collectionSiteName: 'Site One', receivingLocationId: locationId, receivingLocationCode: 'LOC-1' }], commodity: { id: commodityId, code: 'PADDY', name: 'Paddy' }, massUnits: [{ code: 'KG', name: 'Kilogram', kgFactor: '1' }], page: 1, pageSize: 100, hasMore: false, maxLinesPerReceipt: 100 });
      if (path.startsWith('/api/farmers?')) return json({ items: [{ id: farmerId, code: 'ND-1', name: 'Farmer A' }], page: 1, pageSize: 100, hasMore: false });
      if (path.startsWith('/api/plots?')) return json({ items: [{ id: plotId, farmerId, code: 'TR-1' }], page: 1, pageSize: 100, hasMore: false });
      if (path.startsWith('/api/plot-seasons?')) return json({ items: [{ id: plotSeasonId, plotId, seasonId: 'season-1', riceVariety: 'ST25' }], page: 1, pageSize: 100, hasMore: false });
      if (path === '/api/procurements') {
        const request = JSON.parse(String(init?.body)); const p = request.payload; sourceLotId = p.items[0].sourceLotId;
        receipt = { id: p.entityId, code: 'PN-001', farmerId, collectionSiteId: siteId, receivingLocationId: locationId, commodityId, status: 'PENDING_ARRIVAL', receivedAt: p.receivedAt, capturedAt: request.capturedAt, recordedAt: request.capturedAt, totalNetMassKg: '600', items: [{ id: p.items[0].itemId, lineNo: 1, plotSeasonId, plotId, seasonId: 'season-1', riceVariety: 'ST25', measuredValue: '600', measuredUnit: 'KG', netMassKg: '600', moisturePercent: '22.5', sourceLotId, lotId: p.items[0].lotId, lotCode: 'LOT-1', stage: 'RAW_PADDY', initialStock: { locationId, receivedKg: '0' } }] };
        return json(receipt, 201);
      }
      if (path.startsWith('/api/procurements/')) return json(receipt);
      if (path.startsWith('/api/source-lots/')) return json({ id: sourceLotId, lotId: 'lot-id', lotCode: 'LOT-1', procurementId: receipt?.id, procurementCode: 'PN-001', farmerId, plotId, plotSeasonId, riceVariety: 'ST25', receivedAt: receipt?.receivedAt, initialNetMassKg: '600', moisturePercent: '22.5', receivingStatus: 'PENDING_ARRIVAL', usableStockKg: '0', currentStock: [], stockAsOf: new Date().toISOString() });
      if (path === '/api/samples') {
        const request = JSON.parse(String(init?.body)); sample = { id: request.payload.entityId, sourceLotId, code: 'SMP-261017-0008', sealCode: request.payload.sealCode, collectedAt: request.payload.collectedAt, capturedAt: request.capturedAt, recordedAt: request.capturedAt, handoverCount: 0 }; return new Promise<Response>(resolve => { finishSample = resolve; });
      }
      if (path.startsWith('/api/samples?')) return json({ items: [], page: 1, pageSize: 20, hasMore: false });
      if (path.endsWith('/handovers') && init?.method === 'POST') { const request = JSON.parse(String(init.body)); handovers.push({ id: request.payload.entityId, sampleId: sample?.id, ...request.payload }); if (sample) sample.handoverCount = 1; return new Promise<Response>(resolve => { finishHandover = resolve; }); }
      if (path.includes('/handovers?')) return json({ items: handovers, page: 1, pageSize: 20, hasMore: false });
      if (path.startsWith('/api/samples/')) return json(sample);
      return json({}, 404);
    });
    render(<App />); const browser = userEvent.setup();
    await browser.click(await screen.findByRole('button', { name: /Paddy intake/ }));
    await browser.click(await screen.findByRole('button', { name: /Record intake/ }));
    await browser.selectOptions(await screen.findByLabelText('Farmer'), farmerId);
    await browser.selectOptions(await screen.findByLabelText('Collection site'), siteId);
    await browser.selectOptions(screen.getByLabelText('Receiving location'), locationId);
    await browser.selectOptions(await screen.findByLabelText('Plot season'), plotSeasonId);
    await browser.type(screen.getByLabelText('Measured mass'), '600');
    await browser.type(screen.getByLabelText('Moisture (%)'), '22.5');
    await browser.click(screen.getByRole('button', { name: 'Register procurement' }));
    expect(await screen.findByRole('heading', { name: 'PN-001' })).toBeInTheDocument();
    expect(screen.getByText(/No usable stock is available/)).toBeInTheDocument();
    await browser.click(screen.getByRole('button', { name: /LOT-1/ }));
    expect((await screen.findAllByText('600 kg')).length).toBeGreaterThan(0);
    expect(screen.getByText('0 kg')).toBeInTheDocument();
    await browser.click(screen.getByRole('button', { name: /Take sample/ }));
    expect(screen.getByText(/sample code is assigned after saving/i)).toBeInTheDocument();
    await browser.type(screen.getByLabelText('Seal code'), 'SEAL-001');
    await browser.click(screen.getByRole('button', { name: 'Save sample' }));
    expect(await screen.findByRole('button', { name: 'Saving…' })).toBeDisabled();
    expect(screen.queryByText(/The outcome is uncertain/)).not.toBeInTheDocument();
    const samplePost = fetchMock.mock.calls.find(([url]) => url === '/api/samples')!;
    expect(JSON.parse(String(samplePost[1]?.body)).payload).not.toHaveProperty('code');
    expect(JSON.parse(String(samplePost[1]?.body)).payloadSchemaVersion).toBe(2);
    finishSample?.(json(sample, 201));
    expect(await screen.findByRole('heading', { name: 'SMP-261017-0008' })).toBeInTheDocument();
    await browser.click(screen.getByRole('button', { name: /Record handover/ }));
    await browser.type(screen.getByLabelText('From party'), 'Collection agent');
    await browser.type(screen.getByLabelText('To party'), 'Laboratory');
    await browser.type(screen.getByLabelText('Seal condition'), 'Intact');
    await browser.click(screen.getByRole('button', { name: 'Save handover' }));
    expect(await screen.findByRole('button', { name: 'Saving…' })).toBeDisabled();
    expect(screen.queryByText(/The outcome is uncertain/)).not.toBeInTheDocument();
    finishHandover?.(json(handovers[0], 201));
    await waitFor(() => expect(screen.getByText('Laboratory')).toBeInTheDocument());
    expect(fetchMock.mock.calls.some(([url]) => url === '/api/procurements')).toBe(true);
    expect(fetchMock.mock.calls.some(([url]) => url === '/api/samples')).toBe(true);
  });
});
