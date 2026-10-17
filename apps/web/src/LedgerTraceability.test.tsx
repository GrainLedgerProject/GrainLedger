import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { LedgerTraceability, type LotTrace } from './LedgerTraceability';
import { type User } from './api';

const actor: User = { id: 'actor', email: 'test@example.com', displayName: 'Test', phoneNumber: null, role: 'Administrator', organizationId: 'mill', organizationName: 'Mill', isActive: true, permissions: ['trace.read.all'] };
const lotId = 'b9c6099b-19b1-4187-a2a7-542a29186dd8';
const trace: LotTrace = { traceId: 'trace-1', targetLotId: lotId, direction: 'backward', view: 'current', asOf: '2026-10-17T00:00:00Z', complete: true, lotCount: 1, edgeCount: 0, sourceContributionsVisible: true,
  lots: [{ id: lotId, code: 'LOT-TRACE', stage: 'DRIED_PADDY', producedKg: '0.000001', positions: [{ locationId: 'store', quantityKg: '0.000001', availableKg: '0.000000' }], restrictions: ['HOLD'], contributions: [{ sourceLotId: 'source', quantityKg: '0.000000', denominatorKg: '0.000001', percentage: '0.000000' }] }],
  edges: [], operations: [], corrections: [], sources: [{ lotId: 'source', status: 'LEGACY_SOURCE_UNAVAILABLE', provenance: null }] };
const fetchMock = vi.fn<typeof fetch>();
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset();
  fetchMock.mockImplementation(async url => {
    if (String(url) === '/api/auth/csrf') return response({ token: 'csrf' });
    if (String(url).startsWith('/api/ledger/lots?')) return response({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
    return response(trace);
  });
});

it('submits scoped trace with CSRF and keeps exact zero allocation, stock restrictions and legacy explanation', async () => {
  const browser = userEvent.setup(); render(<LedgerTraceability user={actor} />);
  await browser.type(screen.getByLabelText('Ledger lot ID'), lotId);
  await browser.selectOptions(screen.getByLabelText('Trace direction'), 'backward');
  await browser.click(screen.getByRole('button', { name: 'Run trace' }));
  expect(await screen.findByRole('table', { name: 'Traced source contributions' })).toHaveTextContent('0.000000 / 0.000001');
  expect(screen.getByText(/Legacy source details unavailable/)).toBeInTheDocument();
  expect(screen.getByRole('table', { name: 'Traced lots' })).toHaveTextContent('HOLD');
  const call = fetchMock.mock.calls.find(([url]) => url === '/api/ledger/trace')!;
  expect(JSON.parse(String(call[1]?.body))).toEqual({ lotId, direction: 'backward', view: 'current' });
  expect(new Headers(call[1]?.headers).get('X-CSRF-TOKEN')).toBe('csrf');
  await browser.selectOptions(screen.getByLabelText('Trace view'), 'history');
  expect(screen.queryByRole('table', { name: 'Traced lots' })).not.toBeInTheDocument();
});

it('only offers affected-lot direction to Export Staff and never fetches automatically on mount', async () => {
  render(<LedgerTraceability user={{ ...actor, role: 'ExportStaff', permissions: ['trace.read.tenant'], organizationId: 'customer' }} />);
  expect(screen.queryByRole('option', { name: /Backward/ })).not.toBeInTheDocument();
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).startsWith('/api/ledger/lots?'))).toBe(true));
  expect(fetchMock.mock.calls.some(([url]) => url === '/api/ledger/trace')).toBe(false);
});

it('clears old result on failure instead of showing stale or partial data', async () => {
  const browser = userEvent.setup(); render(<LedgerTraceability user={actor} />);
  await browser.type(screen.getByLabelText('Ledger lot ID'), lotId);
  await browser.click(screen.getByRole('button', { name: 'Run trace' }));
  await screen.findByRole('table', { name: 'Traced lots' });
  fetchMock.mockImplementation(async url => String(url) === '/api/auth/csrf' ? response({ token: 'csrf' }) : response({ title: 'Trace exceeds interactive limit; no complete result returned' }, 409));
  await browser.click(screen.getByRole('button', { name: 'Run trace' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Trace exceeds interactive limit');
  expect(screen.queryByRole('table', { name: 'Traced lots' })).not.toBeInTheDocument();
});

it('ignores a request finishing after the user leaves the section', async () => {
  let complete: (value: Response) => void = () => {};
  fetchMock.mockImplementation(async url => String(url) === '/api/auth/csrf' ? response({ token: 'csrf' }) : new Promise(resolve => { complete = resolve; }));
  const browser = userEvent.setup(), screenView = render(<LedgerTraceability user={actor} />);
  await browser.type(screen.getByLabelText('Ledger lot ID'), lotId);
  await browser.click(screen.getByRole('button', { name: 'Run trace' }));
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url === '/api/ledger/trace')).toBe(true));
  screenView.rerender(<LedgerTraceability user={actor} active={false} />);
  await act(async () => complete(response(trace)));
  expect(screen.queryByRole('table', { name: 'Traced lots' })).not.toBeInTheDocument();
});

const childId = '00000000-0000-0000-0000-000000000001';
const branchingTrace: LotTrace = { ...trace, direction: 'forward', lotCount: 2, edgeCount: 1,
  lots: [{ ...trace.lots[0], id: childId, code: 'CHILD', producedKg: '0.000001' }, trace.lots[0]],
  edges: [{ operationId: 'op', inputLotId: lotId, outputLotId: childId, inputConsumedKg: '0.000002', outputProducedKg: '0.000001' }],
  operations: [{ id: 'op', kind: 'DRYING', status: 'COMMITTED', occurredAt: trace.asOf, declaredLossKg: '0.000001', varianceKg: '0.000000' }] };

