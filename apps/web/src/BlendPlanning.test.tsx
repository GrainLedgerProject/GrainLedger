import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BlendPlanWorkspace, ContainerPlans } from './BlendPlanning';

const fetchMock = vi.fn<typeof fetch>();
const json = (body: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } });
const plan = { id: 'plan-1', orderId: 'order-1', containerId: 'container-1', versionNo: 1, version: 1,
  status: 'DRAFT', targetMassKg: '1000', targetProductIdentity: 'HEAD_RICE', demandCurrent: true, canCancel: true,
  createdAt: '2026-10-26T00:00:00Z', demandSnapshot: JSON.stringify({ marketId: 'market-1', commodityId: 'rice-1', shippingFrom: '2026-11-01', shippingTo: '2026-11-10', limits: [] }) };
const emptyPage = { items: [], page: 1, totalCount: 0, hasMore: false };
const candidates = { ...emptyPage, scopeRevision: 'scope-1', summary: { inventoryEligibleCount: 1, excludedCount: 0,
  inventoryEligibleMassKg: '1000', inventoryShortfallKg: '0', directProductAvailableMassKg: '1000' }, items: [{ lotId: 'lot-1', lotCode: 'HDR-261026-000001', locationId: 'store-1', locationName: 'Dry Store',
    availableKg: '1000', stockKind: 'ORDINARY', productIdentity: 'HEAD_RICE', productCompatibility: 'DIRECT_MATCH', inventoryEligible: true,
    isCurrentPool: false, poolRevision: null, sourceCount: 1, candidateRevision: 'candidate-1', restrictions: [], reasons: [] }] };
const preview = { outcome: 'PASS', iterations: 1, reasons: [], selection: { mode: 'DIRECT_STOCK', selectedMassKg: '1000', allocations: [{ lotId: 'lot-1', locationId: 'store-1', lotCode: 'HDR-261026-000001', quantityKg: '1000' }] },
  assessmentStatus: 'PASS', assessmentFindings: [{ code: 'OUTPUT_MASS', status: 'PASS', actual: '1000', limit: '1000', unit: 'kg', reference: null }], predictedOutputKg: '1000', previewReference: 'preview-1', canPropose: true };

beforeEach(() => { vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset(); sessionStorage.clear(); });

