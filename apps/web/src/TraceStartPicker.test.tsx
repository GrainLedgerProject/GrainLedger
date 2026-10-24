import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { TraceStartPicker } from './TraceStartPicker';
afterEach(() => vi.unstubAllGlobals());
it('requests server-side pool, location and source pages rather than filtering an inventory page', async () => {
  const changed = vi.fn();
  const fetchMock = vi.fn<typeof fetch>(async url => {
    const q = new URL(String(url), 'http://localhost');
    const current = Number(q.searchParams.get('page'));
    return new Response(JSON.stringify({ page: current, pageSize: 25, totalCount: q.searchParams.get('locationId') ? 1 : 26,
      facilities: [{ id: 'facility', name: 'Mill One' }], locations: [{ id: 'silo', name: 'Silo One', facilityId: 'facility' }],
      items: [{ id: current === 2 ? 'last-pool' : 'pool', code: current === 2 ? 'POOL-LAST' : 'POOL-FIRST', kind: 'CURRENT_POOL', stage: 'RAW_PADDY', quantityKg: '800.000000', poolRevision: 2, positions: [{ locationName: 'Silo One' }] }],
    }));
  });
  vi.stubGlobal('fetch', fetchMock);
  render(<TraceStartPicker value="" onChange={changed} active />);
  await screen.findByRole('option', { name: /POOL-FIRST.*Current pool.*800.000000/ });
  expect(String(fetchMock.mock.calls[0][0])).toContain('mode=pools');
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Next Starting lot page' }));
  await screen.findByRole('option', { name: /POOL-LAST/ });
  await user.selectOptions(screen.getByLabelText('Starting lot'), 'last-pool'); expect(changed).toHaveBeenLastCalledWith('last-pool');
  await user.selectOptions(screen.getByLabelText('Trace silo / location'), 'silo');
  await waitFor(() => expect(String(fetchMock.mock.calls.at(-1)![0])).toContain('locationId=silo'));
  expect(String(fetchMock.mock.calls.at(-1)![0])).toContain('page=1');
  await user.selectOptions(screen.getByLabelText('Starting point type'), 'sources');
  await waitFor(() => expect(String(fetchMock.mock.calls.at(-1)![0])).toContain('mode=sources'));
  expect(fetchMock.mock.calls.every(([url]) => String(url).startsWith('/api/ledger/trace/starts?'))).toBe(true);
});
it('keeps an entered historical Ledger ID usable and displays selector failures explicitly', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ title: 'Trace upgrade is not installed' }), { status: 503 })));
  render(<TraceStartPicker value="old-lot" onChange={() => {}} active />);
  expect(await screen.findByRole('alert')).toHaveTextContent('Trace upgrade is not installed');
  expect(screen.getByRole('option', { name: 'Entered Ledger lot · old-lot' })).toBeEnabled();
});