it('switches Table and Flowchart without fetching again and selects a node to inspect the same source detail', async () => {
  fetchMock.mockImplementation(async url => String(url) === '/api/auth/csrf' ? response({ token: 'csrf' }) : response(branchingTrace));
  const browser = userEvent.setup(); render(<LedgerTraceability user={actor} />);
  await browser.type(screen.getByLabelText('Ledger lot ID'), lotId);
  await browser.click(screen.getByRole('button', { name: 'Run trace' }));
  const table = await screen.findByRole('table', { name: 'Traced lots' });
  expect(within(table).getAllByRole('row')[1]).toHaveTextContent('LOT-TRACE');
  expect(within(table).getAllByRole('row')[1]).toHaveTextContent('Starting lot');
  await browser.click(screen.getByRole('button', { name: 'Flowchart' }));
  const graph = screen.getByRole('region', { name: 'Trace flowchart' });
  expect(within(graph).getByRole('button', { name: 'Select lot LOT-TRACE — starting lot' })).toHaveAttribute('aria-pressed', 'true');
  expect(within(graph).getByRole('group', { name: 'DRYING operation op' })).toHaveTextContent('COMMITTED');
  await browser.click(within(graph).getByRole('button', { name: 'Select lot CHILD' }));
  expect(screen.getByRole('heading', { name: 'Source composition — CHILD' })).toBeInTheDocument();
  expect(screen.getByRole('region', { name: 'Selected graph lot' })).toHaveTextContent('0.000001');
  await browser.click(screen.getByRole('button', { name: 'Focus starting lot' }));
  expect(within(graph).getByRole('button', { name: /starting lot/ })).toHaveFocus();
  await browser.click(screen.getByRole('button', { name: 'Table' }));
  expect(screen.getByRole('table', { name: 'Traced lots' })).toBeInTheDocument();
  expect(fetchMock.mock.calls.filter(([url]) => url === '/api/ledger/trace')).toHaveLength(1);
});

it('displays a lone starting lot and zoom controls without inventing downstream steps', async () => {
  const browser = userEvent.setup(); render(<LedgerTraceability user={actor} />);
  await browser.type(screen.getByLabelText('Ledger lot ID'), lotId);
  await browser.click(screen.getByRole('button', { name: 'Run trace' }));
  await screen.findByRole('table', { name: 'Traced lots' });
  await browser.click(screen.getByRole('button', { name: 'Flowchart' }));
  expect(screen.getByText(/starting lot is shown on its own/)).toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: 'Zoom in' }));
  expect(screen.getByLabelText('Graph zoom')).toHaveTextContent('120%');
  await browser.click(screen.getByRole('button', { name: 'Reset zoom' }));
  expect(screen.getByLabelText('Graph zoom')).toHaveTextContent('100%');
  const chart = screen.getByRole('region', { name: 'Trace flowchart' });
  expect(within(chart).getAllByRole('button')).toHaveLength(1);
});

it('keeps source data hidden in the Export Staff flowchart as in the API response', async () => {
  const exported: LotTrace = { ...branchingTrace, sourceContributionsVisible: false, sources: [], lots: branchingTrace.lots.map(lot => ({ ...lot, contributions: null })) };
  fetchMock.mockImplementation(async url => String(url) === '/api/auth/csrf' ? response({ token: 'csrf' }) : String(url).startsWith('/api/ledger/lots?') ? response({ items: [branchingTrace.lots[1]], page: 1, pageSize: 25, totalCount: 1, hasMore: false }) : response(exported));
  const browser = userEvent.setup(); render(<LedgerTraceability user={{ ...actor, role: 'ExportStaff', permissions: ['trace.read.tenant'] }} />);
  await screen.findByRole('option', { name: /LOT-TRACE/ });
  await browser.selectOptions(screen.getByLabelText('Starting lot'), lotId);
  await browser.click(screen.getByRole('button', { name: 'Run trace' }));
  await screen.findByRole('table', { name: 'Traced lots' });
  await browser.click(screen.getByRole('button', { name: 'Flowchart' }));
  await browser.click(within(screen.getByRole('region', { name: 'Trace flowchart' })).getByRole('button', { name: 'Select lot CHILD' }));
  expect(screen.queryByRole('heading', { name: /Source composition/ })).not.toBeInTheDocument();
  expect(screen.queryByRole('table', { name: 'Traced source contributions' })).not.toBeInTheDocument();
});

it('keeps the complete table available when a result is too large to draw', async () => {
  const large = { ...trace, lotCount: 251, lots: [trace.lots[0], ...Array.from({ length: 250 }, (_, i) => ({ ...trace.lots[0], id: `extra-${i}`, code: `Extra ${i}` }))] };
  fetchMock.mockImplementation(async url => String(url) === '/api/auth/csrf' ? response({ token: 'csrf' }) : response(large));
  const browser = userEvent.setup(); render(<LedgerTraceability user={actor} />);
  await browser.type(screen.getByLabelText('Ledger lot ID'), lotId);
  await browser.click(screen.getByRole('button', { name: 'Run trace' }));
  await screen.findByRole('table', { name: 'Traced lots' });
  await browser.click(screen.getByRole('button', { name: 'Flowchart' }));
  expect(screen.getByText(/Flowchart supports up to/)).toBeInTheDocument();
  expect(screen.queryByRole('region', { name: 'Trace flowchart' })).not.toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: 'Show table' }));
  expect(screen.getByRole('table', { name: 'Traced lots' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Last traced lots page' })).toBeEnabled();
  expect(fetchMock.mock.calls.filter(([url]) => url === '/api/ledger/trace')).toHaveLength(1);
});