describe('F14 planning workflow', () => {
  it('shows one setup warning and disables draft creation when storage is unavailable', async () => {
    fetchMock.mockImplementation(async input => {
      const path = String(input);
      if (path.endsWith('/planning-context')) return json({ orderVersion: 1, containerVersion: 1, storageReady: false,
        canCreateDraft: false, missingDependencies: [{ code: 'PLANNING_SCHEMA_NOT_READY', description: 'Apply the reviewed Export migration and runtime grants.' }] });
      if (path.includes('/blend-plans?')) return json({}, 503);
      return json({}, 404);
    });
    render(<ContainerPlans containerId="container-1" userId="staff" canPropose onNavigate={vi.fn()} />);
    expect(await screen.findByText('Blend planning is unavailable because its database setup is incomplete.')).toBeInTheDocument();
    expect(screen.getByText('Technical details')).toBeInTheDocument();
    expect(screen.queryByText('Blend plan storage has not been deployed.')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '+ Create draft' })).not.toBeInTheDocument();
  });

  it('creates a draft using the current order and container versions', async () => {
    const navigate = vi.fn();
    fetchMock.mockImplementation(async (input, init) => {
      const path = String(input);
      if (path === '/api/export-containers/container-1/planning-context') return json({ orderVersion: 3, containerVersion: 2, storageReady: true, canCreateDraft: true,
        allowedTargetProducts: ['HEAD_RICE', 'BROKEN_RICE', 'BLENDED_RICE'], missingDependencies: [] });
      if (path.startsWith('/api/export-containers/container-1/blend-plans?')) return json(emptyPage);
      if (path === '/api/auth/csrf') return json({ token: 'csrf' });
      if (path.endsWith('/blend-plans/drafts') && init?.method === 'POST') return json(plan, 201);
      return json({}, 404);
    });
    const user = userEvent.setup();
    render(<ContainerPlans containerId="container-1" userId="staff" canPropose onNavigate={navigate} />);
    const product = await screen.findByLabelText(/Product to ship/);
    expect(within(product).queryByRole('option', { name: 'Raw paddy' })).not.toBeInTheDocument();
    expect(within(product).queryByRole('option', { name: 'Bran' })).not.toBeInTheDocument();
    await user.selectOptions(product, 'HEAD_RICE');
    await user.click(screen.getByRole('button', { name: '+ Create draft' }));
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/export/blend-plans/plan-1'));
    const request = fetchMock.mock.calls.find(([url, init]) => String(url).endsWith('/blend-plans/drafts') && init?.method === 'POST');
    expect(JSON.parse(String(request?.[1]?.body))).toMatchObject({ orderVersion: 3, containerVersion: 2, targetProductIdentity: 'HEAD_RICE' });
  });

  it('explains an unsupported order commodity without offering a draft', async () => {
    fetchMock.mockImplementation(async input => String(input).endsWith('/planning-context')
      ? json({ orderVersion: 1, containerVersion: 1, storageReady: true, canCreateDraft: false,
        allowedTargetProducts: [], missingDependencies: [] }) : json(emptyPage));
    render(<ContainerPlans containerId="container-1" userId="staff" canPropose onNavigate={vi.fn()} />);
    expect(await screen.findByText(/no supported F14 product category/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '+ Create draft' })).not.toBeInTheDocument();
  });

  it('shows incomplete evidence and does not allow a proposal', async () => {
    fetchMock.mockImplementation(async (input, init) => {
      const path = String(input);
      if (path === '/api/blend-plans/plan-1') return json(plan, 200, { ETag: '"1"' });
      if (path === '/api/blend-plans/plan-1/proposal') return json({}, 404);
      if (path.includes('/history?')) return json(emptyPage);
      if (path === '/api/ledger/lookups') return json({ facilities: [{ id: 'mill-1', name: 'Demo Mill' }], locations: [{ id: 'store-1', name: 'Dry Store', kind: 'STORE' }] });
      if (path === '/api/markets/market-1') return json({ code: 'EU', name: 'Europe' });
      if (path === '/api/commodities/rice-1') return json({ code: 'RICE', name: 'Rice' });
      if (path.includes('/candidates?')) return json(candidates);
      if (path === '/api/auth/csrf') return json({ token: 'csrf' });
      if (path === '/api/blend-plans/plan-1/preview' && init?.method === 'POST') return json({ ...preview, outcome: 'SEARCH_EXHAUSTED', assessmentStatus: 'INCOMPLETE',
        assessmentFindings: [{ code: 'QUALITY_EVIDENCE_MISSING', status: 'INCOMPLETE', actual: null, limit: null, unit: null, reference: null }], previewReference: null, canPropose: false });
      return json({}, 404);
    });
    const user = userEvent.setup();
    render(<BlendPlanWorkspace planId="plan-1" userId="staff" canPropose canReadLedger onNavigate={vi.fn()} />);
    await user.selectOptions(await screen.findByLabelText('Facility'), 'mill-1');
    await user.click(await screen.findByRole('button', { name: 'Calculate plan' }));
    expect(await screen.findByText('Quality evidence missing')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save proposal' })).not.toBeInTheDocument();
  });

  it('explains why a raw paddy draft with enough stock cannot complete quality assessment', async () => {
    fetchMock.mockImplementation(async (input, init) => {
      const path = String(input);
      if (path === '/api/blend-plans/plan-1') return json({ ...plan, targetProductIdentity: 'RAW_PADDY', targetMassKg: '200' }, 200, { ETag: '"1"' });
      if (path === '/api/blend-plans/plan-1/proposal') return json({}, 404);
      if (path.includes('/history?')) return json(emptyPage);
      if (path === '/api/ledger/lookups') return json({ facilities: [{ id: 'mill-1', name: 'Demo Mill' }], locations: [] });
      if (path.startsWith('/api/markets/') || path.startsWith('/api/commodities/')) return json({ code: 'PADDY', name: 'Paddy' });
      if (path.includes('/candidates?')) return json({ ...candidates, summary: { ...candidates.summary, directProductAvailableMassKg: '1200' } });
      if (path === '/api/auth/csrf') return json({ token: 'csrf' });
      if (path === '/api/blend-plans/plan-1/preview' && init?.method === 'POST') return json({
        ...preview, outcome: 'SEARCH_EXHAUSTED', reasons: ['SEARCH_SPACE_EXHAUSTED'], assessmentStatus: 'INCOMPLETE',
        assessmentFindings: [{ code: 'QUALITY_EVIDENCE_MISSING', status: 'INCOMPLETE', actual: null, limit: null, unit: null, reference: null }],
        previewReference: null, canPropose: false
      });
      return json({}, 404);
    });
    const user = userEvent.setup();
    render(<BlendPlanWorkspace planId="plan-1" userId="staff" canPropose canReadLedger onNavigate={vi.fn()} />);
    expect(await screen.findByText(/Ledger accepts confirmed planning-quality observations only for finished rice lots/)).toBeInTheDocument();
    await user.selectOptions(await screen.findByLabelText('Facility'), 'mill-1');
    await user.click(await screen.findByRole('button', { name: 'Calculate plan' }));
    expect(await screen.findByText('Candidate found')).toBeInTheDocument();
    expect(screen.getByText(/Enough stock was selected, but the assessment is incomplete/)).toBeInTheDocument();
    expect(screen.queryByText(/search limit was reached/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save proposal' })).not.toBeInTheDocument();
  });

  it('separates physical stock from product-matching stock', async () => {
    fetchMock.mockImplementation(async input => {
      const path = String(input);
      if (path === '/api/blend-plans/plan-1') return json({ ...plan, targetMassKg: '20000' }, 200, { ETag: '"1"' });
      if (path === '/api/blend-plans/plan-1/proposal') return json({}, 404);
      if (path.includes('/history?')) return json(emptyPage);
      if (path === '/api/ledger/lookups') return json({ facilities: [{ id: 'mill-1', name: 'Demo Mill' }], locations: [] });
      if (path.startsWith('/api/markets/') || path.startsWith('/api/commodities/')) return json({ code: 'RICE', name: 'Rice' });
      if (path.includes('/candidates?')) return json({ ...candidates, summary: {
        inventoryEligibleCount: 1, excludedCount: 0, inventoryEligibleMassKg: '1470',
        inventoryShortfallKg: '18530', directProductAvailableMassKg: '0'
      }, totalCount: path.includes('productMatchOnly=true') ? 0 : 1,
      items: path.includes('productMatchOnly=true') ? [] : [{ ...candidates.items[0], productIdentity: 'RAW_PADDY', productCompatibility: 'PRODUCT_MISMATCH', availableKg: '1470' }] });
      return json({}, 404);
    });
    const user = userEvent.setup();
    render(<BlendPlanWorkspace planId="plan-1" userId="staff" canPropose canReadLedger onNavigate={vi.fn()} />);
    await user.selectOptions(await screen.findByLabelText('Facility'), 'mill-1');
    const directMatch = await screen.findByText('Direct product match');
    expect(within(directMatch.parentElement!).getByText('0 kg')).toBeInTheDocument();
    expect(screen.getByText(/No stock directly matches Head rice/)).toBeInTheDocument();
    expect(screen.queryByText('Product mismatch')).not.toBeInTheDocument();
    expect(screen.getByText(/No stock matching this product is shown/)).toBeInTheDocument();
    await user.click(screen.getByLabelText('Show other products'));
    expect(await screen.findByText('Product mismatch')).toBeInTheDocument();
    expect(screen.getByText('In stock')).toBeInTheDocument();
  });

  it('retries a proposal with the same operation ID after an uncertain network result', async () => {
    const requests: unknown[] = [];
    let proposed = false;
    fetchMock.mockImplementation(async (input, init) => {
      const path = String(input);
      if (path === '/api/blend-plans/plan-1') return json({ ...plan, canCancel: !proposed }, 200, { ETag: '"1"' });
      if (path === '/api/blend-plans/plan-1/proposal') return proposed ? json({ id: 'proposal-1', proposedAt: '2026-10-26T02:00:00Z', assessment: preview, freshness: 'CURRENT', freshnessReasons: [], stockReserved: false }) : json({}, 404);
      if (path.includes('/history?')) return json(emptyPage);
      if (path === '/api/ledger/lookups') return json({ facilities: [{ id: 'mill-1', name: 'Demo Mill' }], locations: [{ id: 'store-1', name: 'Dry Store', kind: 'STORE' }] });
      if (path === '/api/markets/market-1') return json({ code: 'EU', name: 'Europe' });
      if (path === '/api/commodities/rice-1') return json({ code: 'RICE', name: 'Rice' });
      if (path.includes('/candidates?')) return json(candidates);
      if (path === '/api/auth/csrf') return json({ token: 'csrf' });
      if (path === '/api/blend-plans/plan-1/preview' && init?.method === 'POST') return json(preview);
      if (path === '/api/blend-plans/plan-1/propose' && init?.method === 'POST') {
        requests.push(JSON.parse(String(init.body)));
        if (requests.length === 1) throw new TypeError('network disconnected');
        proposed = true;
        return json({ id: 'proposal-1' }, 201);
      }
      return json({}, 404);
    });
    const user = userEvent.setup();
    render(<BlendPlanWorkspace planId="plan-1" userId="staff" canPropose canReadLedger onNavigate={vi.fn()} />);
    await user.selectOptions(await screen.findByLabelText('Facility'), 'mill-1');
    await user.click(await screen.findByRole('button', { name: 'Calculate plan' }));
    await user.click(await screen.findByRole('button', { name: 'Save proposal' }));
    expect(await screen.findByText(/outcome is uncertain/i)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Retry same proposal' }));
    await waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1]).toEqual(requests[0]);
    expect(await screen.findByText('Saved proposal')).toBeInTheDocument();
    expect(within(screen.getByRole('heading', { name: 'Saved proposal' }).closest('section')!).getByText('CURRENT')).toBeInTheDocument();
  });

  it('carries the candidate scope revision to the next page', async () => {
    fetchMock.mockImplementation(async input => {
      const path = String(input);
      if (path === '/api/blend-plans/plan-1') return json(plan, 200, { ETag: '"1"' });
      if (path === '/api/blend-plans/plan-1/proposal') return json({}, 404);
      if (path.includes('/history?')) return json(emptyPage);
      if (path === '/api/ledger/lookups') return json({ facilities: [{ id: 'mill-1', name: 'Demo Mill' }], locations: [] });
      if (path.startsWith('/api/markets/') || path.startsWith('/api/commodities/')) return json({ code: 'RICE', name: 'Rice' });
      if (path.includes('/candidates?')) return path.includes('page=2') ? json({ ...candidates, page: 2, hasMore: false, items: [{ ...candidates.items[0], lotId: 'lot-2', lotCode: 'HDR-261026-000002' }] }) : json({ ...candidates, totalCount: 2, hasMore: true });
      return json({}, 404);
    });
    const user = userEvent.setup();
    render(<BlendPlanWorkspace planId="plan-1" userId="staff" canPropose canReadLedger onNavigate={vi.fn()} />);
    await user.selectOptions(await screen.findByLabelText('Facility'), 'mill-1');
    const stock = await screen.findByRole('heading', { name: 'Stock and sources' });
    const section = stock.closest('section')!;
    await within(section).findByText('HDR-261026-000001');
    await user.click(within(section).getByRole('button', { name: 'Next' }));
    await within(section).findByText('HDR-261026-000002');
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('page=2') && String(url).includes('scopeRevision=scope-1'))).toBe(true);
  });

  it('shows a stale saved proposal without offering preview or cancellation', async () => {
    fetchMock.mockImplementation(async input => {
      const path = String(input);
      if (path === '/api/blend-plans/plan-1') return json({ ...plan, hasProposal: true, canCancel: false }, 200, { ETag: '"1"' });
      if (path === '/api/blend-plans/plan-1/proposal') return json({ id: 'proposal-1', proposedAt: '2026-10-26T02:00:00Z', assessment: preview,
        freshness: 'STALE', freshnessReasons: ['DEMAND_CHANGED'], stockReserved: false });
      if (path.includes('/history?')) return json(emptyPage);
      if (path === '/api/ledger/lookups') return json({ facilities: [], locations: [] });
      if (path.startsWith('/api/markets/') || path.startsWith('/api/commodities/')) return json({ code: 'RICE', name: 'Rice' });
      return json({}, 404);
    });
    render(<BlendPlanWorkspace planId="plan-1" userId="manager" canPropose={false} canReadLedger onNavigate={vi.fn()} />);
    expect(await screen.findByText(/planning inputs changed/i)).toBeInTheDocument();
    expect(screen.getByText('STALE')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Calculate plan' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel draft' })).not.toBeInTheDocument();
  });
});

