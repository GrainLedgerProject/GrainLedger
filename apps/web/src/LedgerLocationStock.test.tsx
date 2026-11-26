import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { LedgerStock } from './LedgerStock';
import type { User } from './api';

const user: User = { id: 'actor', email: 'staff@example.com', displayName: 'Staff', phoneNumber: null, role: 'MillOperator', organizationId: 'mill', organizationName: 'Mill', isActive: true, permissions: [] };
const lookup = { facilities: [{ id: 'mill', name: 'Mill' }], locations: [{ id: 'store', facilityId: 'mill', name: 'Store' }, { id: 'empty', facilityId: 'mill', name: 'Empty' }], owners: [], commodities: [], equipment: [] };
const emptyPage = { items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false };
const position = { lotId: 'lot-1', code: 'LOT-1', stage: 'RAW_PADDY', productIdentity: 'PADDY', riceVariety: 'IR', varietyStatus: 'SINGLE', ownerName: 'Owner', quantityKg: '9007199254740993.123456', availableKg: '0.000000', restrictions: ['HOLD', 'RESERVED'] };
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const fetchMock = vi.fn<typeof fetch>();

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset();
  fetchMock.mockImplementation(async input => {
    const url = String(input);
    if (url === '/api/ledger/lookups') return response(lookup);
    if (url.startsWith('/api/ledger/lots?')) return response(emptyPage);
    if (url === '/api/ledger/lots/lot-1') return response({ id: 'lot-1', code: 'LOT-1', ownerId: '', commodityId: '', stage: 'RAW_PADDY', productIdentity: 'PADDY', positions: [], restrictions: [] });
    if (url.startsWith('/api/ledger/locations/store/stock')) {
      const page = new URL(url, 'http://localhost').searchParams.get('page');
      return response({ locationId: 'store', totalKg: '9007199254740994.123456', items: page === '1' ? [position] : [{ ...position, lotId: 'lot-2', code: 'LOT-2', quantityKg: '1.000000' }], page: Number(page), pageSize: 25, totalCount: 26, hasMore: page === '1' });
    }
    if (url.startsWith('/api/ledger/locations/empty/stock')) return response({ ...emptyPage, locationId: 'empty', totalKg: '0.000000' });
    return response({ title: 'Unexpected request' }, 404);
  });
});

it('shows scoped physical totals and restricted positions across pages, with lot detail', async () => {
  const browser = userEvent.setup(); render(<LedgerStock user={user} />);
  const picker = await screen.findByLabelText('Stock location');
  await within(picker).findByRole('option', { name: 'Mill — Store' });
  await browser.selectOptions(picker, 'store');
  expect(await screen.findByText('Authorized physical stock total: 9007199254740994.123456 kg')).toBeInTheDocument();
  const table = screen.getByRole('table', { name: 'Location stock positions' });
  expect(within(table).getByText('9007199254740993.123456')).toBeInTheDocument();
  expect(within(table).getByText('0.000000')).toBeInTheDocument();
  expect(within(table).getByText('HOLD, RESERVED')).toBeInTheDocument();
  await browser.click(within(table).getByRole('button', { name: 'Open lot detail for LOT-1' }));
  expect(await screen.findByRole('heading', { name: 'Lot detail: LOT-1' })).toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: 'Next location stock page' }));
  expect(await screen.findByText('LOT-2')).toBeInTheDocument();
  expect(screen.getByText('Authorized physical stock total: 9007199254740994.123456 kg')).toBeInTheDocument();
  await browser.selectOptions(picker, 'empty');
  expect(await screen.findByText('No authorized physical stock at this location.')).toBeInTheDocument();
  expect(screen.getByText('Authorized physical stock total: 0.000000 kg')).toBeInTheDocument();
});

it('shows loading and allows retry after location request fails', async () => {
  let resolveRequest!: (value: Response) => void;
  const original = fetchMock.getMockImplementation()!;
  let first = true;
  fetchMock.mockImplementation((input, init) => {
    if (String(input).startsWith('/api/ledger/locations/store/stock') && first) {
      first = false;
      return new Promise<Response>(resolve => { resolveRequest = resolve; });
    }
    return original(input, init);
  });
  const browser = userEvent.setup(); render(<LedgerStock user={user} />);
  const picker = await screen.findByLabelText('Stock location');
  await within(picker).findByRole('option', { name: 'Mill — Store' });
  await browser.selectOptions(picker, 'store');
  expect(screen.getByText('Loading location stock…')).toBeInTheDocument();
  resolveRequest(response({ title: 'Stock unavailable' }, 503));
  expect(await screen.findByRole('alert')).toHaveTextContent('Stock unavailable');
  await browser.click(screen.getByRole('button', { name: 'Retry' }));
  expect(await screen.findByText('Authorized physical stock total: 9007199254740994.123456 kg')).toBeInTheDocument();
});

it('ignores an earlier location response after switching locations', async () => {
  let resolveRequest!: (value: Response) => void;
  const original = fetchMock.getMockImplementation()!;
  fetchMock.mockImplementation((input, init) => String(input).startsWith('/api/ledger/locations/store/stock')
    ? new Promise<Response>(resolve => { resolveRequest = resolve; })
    : original(input, init));
  const browser = userEvent.setup(); render(<LedgerStock user={user} />);
  const picker = await screen.findByLabelText('Stock location');
  await within(picker).findByRole('option', { name: 'Mill — Store' });
  await browser.selectOptions(picker, 'store');
  await browser.selectOptions(picker, 'empty');
  expect(await screen.findByText('No authorized physical stock at this location.')).toBeInTheDocument();
  await act(async () => { resolveRequest(response({ locationId: 'store', totalKg: '100.000000', items: [position], page: 1, pageSize: 25, totalCount: 1, hasMore: false })); });
  expect(screen.getByText('Authorized physical stock total: 0.000000 kg')).toBeInTheDocument();
  expect(screen.queryByRole('table', { name: 'Location stock positions' })).not.toBeInTheDocument();
});
