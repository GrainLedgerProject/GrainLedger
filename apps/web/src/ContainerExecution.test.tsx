import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { ContainerExecution } from './ContainerExecution';

const fetchMock = vi.fn<typeof fetch>();
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const route = '/api/export-containers/c1/execution';
const container = { id: 'c1', code: 'C1', orderId: 'o1', targetKg: '10.000000', status: 'PLANNED', version: 1,
  loadedKg: '0.000000', remainingKg: '10.000000', containerNumber: null, sealNumber: null, holds: [], loads: [], dossierAvailable: false,
  approval: { proposalId: 'p1', decisionVersion: 1, mode: 'DIRECT_STOCK', allocations: [{ lotId: 'lot1', lotCode: 'HEAD-1', locationId: 'loc1', reservedKg: '10.000000', remainingKg: '10.000000', restricted: false, loadingPositionReady: true }] } };
const readiness = { phase: 'LOADING', checkedAt: '2026-11-13T12:00:00Z', assessmentStatus: 'PASS', findings: [] };
beforeEach(() => { vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset(); sessionStorage.clear(); });

it('uses this containers reserved stock, requires physical confirmation and sends exact kg strings', async () => {
  const requests: Record<string, unknown>[] = [];
  fetchMock.mockImplementation(async (input, init) => {
    if (String(input) === '/api/auth/csrf') return json({ token: 'csrf' });
    if (String(input) === route + '/loads') { requests.push(JSON.parse(String(init?.body))); return json({ ...container, loadedKg: '4.000001', remainingKg: '5.999999', version: 2, status: 'LOADING' }); }
    return json({ container, readiness });
  });
  const browser = userEvent.setup(); render(<ContainerExecution id="c1" userId="u1" canWrite canHold={false} onNavigate={vi.fn()} />);
  await screen.findByRole('heading', { name: 'Approved reserved stock' });
  expect(screen.getByRole('button', { name: 'Commit actual loading' })).toBeDisabled();
  await browser.selectOptions(screen.getByLabelText('Approved position'), 'lot1:loc1');
  await browser.type(screen.getByLabelText('Actual loaded quantity (kg)'), '4.000001');
  await browser.click(screen.getByRole('checkbox', { name: /I confirm/ }));
  await browser.click(screen.getByRole('button', { name: 'Commit actual loading' }));
  await waitFor(() => expect(requests).toHaveLength(1));
  expect(requests[0]).toMatchObject({ version: 1, proposalId: 'p1', decisionVersion: 1, lines: [{ lotId: 'lot1', locationId: 'loc1', quantityKg: '4.000001' }] });
  expect(screen.queryByRole('button', { name: 'Place container HOLD' })).not.toBeInTheDocument();
});

it.each(['network', 'invalid-json', 'empty', 'missing-id', 'timeout'])('retries the identical uncertain load after %s without generating a new operation', async failure => {
  const bodies: string[] = []; let first = true;
  fetchMock.mockImplementation(async (input, init) => {
    if (String(input) === '/api/auth/csrf') return json({ token: 'csrf' });
    if (String(input) === route + '/loads') { bodies.push(String(init?.body)); if (first) { first = false; if (failure === 'invalid-json') return new Response('{'); if (failure === 'empty') return new Response(''); if (failure === 'missing-id') return json({}); if (failure === 'timeout') return json({ title: 'Timed out' }, 408); throw new TypeError('connection lost'); } return json(container); }
    return json({ container, readiness });
  });
  const browser = userEvent.setup(); const props = { id: 'c1', userId: 'u1', canWrite: true, canHold: false, onNavigate: vi.fn() };
  const view = render(<ContainerExecution {...props} />);
  await screen.findByLabelText('Approved position'); await browser.selectOptions(screen.getByLabelText('Approved position'), 'lot1:loc1');
  await browser.type(screen.getByLabelText('Actual loaded quantity (kg)'), '4'); await browser.click(screen.getByRole('checkbox', { name: /I confirm/ }));
  await browser.click(screen.getByRole('button', { name: 'Commit actual loading' }));
  await screen.findByRole('button', { name: 'Retry container operation' });
  view.unmount(); render(<ContainerExecution {...props} />);
  await screen.findByRole('heading', { name: 'Approved reserved stock' });
  expect(screen.getByLabelText('Actual loaded quantity (kg)')).toBeDisabled();
  await browser.click(screen.getByRole('button', { name: 'Retry container operation' }));
  await waitFor(() => expect(bodies).toHaveLength(2)); expect(bodies[1]).toBe(bodies[0]);
});

it('explains missing blend outputs and never enables a draft as actual loading', async () => {
  fetchMock.mockResolvedValue(json({ container: { ...container, approval: { ...container.approval, mode: 'BLEND_INPUT_STOCK' } }, readiness: { ...readiness, assessmentStatus: 'INCOMPLETE', findings: ['BLEND_OUTPUT_HANDOFF_REQUIRED'] } }));
  render(<ContainerExecution id="c1" userId="u1" canWrite canHold={false} onNavigate={vi.fn()} />);
  expect(await screen.findByText(/needs actual blend execution/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Commit actual loading' })).toBeDisabled();
  expect(screen.getByText('No actual loading recorded.')).toBeInTheDocument();
});

it('shows container sources only on explicit authorized inspection and hides commercial mutations for managers', async () => {
  fetchMock.mockImplementation(async input => {
    if (String(input) === '/api/auth/csrf') return json({ token: 'csrf' });
    if (String(input) === route + '/trace') return json({ loadedKg: '4', complete: true, sourceContributions: [{ sourceLotId: 'source1', quantityKg: '4.000000', percentage: '100', status: 'RECORDED', provenance: { plotId: 'plot-1', procurementCode: 'PRC-1' } }] });
    return json({ container, readiness });
  });
  const browser = userEvent.setup(); render(<ContainerExecution id="c1" userId="manager" canWrite={false} canHold onNavigate={vi.fn()} />);
  await screen.findByRole('button', { name: 'Trace container sources' });
  expect(fetchMock.mock.calls.some(([input]) => String(input).endsWith('/trace'))).toBe(false);
  expect(screen.queryByRole('button', { name: 'Save container metadata' })).not.toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: 'Trace container sources' }));
  expect(await screen.findByText('plot-1')).toBeInTheDocument();
  expect(within(screen.getByRole('table', { name: 'Actual container sources' })).getByText('source1')).toBeInTheDocument();
});
