import { act, render, screen, waitFor } from '@testing-library/react';
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
