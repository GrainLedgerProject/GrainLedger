import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { ContainerWorkflow } from './ContainerWorkflow';

const fetchMock = vi.fn<typeof fetch>();
const route = '/api/export-containers/container1/workflow';
const data = { containerId: 'container1', ownerId: 'owner1', status: 'LOADING', version: 5,
  proposals: [{ id: 'proposal1', planId: 'plan1', version: 1, context: { facilityId: 'facility1', mode: 'DIRECT_STOCK', inputs: [], outputs: [], remainingProductionKg: '0' } }],
  reviews: [], handoffs: [], documents: [], supplements: [], holds: [], holdResponses: [] };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
beforeEach(() => { vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset(); sessionStorage.clear(); });

it('retains a scope review across remount and retries the exact uncertain request', async () => {
  const bodies: string[] = []; let failed = false;
  fetchMock.mockImplementation(async (url, init) => {
    if (String(url) === '/api/auth/csrf') return json({ token: 'csrf' });
    if (String(url) === route + '/reviews') {
      bodies.push(String(init?.body));
      if (!failed) { failed = true; throw new TypeError('Synthetic connection lost'); }
      return json({ ...data, version: 6 });
    }
    return json(data);
  });
  const browser = userEvent.setup(); const first = render(<ContainerWorkflow id="container1" userId="cm1" canReview />);
  await browser.selectOptions(await screen.findByLabelText('Proposal to review'), 'proposal1');
  await browser.selectOptions(screen.getByLabelText('Review scope'), 'ALL_REMAINING');
  await browser.type(screen.getByLabelText('Scope review reason'), 'Synthetic current evidence review');
  await browser.click(screen.getByRole('button', { name: 'Approve and activate scope' }));
  await screen.findByRole('button', { name: 'Retry workflow operation' }); first.unmount();
  render(<ContainerWorkflow id="container1" userId="cm1" canReview />);
  await browser.click(await screen.findByRole('button', { name: 'Retry workflow operation' }));
  await waitFor(() => expect(bodies).toHaveLength(2)); expect(bodies[1]).toBe(bodies[0]);
  expect(JSON.parse(bodies[0])).toMatchObject({ proposalId: 'proposal1', scope: 'ALL_REMAINING', decision: 'APPROVED', version: 5 });
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry workflow operation' })).not.toBeInTheDocument());
});

it('gives Export document and supplement controls without customer review or mill execution authority', async () => {
  fetchMock.mockImplementation(async () => json({ ...data, status: 'CLOSED', documents: [
    { id: 'generated', documentType: 'PACKING_LIST', version: 1, filename: 'packing-list.html', origin: 'GENERATED' },
    { id: 'uploaded', documentType: 'PACKING_LIST', version: 2, filename: 'scanned-list.pdf', origin: 'UPLOADED' },
    { id: 'historical', documentType: 'PACKING_LIST', version: 3, filename: 'old-list.html', origin: 'UNKNOWN' },
  ] }));
  render(<ContainerWorkflow id="container1" userId="export1" canWrite />);
  await screen.findByRole('button', { name: 'Save document version' });
  expect(screen.getByRole('button', { name: 'Append evidence supplement' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Approve and activate scope' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Commit reviewed handoff' })).not.toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'packing-list.html' }).parentElement).toHaveTextContent('Generated from closure dossier');
  expect(screen.getByRole('link', { name: 'scanned-list.pdf' }).parentElement).toHaveTextContent('Uploaded file');
  expect(screen.getByRole('link', { name: 'old-list.html' }).parentElement).toHaveTextContent('Origin unrecorded');
});

it('lets the assigned Operator commit a reviewed handoff and never shows scope approval controls', async () => {
  const handoff = { id: 'handoff1', proposalId: 'proposal1', facilityId: 'facility1', kind: 'TRANSFER', state: 'APPROVED', version: 2,
    content: { lotId: 'lot1', quantityKg: '6.000001', fromLocationId: 'store1', toLocationId: 'store2' } };
  const bodies: string[] = [];
  vi.spyOn(window, 'confirm').mockReturnValue(true);
  fetchMock.mockImplementation(async (url, init) => {
    if (String(url) === '/api/auth/csrf') return json({ token: 'csrf' });
    if (String(url) === '/api/ledger/lookups') return json({ facilities: [], owners: [], locations: [], equipment: [], commodities: [] });
    if (String(url) === route + '/handoffs/commit') { bodies.push(String(init?.body)); return json({ ...data, version: 6, handoffs: [{ ...handoff, state: 'COMMITTED' }] }); }
    return json({ ...data, handoffs: [handoff] });
  });
  const browser = userEvent.setup(); render(<ContainerWorkflow id="container1" userId="operator1" operator />);
  await browser.click(await screen.findByRole('button', { name: 'Commit reviewed handoff' }));
  await waitFor(() => expect(bodies).toHaveLength(1));
  expect(JSON.parse(bodies[0])).toMatchObject({ version: 5, handoffId: 'handoff1', handoffVersion: 2 });
  expect(screen.queryByRole('button', { name: 'Approve and activate scope' })).not.toBeInTheDocument();
});

it('keeps HOLD handling limited to retained responses without release or unload controls', async () => {
  fetchMock.mockImplementation(async url => String(url) === '/api/ledger/lookups' ? json({ facilities: [], owners: [], locations: [], equipment: [], commodities: [] }) : json({ ...data, holds: [{ id: 'hold1', reason: 'Synthetic HOLD' }] }));
  render(<ContainerWorkflow id="container1" userId="manager1" manager />);
  await screen.findByLabelText('Handling action');
  expect(screen.getByRole('option', { name: 'Acknowledge' })).toBeInTheDocument();
  expect(screen.getByRole('option', { name: 'Record response' })).toBeInTheDocument();
  expect(screen.queryByRole('option', { name: /release/i })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /unload|reopen|release/i })).not.toBeInTheDocument();
});
