import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { AdminCropSeasons } from './AdminCropSeasons';
import { MrlLookup } from './MrlLookup';
import { Procurements } from './Procurement';

const fetchMock = vi.fn<typeof fetch>();
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const companyId = '11111111-1111-4111-8111-111111111111';
const marketId = '22222222-2222-4222-8222-222222222222';
const commodityId = '33333333-3333-4333-8333-333333333333';
const versionId = '44444444-4444-4444-8444-444444444444';
beforeEach(() => { vi.stubGlobal('fetch', fetchMock); vi.stubGlobal('confirm', vi.fn(() => true)); fetchMock.mockReset(); });

it('manages a selected company crop season with its version and keeps other companies out of the list request', async () => {
  let row = { id: versionId, companyId, name: 'Winter', startsOn: '2027-01-01', endsOn: '2027-05-01', version: 1, canEdit: true, canDelete: true };
  fetchMock.mockImplementation(async (url, init) => {
    const text = String(url);
    if (text === '/api/admin/organizations') return json([{ id: companyId, name: 'Customer A', kind: 'Customer' }, { id: marketId, name: 'Mill', kind: 'Mill' }]);
    if (text.startsWith('/api/crop-seasons?')) {
      expect(new URL(text, 'http://localhost').searchParams.get('companyId')).toBe(companyId);
      return json({ items: [row], page: 1, pageSize: 20, hasMore: false });
    }
    if (text === `/api/crop-seasons/${versionId}` && init?.method === 'PUT') {
      expect(JSON.parse(String(init.body)).expectedVersion).toBe(1);
      row = { ...row, ...JSON.parse(String(init.body)), version: 2 };
      return json(row);
    }
    if (text === '/api/auth/csrf') return json({ token: 'csrf' });
    return json({}, 404);
  });
  const user = userEvent.setup(); render(<AdminCropSeasons />);
  expect(screen.queryByText('Winter')).not.toBeInTheDocument();
  await user.selectOptions(await screen.findByLabelText('Customer company'), companyId);
  await screen.findByText('Winter');
  await user.click(screen.getByRole('button', { name: 'Edit' }));
  const dialog = screen.getByRole('dialog', { name: 'Edit crop season' });
  await user.clear(within(dialog).getByLabelText('Name'));
  await user.type(within(dialog).getByLabelText('Name'), 'Spring');
  await user.click(within(dialog).getByRole('button', { name: 'Save crop season' }));
  expect(await screen.findByText('Spring')).toBeInTheDocument();
});

it('passes the chosen local decision time to MRL resolve and reads the returned version limits', async () => {
  fetchMock.mockImplementation(async url => {
    const path = String(url);
    if (path.startsWith('/api/markets?')) return json({ items: [{ id: marketId, code: 'EU', name: 'Europe' }], hasMore: false });
    if (path.startsWith('/api/commodities?')) return json({ items: [{ id: commodityId, code: 'PADDY', name: 'Paddy' }], hasMore: false });
    if (path.startsWith('/api/active-substances?')) return json({ items: [{ id: companyId, code: 'SUB', name: 'Substance' }], hasMore: false });
    if (path.startsWith('/api/mrl-versions/resolve?')) {
      const params = new URL(path, 'http://localhost').searchParams;
      expect(params.get('marketId')).toBe(marketId);
      expect(params.get('commodityId')).toBe(commodityId);
      expect(params.get('decisionAt')).toBe(new Date('2027-03-01T10:00').toISOString());
      return json({ id: versionId, versionNo: 2, status: 'PUBLISHED', effectiveFrom: '2027-01-01T00:00:00Z', effectiveTo: null, sourceRef: 'EU regulation' });
    }
    if (path.startsWith(`/api/mrl-versions/${versionId}/limits?`)) return json({ items: [{ id: 'limit', substanceId: companyId, limitValue: 0.01, unit: 'mg/kg' }], hasMore: false });
    return json({}, 404);
  });
  const user = userEvent.setup(); render(<MrlLookup />);
  await screen.findByRole('option', { name: 'EU · Europe' });
  await user.selectOptions(screen.getByLabelText('Export market'), marketId);
  await user.selectOptions(screen.getByLabelText('Commodity'), commodityId);
  fireEvent.change(screen.getByLabelText('Decision time'), { target: { value: '2027-03-01T10:00' } });
  await user.click(screen.getByRole('button', { name: 'Find version' }));
  expect(await screen.findByText('EU regulation')).toBeInTheDocument();
  expect(screen.getByText('0.01')).toBeInTheDocument();
});

it('sends intake filters and sort to the server, then restores them after remount', async () => {
  fetchMock.mockImplementation(async url => {
    const path = String(url);
    if (path.startsWith('/api/farmers?')) return json({ items: [], hasMore: false });
    if (path.startsWith('/api/procurements?')) return json({ items: [], page: 1, pageSize: 20, hasMore: false });
    return json({}, 404);
  });
  const onStateChange = vi.fn(); const user = userEvent.setup();
  const view = render(<Procurements canCreate={false} onCreate={() => {}} onOpen={() => {}} onStateChange={onStateChange} />);
  await user.type(screen.getByLabelText('Receipt code'), 'TM-123');
  fireEvent.change(screen.getByLabelText('From date'), { target: { value: '2026-10-01' } });
  fireEvent.change(screen.getByLabelText('To date'), { target: { value: '2026-10-16' } });
  await user.selectOptions(screen.getByLabelText('Sort by'), 'code');
  await user.click(screen.getByRole('button', { name: 'Apply filters' }));
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => {
    if (!String(url).startsWith('/api/procurements?')) return false;
    const params = new URL(String(url), 'http://localhost').searchParams;
    return params.get('search') === 'TM-123' && params.get('from') === '2026-10-01' && params.get('to') === '2026-10-16' && params.get('sortBy') === 'code';
  })).toBe(true));
  const saved = onStateChange.mock.lastCall?.[0];
  view.unmount();
  render(<Procurements canCreate={false} onCreate={() => {}} onOpen={() => {}} initialState={saved} />);
  expect(screen.getByLabelText('Receipt code')).toHaveValue('TM-123');
  expect(screen.getByLabelText('Sort by')).toHaveValue('code');
});
