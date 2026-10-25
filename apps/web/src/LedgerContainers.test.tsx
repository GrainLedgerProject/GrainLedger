import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { LedgerContainers } from './LedgerContainers';
import type { User } from './api';
import type { Lot } from './ledger-ui';

const lot: Lot = { id: 'lot', code: 'BLEND-01', stage: 'BLENDED_RICE', productIdentity: 'BLENDED_RICE', commodityId: 'commodity', ownerId: 'owner', restrictions: [], positions: [{ locationId: 'location', facilityId: 'facility', quantityKg: '8.000001' }] };
let optionLot = lot;
vi.mock('./LedgerLotPicker', () => ({ LedgerLotPicker: ({ label, value, onChange, onSelected }: { label: string; value: string; onChange: (id: string) => void; onSelected: (lot: Lot) => void }) => <label>{label}<select value={value} onChange={e => { onChange(e.target.value); onSelected(optionLot); }}><option value="">Select…</option><option value="lot">BLEND-01</option></select></label> }));
const actor: User = { id: 'export', email: 'e@example.com', displayName: 'Export', phoneNumber: null, role: 'ExportStaff', organizationId: 'owner', organizationName: 'Owner', isActive: true, permissions: ['containers.write', 'lots.read.tenant'] };
const initial = { preparationVersion: 2, id: 'container', code: 'BOX-01', status: 'OPEN', facilityId: 'facility', orderId: null, targetKg: '8.000001', containerNumber: null, sealNumber: null, version: 'v1', committedLoadedKg: '0.000000', remainingKg: '8.000001', drafts: [] as { id: string; loadedAt: string; totalKg: string; lines: unknown[] }[] };
const mock = vi.fn<typeof fetch>();
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
let saved = { ...initial };
beforeEach(() => {
  vi.stubGlobal('fetch', mock); mock.mockReset(); optionLot = lot; saved = { ...initial, drafts: [] };
  mock.mockImplementation(async (url, init) => {
    const path = String(url), body = init?.body ? JSON.parse(String(init.body)) : {};
    if (path === '/api/auth/csrf') return response({ token: 'csrf' });
    if (path === '/api/ledger/lookups') return response({ owners: [], locations: [{ id: 'location', name: 'Store' }], facilities: [{ id: 'facility', name: 'Mill' }], commodities: [], equipment: [] });
    if (path.includes('/history')) return response({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
    if (path.includes('close-check') || path.includes('loading-check')) return response({ ready: false, version: saved.version, committedLoadedKg: '0.000000', draftKg: '8.000001', targetKg: '8.000001', findings: [{ code: 'OUTPUT_RESERVATION_UNAVAILABLE', message: 'Output stock reservations are not connected. Drafts do not hold stock.' }] });
    if (path.endsWith('/loading-draft')) { saved = { ...saved, version: 'v2', drafts: [{ id: body.draftId, loadedAt: body.loadedAt, lines: body.lines, totalKg: '8.000001' }] }; return response(saved); }
    if (path === '/api/ledger/containers' && init?.method === 'POST') return response(saved);
    if (path.startsWith('/api/ledger/containers?')) return response({ items: [initial], page: 1, pageSize: 25, totalCount: 1, hasMore: false });
    return response(saved);
  });
});

it('creates preparation and saves exact loading lines, with closure blocked and no commit requests', async () => {
  const browser = userEvent.setup(); render(<LedgerContainers user={actor} />);
  await screen.findByRole('option', { name: 'Mill' });
  await browser.type(screen.getByLabelText('Internal container code'), 'BOX-01');
  await browser.selectOptions(screen.getByLabelText('Loading facility'), 'facility');
  await browser.type(screen.getByLabelText('Target net kg'), '8.000001');
  await browser.click(screen.getByRole('button', { name: 'Create preparation' }));
  await screen.findByRole('heading', { name: 'Container — BOX-01' });
  await browser.selectOptions(screen.getByLabelText('Lot for line 1'), 'lot');
  await browser.selectOptions(screen.getByLabelText('Source position for line 1'), 'location');
  await browser.type(screen.getByLabelText('Net kg for line 1'), '8.000001');
  await browser.click(screen.getByRole('button', { name: 'Save loading draft' }));
  await screen.findByText('8.000001 kg draft');
  const call = mock.mock.calls.find(([url]) => String(url).endsWith('/loading-draft'))!;
  expect(JSON.parse(String(call[1]?.body)).lines).toEqual([{ lotId: 'lot', locationId: 'location', quantityKg: '8.000001' }]);
  expect(new Headers(call[1]?.headers).get('X-CSRF-TOKEN')).toBe('csrf');
  await browser.click(screen.getByRole('button', { name: /Check loading / }));
  expect(await screen.findByRole('status', { name: 'Container readiness' })).toHaveTextContent('Output stock reservations are not connected');
  expect(screen.getByRole('heading', { name: 'Reserved output stock' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Commit loading' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Close container' })).toBeDisabled();
  expect(mock.mock.calls.some(([url]) => /\/(commit-loading|close)$/.test(String(url)))).toBe(false);
});

it('gives Customer Manager read/check access without editing or creation forms', async () => {
  const browser = userEvent.setup(); render(<LedgerContainers user={{ ...actor, role: 'CustomerManager', permissions: ['blend.approve'] }} />);
  await browser.click(await screen.findByRole('button', { name: 'BOX-01' }));
  expect(await screen.findByRole('heading', { name: 'Container — BOX-01' })).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Create preparation' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Save metadata' })).not.toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: 'Check closure' }));
  expect(await screen.findByRole('status', { name: 'Container readiness' })).toHaveTextContent('Blocked');
});

