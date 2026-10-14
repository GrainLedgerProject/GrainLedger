import { render, screen, waitFor, within } from '@testing-library/react';
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
