import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ExportOrders } from './ExportOrders';
import { App } from './App';

const fetchMock = vi.fn<typeof fetch>();
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body),
  { status, headers: { 'Content-Type': 'application/json' } });

beforeEach(() => { vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset(); sessionStorage.clear(); });

describe('Export order form', () => {
  it('opens the export workspace only after the backend reports it ready', async () => {
    fetchMock.mockImplementation(async input => {
      const path = String(input);
      if (path === '/api/auth/session') return json({ id: 'staff', email: 'staff@example.com', displayName: 'Export Staff',
        role: 'ExportStaff', organizationId: 'company', organizationName: 'Customer', isActive: true,
        phoneNumber: null, permissions: ['export-orders.read', 'export-orders.write'] });
      if (path === '/api/export-availability') return json({ ready: true });
      if (path.startsWith('/api/export-orders?')) return json({ items: [], totalCount: 0, hasMore: false });
      if (path.startsWith('/api/markets?') || path.startsWith('/api/commodities?')) return json({ items: [], hasMore: false });
      return json({}, 404);
    });
    window.history.replaceState({}, '', '/');
    const browser = userEvent.setup();
    render(<App />);
    await browser.click(await screen.findByRole('button', { name: 'Export orders' }));
    expect(await screen.findByRole('heading', { name: 'Export orders' })).toBeInTheDocument();
    expect(window.location.pathname).toBe('/export/orders');
  });

  it.each(['network', 'invalid-json', 'empty-body', 'missing-id'])('replays the exact create request after %s without allowing edits', async failure => {
    const navigate = vi.fn();
    const requests: Array<{ operationId: string; requiredMass: string; limits: unknown[]; containers: unknown[] }> = [];
    fetchMock.mockImplementation(async (input, init) => {
      const path = String(input);
      if (path.startsWith('/api/markets?')) return json({ items: [{ id: 'market-1', code: 'EU', name: 'Europe' }], hasMore: false });
      if (path.startsWith('/api/commodities?')) return json({ items: [{ id: 'commodity-1', code: 'RICE', name: 'Rice' }], hasMore: false });
      if (path.startsWith('/api/quality-attributes?')) return json({ items: [{ id: 'attribute-1', code: 'MOIST', name: 'Moisture', unit: '%' }], hasMore: false });
      if (path.startsWith('/api/measurement-units?')) return json({ items: [{ id: 'unit-t', code: 'T', name: 'Tonne', quantityKind: 'MASS' }], hasMore: false });
      if (path === '/api/auth/csrf') return json({ token: 'csrf' });
      if (path === '/api/export-orders') {
        requests.push(JSON.parse(String(init?.body)));
        if (requests.length === 1) {
          if (failure === 'network') throw new TypeError('network disconnected');
          if (failure === 'invalid-json') return new Response('{"id":', { status: 201 });
          if (failure === 'empty-body') return new Response('', { status: 201 });
          return json({}, 201);
        }
        return json({ id: 'order-1' }, 201);
      }
      return json({}, 404);
    });
    const browser = userEvent.setup();
    const view = render(<ExportOrders canWrite path="/export/orders/new" onNavigate={navigate} />);
    await browser.selectOptions(await screen.findByLabelText('Export market'), 'market-1');
    await browser.selectOptions(await screen.findByLabelText('Commodity'), 'commodity-1');
    await browser.type(screen.getByLabelText('Required mass'), '48');
    await screen.findAllByRole('option', { name: 'Tonne (T)' });
    await browser.selectOptions(screen.getAllByLabelText('Unit')[0], 'T');
    await browser.type(screen.getByLabelText('Shipping from'), '2026-11-10');
    await browser.type(screen.getByLabelText('Shipping to'), '2026-11-20');
    await browser.selectOptions(await screen.findByLabelText('Attribute 1'), 'attribute-1');
    await browser.type(screen.getByLabelText('Maximum (optional)'), '14');
    await browser.type(screen.getByLabelText('Target mass'), '24');
    await browser.selectOptions(screen.getAllByLabelText('Unit')[1], 'T');
    await browser.click(screen.getByRole('button', { name: 'Create export order' }));
    expect(await screen.findByText(/result is uncertain/i)).toBeInTheDocument();
    expect(screen.getByLabelText('Required mass')).toBeDisabled();
    expect(sessionStorage.length).toBe(1);
    expect(navigate).not.toHaveBeenCalled();
    view.unmount();
    render(<ExportOrders canWrite path="/export/orders/new" onNavigate={navigate} />);
    expect(screen.getByLabelText('Required mass')).toBeDisabled();
    await browser.click(screen.getByRole('button', { name: 'Retry same request' }));
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/export/orders/order-1'));
    expect(requests).toHaveLength(2);
    expect(requests[1]).toEqual(requests[0]);
    expect(requests[0].operationId).toBe(requests[1].operationId);
    expect(requests[0].requiredMass).toBe('48');
    expect(requests[0].limits).toEqual([{ attributeId: 'attribute-1', minValue: null, maxValue: '14' }]);
    expect(requests[0].containers).toEqual([{ targetMass: '24', unit: 'T' }]);
    expect(sessionStorage.length).toBe(0);
  });

  it.each(['order', 'container'])('retains the exact %s edit after a corrupt success response and remount', async kind => {
    const navigate = vi.fn();
    const requests: Array<{ body: string; etag: string | null }> = [];
    const order = { id: 'order-1', code: 'EXP-1', marketId: 'market-1', commodityId: 'commodity-1',
      requiredMassKg: '48000', originalMass: '48', originalUnit: 'T', shippingFrom: '2026-11-10',
      shippingTo: '2026-11-20', version: 1, specificationVersion: 1, limits: [],
      allocatedMassKg: '24000', remainingMassKg: '24000', canEdit: true };
    const container = { id: 'container-1', orderId: 'order-1', internalCode: 'CTN-1',
      targetMassKg: '24000', originalMass: '24', originalUnit: 'T', status: 'PLANNED', version: 1 };
    const path = kind === 'order' ? '/export/orders/order-1' : '/export/containers/container-1';
    const route = kind === 'order' ? '/api/export-orders/order-1' : '/api/export-containers/container-1';
    fetchMock.mockImplementation(async (input, init) => {
      const url = String(input);
      if (url === route && init?.method === 'PATCH') {
        requests.push({ body: String(init.body), etag: new Headers(init.headers).get('If-Match') });
        return requests.length === 1 ? new Response('{"id":', { status: 200 }) : json(kind === 'order' ? order : container);
      }
      if (url === '/api/export-orders/order-1' || url === '/api/export-containers/container-1')
        return new Response(JSON.stringify(url.includes('export-containers') ? container : order),
          { headers: { 'Content-Type': 'application/json', ETag: '"1"' } });
      if (url.includes('/specifications?') || url.includes('/containers?') || url.includes('/history?'))
        return json({ items: [], totalCount: 0, hasMore: false });
      if (url.startsWith('/api/markets?')) return json({ items: [{ id: 'market-1', code: 'EU', name: 'Europe' }], hasMore: false });
      if (url.startsWith('/api/commodities?')) return json({ items: [{ id: 'commodity-1', code: 'RICE', name: 'Rice' }], hasMore: false });
      if (url === '/api/markets/market-1') return json({ id: 'market-1', code: 'EU', name: 'Europe' });
      if (url === '/api/commodities/commodity-1') return json({ id: 'commodity-1', code: 'RICE', name: 'Rice' });
      if (url.startsWith('/api/measurement-units?')) return json({ items: [{ id: 't', code: 'T', name: 'Tonne', quantityKind: 'MASS' }], hasMore: false });
      if (url === '/api/auth/csrf') return json({ token: 'csrf' });
      return json({}, 404);
    });
    const browser = userEvent.setup();
    const view = render(<ExportOrders canWrite path={path} onNavigate={navigate} />);
    await browser.click(await screen.findByRole('button', { name: kind === 'order' ? 'Edit order' : 'Edit target' }));
    await screen.findByRole('option', { name: 'Tonne (T)' });
    const field = screen.getByLabelText(kind === 'order' ? 'Required mass' : 'Target mass');
    await browser.clear(field);
    await browser.type(field, kind === 'order' ? '50' : '30');
    await browser.click(screen.getByRole('button', { name: kind === 'order' ? 'Save order' : 'Save target' }));
    expect(await screen.findByText(/result is uncertain/i)).toBeInTheDocument();
    expect(field).toBeDisabled();
    expect(sessionStorage.length).toBe(1);
    view.unmount();
    render(<ExportOrders canWrite path={path} onNavigate={navigate} />);
    await browser.click(await screen.findByRole('button', { name: 'Retry same change' }));
    await waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]).toEqual(requests[0]);
    expect(requests[1].etag).toBe('"1"');
    await waitFor(() => expect(sessionStorage.length).toBe(0));
    expect(screen.queryByRole('button', { name: 'Retry same change' })).not.toBeInTheDocument();
  });

  it('shows an older specification and pages containers and history', async () => {
    const limit = (value: string) => ({ attributeId: 'attribute-1', attributeCode: 'MOIST',
      attributeName: 'Moisture', unit: '%', minValue: null, maxValue: value });
    fetchMock.mockImplementation(async input => {
      const path = String(input);
      if (path === '/api/export-orders/order-1') return new Response(JSON.stringify({
        id: 'order-1', code: 'EXP-1', marketId: 'market-1', commodityId: 'commodity-1',
        requiredMassKg: '1000', originalMass: '1', originalUnit: 'T', shippingFrom: '2026-11-10',
        shippingTo: '2026-11-20', version: 2, specificationVersion: 2, limits: [limit('13')],
        containers: [], allocatedMassKg: '1000', remainingMassKg: '0', canEdit: true,
      }), { headers: { 'Content-Type': 'application/json', ETag: '"2"' } });
      if (path.includes('/specifications?')) return json({ items: [
        { versionNo: 2, recordedAt: '2026-11-02T00:00:00Z', limits: [limit('13')] },
        { versionNo: 1, recordedAt: '2026-11-01T00:00:00Z', limits: [limit('14')] },
      ], totalCount: 2, hasMore: false });
      if (path.includes('/containers?page=1')) return json({ items: [{ id: 'container-1', internalCode: 'CTN-1', targetMassKg: '500', status: 'PLANNED' }], totalCount: 2, hasMore: true });
      if (path.includes('/containers?page=2')) return json({ items: [{ id: 'container-2', internalCode: 'CTN-2', targetMassKg: '500', status: 'PLANNED' }], totalCount: 2, hasMore: false });
      if (path.includes('/history?page=1')) return json({ items: [{ action: 'ORDER_CREATED', recordedAt: '2026-11-01T00:00:00Z', actorName: 'Export Staff', snapshot: JSON.stringify({ RequiredMass: '1', Unit: 'T', ShippingFrom: '2026-11-10', ShippingTo: '2026-11-20' }) }], totalCount: 2, hasMore: true });
      if (path.includes('/history?page=2')) return json({ items: [{ action: 'SPECIFICATION_VERSION_ADDED', recordedAt: '2026-11-02T00:00:00Z', actorName: 'Export Staff', snapshot: JSON.stringify({ Limits: [limit('13')] }) }], totalCount: 2, hasMore: false });
      if (path === '/api/markets/market-1') return json({ id: 'market-1', code: 'EU', name: 'Europe' });
      if (path === '/api/commodities/commodity-1') return json({ id: 'commodity-1', code: 'RICE', name: 'Rice' });
      return json({}, 404);
    });
    const browser = userEvent.setup();
    render(<ExportOrders canWrite={false} path="/export/orders/order-1" onNavigate={vi.fn()} />);
    expect(await screen.findByText('Europe')).toBeInTheDocument();
    expect(screen.getByText('Rice')).toBeInTheDocument();
    await browser.selectOptions(await screen.findByLabelText('View version'), '1');
    expect(screen.getByText('14')).toBeInTheDocument();
    expect(await screen.findByText('CTN-1')).toBeInTheDocument();
    await browser.click(within(screen.getByRole('heading', { name: 'Container targets' }).closest('section')!).getByRole('button', { name: 'Next' }));
    expect(await screen.findByText('CTN-2')).toBeInTheDocument();
    await browser.click(within(screen.getByRole('heading', { name: 'History' }).closest('section')!).getByRole('button', { name: 'Next' }));
    expect(await screen.findByText(/New specification with 1 attributes/)).toBeInTheDocument();
  });

  it('locks an order edit while saving and focuses a field rejected by the API', async () => {
    let finishSave: ((response: Response) => void) | undefined;
    fetchMock.mockImplementation(async (input, init) => {
      const path = String(input);
      if (path === '/api/export-orders/order-1' && init?.method === 'PATCH')
        return new Promise<Response>(resolve => { finishSave = resolve; });
      if (path === '/api/export-orders/order-1') return new Response(JSON.stringify({
        id: 'order-1', code: 'EXP-1', marketId: 'market-1', commodityId: 'commodity-1',
        requiredMassKg: '1000', originalMass: '1', originalUnit: 'T', shippingFrom: '2026-11-10',
        shippingTo: '2026-11-20', version: 1, specificationVersion: 1, limits: [],
        allocatedMassKg: '0', remainingMassKg: '1000', canEdit: true,
      }), { headers: { 'Content-Type': 'application/json', ETag: '"1"' } });
      if (path.includes('/specifications?') || path.includes('/containers?') || path.includes('/history?'))
        return json({ items: [], totalCount: 0, hasMore: false });
      if (path === '/api/markets/market-1') return json({ id: 'market-1', code: 'EU', name: 'Europe' });
      if (path === '/api/commodities/commodity-1') return json({ id: 'commodity-1', code: 'RICE', name: 'Rice' });
      if (path.startsWith('/api/markets?')) return json({ items: [{ id: 'market-1', code: 'EU', name: 'Europe' }], hasMore: false });
      if (path.startsWith('/api/commodities?')) return json({ items: [{ id: 'commodity-1', code: 'RICE', name: 'Rice' }], hasMore: false });
      if (path.startsWith('/api/measurement-units?')) return json({ items: [{ id: 'unit-t', code: 'T', name: 'Tonne', quantityKind: 'MASS' }, { id: 'unit-kg', code: 'KG', name: 'Kilogram', quantityKind: 'MASS', isDeleted: true }], hasMore: false });
      if (path === '/api/auth/csrf') return json({ token: 'csrf' });
      return json({}, 404);
    });
    const browser = userEvent.setup();
    render(<ExportOrders canWrite path="/export/orders/order-1" onNavigate={vi.fn()} />);
    await browser.click(await screen.findByRole('button', { name: 'Edit order' }));
    const form = screen.getByRole('button', { name: 'Save order' }).closest('form')!;
    expect(within(form).queryByRole('option', { name: 'Kilogram (KG)' })).not.toBeInTheDocument();
    await browser.click(within(form).getByRole('button', { name: 'Save order' }));
    await waitFor(() => expect(finishSave).toBeDefined());
    expect(within(form).getByLabelText('Required mass')).toBeDisabled();
    expect(within(form).getByRole('button', { name: 'Cancel' })).toBeDisabled();
    finishSave!(json({ title: 'Validation failed', errors: { unit: ['Choose a configured mass unit.'] } }, 400));
    expect(await within(form).findByText('Choose a configured mass unit.')).toBeInTheDocument();
    await waitFor(() => expect(within(form).getByText('Choose a configured mass unit.').closest('label')?.querySelector('select')).toHaveFocus());
  });
});