it('retries an uncertain create with the same identity and request ID, and reports stale edits', async () => {
  const browser = userEvent.setup(); render(<LedgerContainers user={actor} />);
  await screen.findByRole('option', { name: 'Mill' });
  await browser.type(screen.getByLabelText('Internal container code'), 'BOX-01');
  await browser.selectOptions(screen.getByLabelText('Loading facility'), 'facility');
  await browser.type(screen.getByLabelText('Target net kg'), '8');
  const implementation = mock.getMockImplementation()!; let attempts = 0;
  mock.mockImplementation(async (url, init) => {
    if (url === '/api/ledger/containers' && init?.method === 'POST' && attempts++ === 0) throw new Error('Network lost');
    if (String(url).endsWith('/metadata')) return response({ title: 'Container changed; reload before saving' }, 409);
    return implementation(url, init);
  });
  await browser.click(screen.getByRole('button', { name: 'Create preparation' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Network lost');
  await browser.click(screen.getByRole('button', { name: 'Create preparation' }));
  await screen.findByRole('heading', { name: 'Container — BOX-01' });
  const calls = mock.mock.calls.filter(([url, init]) => url === '/api/ledger/containers' && init?.method === 'POST');
  expect(calls).toHaveLength(2); expect(calls[0][1]?.body).toBe(calls[1][1]?.body);
  await browser.click(screen.getByRole('button', { name: 'Save metadata' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Container changed');
  expect(screen.getByRole('button', { name: 'Reload container' })).toBeEnabled();
});

it('does not fetch inactive sections or show stale responses after deactivation', async () => {
  const view = render(<LedgerContainers user={actor} active={false} />);
  expect(mock).not.toHaveBeenCalled();
  view.rerender(<LedgerContainers user={actor} active />);
  await waitFor(() => expect(within(screen.getByRole('table', { name: 'Containers' })).getByText('BOX-01')).toBeInTheDocument());
  expect(mock.mock.calls.some(([url]) => String(url).includes('search=+'))).toBe(false);
});


it('ignores detail that finishes after leaving the container section', async () => {
  const implementation = mock.getMockImplementation()!;
  let complete: (response: Response) => void = () => {};
  mock.mockImplementation((url, init) => String(url) === '/api/ledger/containers/container'
    ? new Promise<Response>(resolve => { complete = resolve; }) : implementation(url, init));
  const browser = userEvent.setup(); const view = render(<LedgerContainers user={actor} />);
  await browser.click(await screen.findByRole('button', { name: 'BOX-01' }));
  view.rerender(<LedgerContainers user={actor} active={false} />);
  await act(async () => complete(response(initial)));
  expect(screen.queryByRole('heading', { name: 'Container — BOX-01' })).not.toBeInTheDocument();
});

it('retains an edited draft pool revision until the user explicitly reselects the position', async () => {
  optionLot = { ...lot, isCurrentPool: true, poolRevision: 8, positions: [{ ...lot.positions[0], poolRevision: 8 }] };
  saved = { ...initial, drafts: [{ id: 'draft', loadedAt: '2026-10-25T00:00:00Z', totalKg: '1.000001', lines: [{ lotId: 'lot', locationId: 'location', quantityKg: '1.000001', poolRevision: '7' }] }] };
  const implementation = mock.getMockImplementation()!; let attempts = 0;
  mock.mockImplementation(async (url, init) => String(url).endsWith('/loading-draft') && attempts++ === 0
    ? response({ title: 'Pool changed; reload, reselect and save the draft again' }, 409) : implementation(url, init));
  const browser = userEvent.setup(); render(<LedgerContainers user={actor} />);
  await browser.click(await screen.findByRole('button', { name: 'BOX-01' }));
  await browser.click(screen.getByRole('button', { name: 'Edit draft draft' }));
  expect(within(screen.getByRole('group', { name: 'Loading line 1' })).getByText(/Observed pool revision: 7/)).toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: 'Save loading draft' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Pool changed');
  const first = mock.mock.calls.find(([url]) => String(url).endsWith('/loading-draft'))!;
  expect(JSON.parse(String(first[1]?.body)).lines[0].poolRevision).toBe('7');
  await browser.selectOptions(screen.getByLabelText('Lot for line 1'), 'lot');
  await browser.selectOptions(screen.getByLabelText('Source position for line 1'), 'location');
  expect(within(screen.getByRole('group', { name: 'Loading line 1' })).getByText(/Observed pool revision: 8/)).toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: 'Save loading draft' }));
  await waitFor(() => expect(mock.mock.calls.filter(([url]) => String(url).endsWith('/loading-draft'))).toHaveLength(2));
  const second = mock.mock.calls.filter(([url]) => String(url).endsWith('/loading-draft'))[1];
  expect(JSON.parse(String(second[1]?.body)).lines[0]).toMatchObject({ poolRevision: '8', quantityKg: '1.000001' });
});

it('loads evidence only on request for one distinct saved lot and delegates trace without a POST', async () => {
  saved = { ...initial, drafts: [{ id: 'draft', loadedAt: '2026-10-25T00:00:00Z', totalKg: '2', lines: [
    { lotId: 'lot', lotCode: 'BLEND-01', locationId: 'location', quantityKg: '1' },
    { lotId: 'lot', lotCode: 'BLEND-01', locationId: 'location', quantityKg: '1' },
  ] }] };
  const implementation = mock.getMockImplementation()!;
  mock.mockImplementation(async (url, init) => String(url).startsWith('/api/active-substances') ? response({ items: [], page: 1, hasMore: false })
    : String(url).includes('/residue-evaluations') ? response({ items: [] }) : implementation(url, init));
  const onTrace = vi.fn(); const browser = userEvent.setup();
  const user = { ...actor, role: 'CustomerManager', permissions: ['blend.approve', 'residue.evaluate', 'trace.read.tenant'] };
  const view = render(<LedgerContainers user={user} onTrace={onTrace} />);
  await browser.click(await screen.findByRole('button', { name: 'BOX-01' }));
  expect(screen.getAllByRole('button', { name: 'Inspect lot BLEND-01' })).toHaveLength(1);
  expect(mock.mock.calls.some(([url]) => String(url).includes('/residue-'))).toBe(false);
  await browser.click(screen.getByRole('button', { name: 'Inspect lot BLEND-01' }));
  expect(mock.mock.calls.some(([url]) => String(url).includes('/residue-'))).toBe(false);
  await browser.click(screen.getByRole('button', { name: 'View loading lot residue bounds' }));
  await screen.findByText('No saved evaluations for this lot.');
  expect(mock.mock.calls.filter(([url]) => String(url).includes('/lots/lot/residue-evaluations'))).toHaveLength(1);
  await browser.click(screen.getByRole('button', { name: 'Trace selected loading lot' }));
  expect(onTrace).toHaveBeenCalledWith('lot');
  expect(mock.mock.calls.some(([url, init]) => String(url).includes('/trace') && init?.method === 'POST')).toBe(false);
  view.rerender(<LedgerContainers user={user} active={false} onTrace={onTrace} />);
  expect(screen.queryByRole('heading', { name: 'Lot residue bounds' })).not.toBeInTheDocument();
});

it('shows deployment upgrade explicitly and keeps draft save disabled with old detail', async () => {
  saved = { ...initial, preparationVersion: 1 };
  const browser = userEvent.setup(); render(<LedgerContainers user={actor} />);
  await browser.click(await screen.findByRole('button', { name: 'BOX-01' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('preparation upgrade is required');
  expect(screen.getByRole('button', { name: 'Save loading draft' })).toBeDisabled();
});

it('closes evidence for an unsaved lot when the loading selection is cleared or a new draft starts', async () => {
  const implementation = mock.getMockImplementation()!;
  mock.mockImplementation(async (url, init) => String(url).startsWith('/api/active-substances') ? response({ items: [], page: 1, hasMore: false })
    : String(url).includes('/residue-evaluations') ? response({ items: [] }) : implementation(url, init));
  const browser = userEvent.setup();
  render(<LedgerContainers user={{ ...actor, permissions: [...actor.permissions, 'residue.evaluate'] }} />);
  await browser.click(await screen.findByRole('button', { name: 'BOX-01' }));
  await browser.selectOptions(screen.getByLabelText('Lot for line 1'), 'lot');
  await browser.click(screen.getByRole('button', { name: 'Inspect lot BLEND-01' }));
  await browser.click(screen.getByRole('button', { name: 'View loading lot residue bounds' }));
  await screen.findByText('No saved evaluations for this lot.');
  await browser.selectOptions(screen.getByLabelText('Lot for line 1'), '');
  expect(screen.queryByRole('region', { name: 'Selected loading lot evidence' })).not.toBeInTheDocument();
  expect(screen.queryByText('No saved evaluations for this lot.')).not.toBeInTheDocument();
  await browser.selectOptions(screen.getByLabelText('Lot for line 1'), 'lot');
  await browser.click(screen.getByRole('button', { name: 'Inspect lot BLEND-01' }));
  await browser.click(screen.getByRole('button', { name: 'View loading lot residue bounds' }));
  await screen.findByText('No saved evaluations for this lot.');
  await browser.click(screen.getByRole('button', { name: 'New draft' }));
  expect(screen.queryByText('No saved evaluations for this lot.')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Close loading lot bounds' })).not.toBeInTheDocument();
});
