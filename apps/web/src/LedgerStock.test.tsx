import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { useState } from 'react';
import { LedgerStock } from './LedgerStock';
import { LedgerLotPicker } from './LedgerLotPicker';
import { type User } from './api';
import { type Lot } from './ledger-ui';
function PickerConsumer({ onSubmit }: { onSubmit: () => void }) {
  const [value, setValue] = useState(''), [selected, setSelected] = useState<Lot>();
  return <form onSubmit={event => { event.preventDefault(); onSubmit(); }}><LedgerLotPicker label="Transfer lot" value={value} onChange={setValue} onSelected={setSelected} look={{ owners: [], facilities: [], locations: [], commodities: [], equipment: [] }} ownerId="owner-a" /><p>Selected lot: {selected?.code || 'none'}</p><button type="submit">Commit transfer</button></form>;
}

const user: User = { id: 'operator', email: 'operator@example.com', displayName: 'Operator', phoneNumber: null, role: 'MillOperator', organizationId: 'owner-a', organizationName: 'Owner A', isActive: true, permissions: ['lots.transfer'] };
const fetchMock = vi.fn<typeof fetch>();
const response = (body: unknown) => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } });
const lot = (id: string, code: string) => ({ id, code, ownerId: 'owner-a', commodityId: 'rice', stage: 'RAW_PADDY', productIdentity: null, positions: [], restrictions: [] });
beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  fetchMock.mockImplementation(async url => {
    const path = String(url).split('?')[0], query = new URL(String(url), 'http://localhost').searchParams;
    if (path === '/api/ledger/lookups') return response({ owners: [{ id: 'owner-a', name: 'Owner A' }], facilities: [], locations: [], commodities: [], equipment: [] });
    if (path === '/api/ledger/lots') {
      const page = Number(query.get('page'));
      return response({ items: [lot(page === 1 ? 'lot-1' : 'lot-26', page === 1 ? 'LOT-001' : 'LOT-026')], page, pageSize: 25, totalCount: 26, hasMore: page === 1 });
    }
    return response({ title: `Unexpected route ${path}` });
  });
});
it('loads stock pages from server and shows records beyond first 25', async () => {
  const browser = userEvent.setup();
  render(<LedgerStock user={user} />);
  expect(await screen.findByText('LOT-001')).toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: 'Next stock page' }));
  expect(await screen.findByText('LOT-026')).toBeInTheDocument();
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/api/ledger/lots?') && new URL(String(url), 'http://localhost').searchParams.get('page') === '2')).toBe(true));
  expect(screen.getByRole('button', { name: 'Page 2 of stock' })).toHaveAttribute('aria-current', 'page');
});
it('refreshes selected lot detail when stock view becomes active again', async () => {
  const browser = userEvent.setup();
  let detailReads = 0;
  fetchMock.mockImplementation(async url => {
    const path = String(url).split('?')[0], query = new URL(String(url), 'http://localhost').searchParams;
    if (path === '/api/ledger/lookups') return response({ owners: [{ id: 'owner-a', name: 'Owner A' }], facilities: [{ id: 'facility-a', name: 'Mill A' }], locations: [{ id: 'location-a', name: 'Store' }], commodities: [], equipment: [] });
    if (path === '/api/ledger/lots') return response({ items: [lot('lot-1', 'LOT-001')], page: Number(query.get('page') || 1), pageSize: 25, totalCount: 1, hasMore: false });
    if (path === '/api/ledger/lots/lot-1') {
      detailReads++;
      const current = detailReads === 1 ? '10' : '7';
      return response({ ...lot('lot-1', 'LOT-001'), positions: [{ locationId: 'location-a', facilityId: 'facility-a', quantityKg: current, availableKg: current }], restrictions: detailReads === 1 ? [] : ['HOLD restriction'], contributions: [], history: { items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false } });
    }
    return response({ title: `Unexpected route ${path}` });
  });
  const view = render(<LedgerStock user={user} />);
  await screen.findByText('LOT-001');
  const open = screen.getByRole('button', { name: 'Open lot detail for LOT-001' });
  await waitFor(() => expect(open).toBeEnabled());
  await browser.click(open);
  const positionRow = () => within(screen.getByRole('table', { name: 'stock positions' })).getAllByRole('row')[1];
  await waitFor(() => expect(positionRow()).toHaveTextContent('10'));
  view.rerender(<LedgerStock user={user} active={false} />);
  view.rerender(<LedgerStock user={user} active />);
  await waitFor(() => expect(positionRow()).toHaveTextContent('7'));
  expect(await screen.findByText('HOLD restriction')).toBeInTheDocument();
});
it('waits for complete deep-linked lot detail and displays scoped source composition', async () => {
  let resolveDetail!: (value: Response) => void;
  const pendingDetail = new Promise<Response>(resolve => { resolveDetail = resolve; });
  const pool = { ...lot('pool', 'POOL-001'), mixedBulk: true, isCurrentPool: true, poolRevision: 4, positions: [{ locationId: 'location-a', facilityId: 'facility-a', quantityKg: '100', availableKg: '100', poolRevision: 4 }], sourceComposition: [{ sourceLotId: 'source-a', sourceLotCode: 'SRC-A', quantityKg: '50' }, { sourceLotId: 'source-b', sourceLotCode: 'SRC-B', quantityKg: '30' }, { sourceLotId: 'source-c', sourceLotCode: 'SRC-C', quantityKg: '20' }], history: { items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false } };
  fetchMock.mockImplementation(async url => {
    const path = String(url).split('?')[0];
    if (path === '/api/ledger/lookups') return response({ owners: [], facilities: [{ id: 'facility-a', name: 'Mill A' }], locations: [{ id: 'location-a', name: 'Silo' }], commodities: [], equipment: [] });
    if (path === '/api/ledger/lots') return response({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
    if (path === '/api/ledger/lots/pool') return pendingDetail;
    return response({ title: `Unexpected route ${path}` });
  });
  render(<LedgerStock user={user} initialLotId="pool" />);
  expect(await screen.findByRole('status')).toHaveTextContent('Loading lot detail');
  expect(screen.queryByRole('heading', { name: 'Lot detail: POOL-001' })).not.toBeInTheDocument();
  await act(async () => { resolveDetail(response(pool)); });
  expect(await screen.findByRole('heading', { name: 'Lot detail: POOL-001' })).toBeInTheDocument();
  const composition = screen.getByRole('table', { name: 'source composition' });
  expect(within(composition).getByText('SRC-A')).toBeInTheDocument();
  expect(within(composition).getByText('50')).toBeInTheDocument();
  expect(screen.getByText(/not separate withdrawable stock/)).toBeInTheDocument();
});
it.each([false, true])('shows equipment labels in lot and chronological history without fabricating receipt equipment (retired=%s)', async retired => {
  const browser = userEvent.setup(), equipmentName = retired ? 'Dryer (retired)' : 'Dryer';
  const history = { items: [
    { id: 'processing-event', operationId: 'operation', occurredAt: '2026-01-02T00:00:00Z', kind: 'DRYING', ownerName: 'Owner A', equipmentName, quantityKg: '10', details: { declaredLossKg: '0' } },
    { id: 'receipt-event', operationId: null, occurredAt: '2026-01-01T00:00:00Z', kind: 'RECEIPT', ownerName: 'Owner A', quantityKg: '10', details: {} },
  ], page: 1, pageSize: 25, totalCount: 2, hasMore: false };
  fetchMock.mockImplementation(async url => {
    const path = String(url).split('?')[0];
    if (path === '/api/ledger/lookups') return response({ owners: [{ id: 'owner-a', name: 'Owner A' }], facilities: [], locations: [], commodities: [], equipment: [] });
    if (path === '/api/ledger/lots') return response({ items: [lot('lot-1', 'LOT-001')], page: 1, pageSize: 25, totalCount: 1, hasMore: false });
    if (path === '/api/ledger/lots/lot-1') return response({ ...lot('lot-1', 'LOT-001'), history });
    if (path === '/api/ledger/history') return response(history);
    return response({ title: `Unexpected route ${path}` });
  });
  render(<LedgerStock user={user} initialLotId="lot-1" />);
  await screen.findByRole('heading', { name: 'Lot detail: LOT-001' });
  await browser.click(screen.getByText('Lot history', { selector: 'summary' }));
  const assertHistory = (table: HTMLElement) => {
    expect(within(table).getByText(equipmentName)).toBeInTheDocument();
    const receipt = within(table).getByText('RECEIPT').closest('tr')!;
    expect(within(receipt).getAllByRole('cell')[3]).toHaveTextContent('—');
    expect(receipt).not.toHaveTextContent('Dryer');
  };
  assertHistory(screen.getByRole('table', { name: 'lot history' }));
  await browser.click(screen.getByRole('button', { name: 'History' }));
  assertHistory(await screen.findByRole('table', { name: 'history' }));
});
it('keeps positive custody-derived aliquots selectable at ordinary positions', async () => {
  const browser = userEvent.setup(), onSubmit = vi.fn(), aliquot = { ...lot('aliquot', 'ALIQUOT-001'), mixedBulk: true, isCurrentPool: false, custodyDerived: true, positions: [{ locationId: 'store', facilityId: 'facility-a', quantityKg: '40', availableKg: '40' }] };
  fetchMock.mockImplementation(async url => {
    const path = String(url).split('?')[0];
    if (path === '/api/ledger/lookups') return response({ owners: [{ id: 'owner-a', name: 'Owner A' }], facilities: [], locations: [{ id: 'store', name: 'Store' }], commodities: [], equipment: [] });
    if (path === '/api/ledger/lots') return response({ items: [aliquot], page: 1, pageSize: 25, totalCount: 1, hasMore: false });
    return response({ title: `Unexpected route ${path}` });
  });
  render(<PickerConsumer onSubmit={onSubmit} />);
  const option = await screen.findByRole('option', { name: /ALIQUOT-001/ });
  expect(option).toBeEnabled();
  expect(option).toHaveTextContent(/custody-derived stock at current position/);
  await browser.selectOptions(screen.getByLabelText('Transfer lot'), 'aliquot');
  expect(await screen.findByText('Selected lot: ALIQUOT-001')).toBeInTheDocument();
});
it('pages lot choices beyond the first page', async () => {
  const browser = userEvent.setup(), onSubmit = vi.fn();
  render(<PickerConsumer onSubmit={onSubmit} />);
  await screen.findByRole('option', { name: /LOT-001/ });
  await browser.click(screen.getByRole('button', { name: 'Next Transfer lot page' }));
  await screen.findByRole('option', { name: /LOT-026/ });
  await browser.selectOptions(screen.getByLabelText('Transfer lot'), 'lot-26');
  expect(await screen.findByText('Selected lot: LOT-026')).toBeInTheDocument();
  await browser.type(screen.getByLabelText('Search Transfer lot'), 'lot');
  await browser.keyboard('{Enter}');
  expect(onSubmit).not.toHaveBeenCalled();
});
it('loads and refreshes live preview separately from audit snapshots and history', async () => {
  const browser = userEvent.setup();
  const residueUser = { ...user, permissions: [...user.permissions, 'residue.evaluate'] };
  const savedId = 'snapshot-1';
  const history = [{ id: savedId, lotId: 'lot-1', snapshotAt: '2026-01-02T03:04:00Z', persistedAt: '2026-01-02T03:04:01Z', engineVersion: 'F12-exact-1' }];
  const evaluation = (kind: 'preview' | 'saved') => ({
    ...(kind === 'saved' ? { id: savedId, persistedAt: '2026-01-02T03:04:01Z' } : {}),
    lotId: 'lot-1', companyId: 'owner-a',
    snapshotAt: kind === 'saved' ? '2026-01-02T03:04:00Z' : '2026-01-03T03:04:00Z',
    engineVersion: 'F12-exact-1', results: [],
  });
  let previews = 0, saves = 0, historyReads = 0;
  fetchMock.mockImplementation(async (input, init) => {
    const path = String(input).split('?')[0];
    if (path === '/api/auth/csrf') return response({ token: 'csrf' });
    if (path === '/api/ledger/lookups') return response({ owners: [{ id: 'owner-a', name: 'Owner A' }], facilities: [], locations: [], commodities: [], equipment: [] });
    if (path === '/api/ledger/lots') return response({ items: [lot('lot-1', 'LOT-001')], page: 1, pageSize: 25, totalCount: 1, hasMore: false });
    if (path === '/api/ledger/lots/lot-1') return response({ ...lot('lot-1', 'LOT-001'), positions: [], restrictions: [], history: { items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false } });
    if (path === '/api/active-substances') return response({ items: [{ id: 'substance-1', code: 'SUB-1', name: 'Substance 1' }], page: 1, pageSize: 100, hasMore: false });
    if (path === '/api/ledger/lots/lot-1/residue-evaluations' && init?.method === 'POST') {
      saves++;
      return response(evaluation('saved'));
    }
    if (path === '/api/ledger/lots/lot-1/residue-evaluations') {
      historyReads++;
      return response({ items: history });
    }
    if (path === `/api/ledger/lots/lot-1/residue-evaluations/${savedId}`) return response(evaluation('saved'));
    if (path === '/api/ledger/lots/lot-1/residue-preview') {
      previews++;
      return response(evaluation('preview'));
    }
    return response({ title: `Unexpected route ${path}` });
  });

  render(<LedgerStock user={residueUser} />);
  const open = await screen.findByRole('button', { name: 'Open lot detail for LOT-001' });
  await browser.click(open);
  expect(await screen.findByRole('heading', { name: /Live preview/ })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /engine Residue bounds exact 1/ })).toBeInTheDocument();
  expect(previews).toBe(1);
  expect(saves).toBe(0);

  await browser.click(screen.getByRole('button', { name: /engine Residue bounds exact 1/ }));
  expect(await screen.findByRole('heading', { name: /Historical snapshot/ })).toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: 'Refresh live preview' }));
  expect(await screen.findByRole('heading', { name: /Live preview/ })).toBeInTheDocument();
  expect(previews).toBe(2);
  expect(saves).toBe(0);

  await browser.click(screen.getByRole('button', { name: 'Lock snapshot for audit' }));
  expect(await screen.findByRole('heading', { name: /Saved snapshot/ })).toBeInTheDocument();
  expect(saves).toBe(1);
  expect(historyReads).toBeGreaterThanOrEqual(2);
});