describe('F15 proposal decisions', () => {
  const recordedAt = '2026-11-13T00:00:00Z';
  const saved = { id: 'proposal-1', proposedAt: recordedAt, assessment: preview };
  function mockDecision({ failOnce = false, unavailable = false, stale = false, started = false } = {}) {
    let status = started ? 'APPROVED' : 'PENDING';
    let reason: string | null = null;
    let history: string[] = [];
    const requests: string[] = [];
    function state(action = status) {
      return { proposalId: saved.id, status: action, decisionVersion: action === 'PENDING' ? 0 : action === 'REVOKED' ? 2 : 1,
        reason, recordedAt: action === 'PENDING' ? null : recordedAt, activeApproval: action === 'APPROVED',
        reservation: { status: action === 'APPROVED' ? 'HELD' : action === 'REVOKED' ? 'RELEASED' : 'NOT_RESERVED',
          reservedKg: ['APPROVED', 'REVOKED'].includes(action) ? '1000' : '0', releasedKg: action === 'REVOKED' ? '1000' : '0',
          consumedKg: '0', remainingKg: action === 'APPROVED' ? '1000' : '0' } };
    }
    fetchMock.mockImplementation(async (input, init) => {
      const path = String(input);
      if (path.endsWith('/plan-1')) return json({ ...plan, hasProposal: true });
      if (path.endsWith('/proposal')) return json({ ...saved, freshness: status !== 'PENDING' ? 'NOT_RECHECKED' : stale ? 'STALE' : 'CURRENT' });
      if (path.endsWith('/decision')) return unavailable ? json({ code: 'DECISION_SCHEMA_NOT_READY' }, 503) : json(state());
      if (path.endsWith('/container-1')) return json({ status: started ? 'LOADING' : 'PLANNED' });
      if (path.includes('/history?')) return json({ ...emptyPage, items: history.map(action => ({ action, recordedAt, actorName: 'Demo Manager' })), totalCount: history.length });
      if (path.endsWith('/csrf')) return json({ token: 'csrf' });
      if (init?.method === 'POST' && /\/(approve|reject|revoke)$/.test(path)) {
        const body = String(init.body);
        requests.push(body);
        if (requests.length === 2 && failOnce) return json(state());
        const action = path.split('/').at(-1);
        status = action === 'approve' ? 'APPROVED' : action === 'reject' ? 'REJECTED' : 'REVOKED';
        reason = JSON.parse(body).reason ?? null;
        history = [`PLAN_${status}`, ...history];
        if (failOnce && requests.length === 1) throw new TypeError('Network failure');
        return json(state());
      }
      return json({}, 404);
    });
    return requests;
  }

  function open(canApprove = true) {
    render(<BlendPlanWorkspace planId="plan-1" userId={canApprove ? 'manager' : 'staff'} canApprove={canApprove} canPropose={false} canReadLedger={false} onNavigate={vi.fn()} />);
  }

  it('retries the exact approval after a lost response, then revokes and refreshes history', async () => {
    const requests = mockDecision({ failOnce: true });
    const user = userEvent.setup();
    open();
    await user.click(await screen.findByRole('button', { name: 'Approve proposal' }));
    await user.click(screen.getByRole('button', { name: 'Approve and reserve stock' }));
    await user.click(await screen.findByRole('button', { name: 'Retry same decision' }));
    await user.click(await screen.findByRole('button', { name: 'Revoke approval' }));
    expect(requests[1]).toBe(requests[0]);
    expect(JSON.parse(requests[0])).toMatchObject({ proposalId: saved.id, expectedDecisionVersion: 0 });
    expect(screen.getByText('Still held').nextElementSibling).toHaveTextContent('1000 kg');
    const revoke = screen.getByRole('button', { name: 'Revoke approval and release stock' });
    expect(revoke).toBeDisabled();
    await user.type(screen.getByRole('textbox', { name: 'Reason' }), 'Buyer cancelled');
    await user.click(revoke);
    expect(await screen.findByText('Plan revoked')).toBeInTheDocument();
    expect(await screen.findByText('Buyer cancelled')).toBeInTheDocument();
    expect(screen.getByText('Still held').nextElementSibling).toHaveTextContent('0 kg');
    expect(JSON.parse(requests[2])).toMatchObject({ proposalId: saved.id, expectedDecisionVersion: 1, reason: 'Buyer cancelled' });
  });

  it('allows rejection with a reason even when a saved proposal is stale', async () => {
    const requests = mockDecision({ stale: true });
    const user = userEvent.setup();
    open();
    await user.click(await screen.findByRole('button', { name: 'Reject proposal' }));
    expect(screen.queryByRole('button', { name: 'Approve proposal' })).not.toBeInTheDocument();
    await user.type(screen.getByRole('textbox', { name: 'Reason' }), 'Stock changed');
    await user.click(screen.getByRole('button', { name: 'Reject proposal' }));
    expect(await screen.findByText('Plan rejected')).toBeInTheDocument();
    expect(JSON.parse(requests[0])).toMatchObject({ reason: 'Stock changed', expectedDecisionVersion: 0 });
  });

  it('lets staff read a decision without manager actions', async () => {
    mockDecision();
    open(false);
    expect(await screen.findByText('Pending review')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reject proposal' })).not.toBeInTheDocument();
  });

  it('hides revocation after container execution starts', async () => {
    mockDecision({ started: true });
    open();
    expect(await screen.findByText('Revocation is unavailable once container execution begins.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Revoke approval' })).not.toBeInTheDocument();
  });

  it('keeps the saved proposal visible when decision storage is unavailable', async () => {
    mockDecision({ unavailable: true });
    open();
    expect(await screen.findByText(/Decision and reservation details are unavailable/)).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Saved proposal' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approve proposal' })).not.toBeInTheDocument();
  });
});
