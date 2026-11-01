import { useEffect, useRef, useState } from 'react';
import { api, apiWithEtag, ApiError, errorMessage } from './api';

type Page<T> = { items: T[]; page: number; totalCount: number; hasMore: boolean };
type Context = { orderVersion: number; containerVersion: number; storageReady: boolean; canCreateDraft: boolean; candidateReadReady: boolean; compatibilityReadReady: boolean; missingDependencies: { code: string; description: string }[]; allowedTargetProducts: string[] };
type Plan = { id: string; orderId: string; containerId: string; versionNo: number; version: number; status: string; targetMassKg: string; targetProductIdentity: string | null; demandSnapshot: string; createdAt: string; demandCurrent: boolean; canCancel: boolean; hasProposal: boolean };
type Candidate = { lotId: string; lotCode: string; locationId: string; locationName: string; availableKg: string; stockKind: string; productIdentity: string; productCompatibility: string; inventoryEligible: boolean; isCurrentPool: boolean; poolRevision: string | null; sourceCount: number; candidateRevision: string; restrictions: string[]; reasons: string[] };
type Candidates = Page<Candidate> & { scopeRevision: string; summary: { inventoryEligibleCount: number; excludedCount: number; inventoryEligibleMassKg: string; inventoryShortfallKg: string; directProductAvailableMassKg: string | null } };
type Selection = { mode: string; selectedMassKg: string; allocations: { lotId: string; locationId: string; lotCode: string; quantityKg: string }[] };
type Finding = { code: string; status: string; actual: string | null; limit: string | null; unit: string | null; reference: string | null };
type Assessment = { outcome: string; iterations: number; reasons: string[]; selection: Selection | null; assessmentStatus: string; assessmentFindings: Finding[]; predictedOutputKg: string | null; evidence?: unknown };
type Preview = Assessment & { previewReference: string | null; canPropose: boolean };
type Proposal = { id: string; proposedAt: string; assessment: Assessment; freshness: string; freshnessReasons?: string[]; stockReserved: boolean };
type History = { action: string; recordedAt: string; actorName: string };
type Lookup = { facilities: { id: string; name: string }[]; locations: { id: string; name: string; kind: string }[] };
type Lineage = { composition: { ledgerSourceLotId: string; captureSourceLotId: string | null; lotAttributedKg: string }[]; positionAttributionStatus: string; lineage: { complete: boolean; nodes: unknown[]; edges: unknown[]; operations: unknown[]; reasons: string[] } };
type Pending = { route: string; body: string; etag?: string };

const label = (code: string) => code.replaceAll('_', ' ').toLowerCase().replace(/^./, letter => letter.toUpperCase());
function displayFindingLimit(finding: Finding) {
  if (!finding.limit) return '—';
  if (finding.code !== 'QUALITY_LIMIT' || !finding.limit.includes('..')) return finding.limit;
  const [minimum, maximum] = finding.limit.split('..');
  const concise = (value: string) => value.replace(/(\.\d*?)0+$/, '$1').replace(/\.$/, '');
  if (minimum === '-') return maximum === '-' ? '—' : `≤ ${concise(maximum)}`;
  if (maximum === '-') return `≥ ${concise(minimum)}`;
  return `${concise(minimum)}–${concise(maximum)}`;
}
function findingArea(code: string) {
  if (code.includes('MRL') || code.startsWith('RESIDUE') || code.startsWith('ASSESSMENT')) return 'Residues and market';
  if (code.startsWith('QUALITY')) return 'Product quality';
  return 'Stock and output';
}
function findingAction(code: string, status: string) {
  if (status === 'PASS') return '—';
  if (code === 'ASSESSMENT_POLICY_MISSING') return 'Customer Manager: publish the market assessment policy.';
  if (code === 'APPLICABLE_MRL_MISSING') return 'MRL administrator: publish an applicable MRL version.';
  if (code === 'OUTPUT_YIELD_POLICY_MISSING') return 'Mill Manager: publish a facility yield rule.';
  if (code === 'QUALITY_RULE_MISSING') return 'Customer Manager: add a reviewed quality calculation rule.';
  if (code === 'QUALITY_EVIDENCE_MISSING') return 'Customer Manager: confirm finished-lot quality with Ledger evidence.';
  if (code === 'RESIDUE_COVERAGE_INCOMPLETE') return 'Review residue evidence, interpretation rules and required substances.';
  if (code === 'QUALITY_SPECIFICATION_MISSING') return 'Add buyer quality requirements to the export order.';
  if (code === 'OUTPUT_MASS') return 'Check target mass, eligible stock and the yield rule.';
  if (code === 'RESIDUE_LIMIT' || code === 'QUALITY_LIMIT') return 'Review the selected lots and requirements; the assessed result exceeds a limit.';
  return 'Review the source stock, policy or evidence for this check.';
}
const uncertain = (cause: unknown) => !(cause instanceof ApiError) || cause.status >= 500 || cause.status === 408;
const pendingKey = (userId: string, resource: string, action: string) => `grainledger.f14.${userId}.${resource}.${action}`;
function readPending(key: string): Pending | null {
  try { const data = JSON.parse(sessionStorage.getItem(key) || 'null'); return data?.route && data?.body ? data as Pending : null; }
  catch { return null; }
}
function demand(snapshot: string): Record<string, unknown> {
  try { const value = JSON.parse(snapshot); return value && typeof value === 'object' ? value : {}; }
  catch { return {}; }
}
function Notice({ error }: { error: string }) { return error && <p className="notice error" role="alert">{error}</p>; }
function Pager({ page, count, more, onPage }: { page: number; count: number; more: boolean; onPage: (page: number) => void }) {
  return <div className="form-footer"><span>Page {page} · {count} total</span><div><button className="secondary" disabled={page <= 1} onClick={() => onPage(page - 1)}>Previous</button> <button className="secondary" disabled={!more} onClick={() => onPage(page + 1)}>Next</button></div></div>;
}

export function ContainerPlans({ containerId, userId, canPropose, onNavigate }: { containerId: string; userId: string; canPropose: boolean; onNavigate: (path: string) => void }) {
  const [context, setContext] = useState<Context | null>(null);
  const [plans, setPlans] = useState<Page<Plan> | null>(null);
  const [page, setPage] = useState(1);
  const [reload, setReload] = useState(0);
  const [error, setError] = useState('');
  const [targetError, setTargetError] = useState('');
  const targetRef = useRef<HTMLSelectElement>(null);
  const key = pendingKey(userId, containerId, 'draft');
  const [pending, setPending] = useState<Pending | null>(() => readPending(key));
  const [target, setTarget] = useState(() => {
    try { return pending ? JSON.parse(pending.body).targetProductIdentity as string : ''; }
    catch { return ''; }
  });
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    setError('');
    void api<Context>(`/api/export-containers/${containerId}/planning-context`, { signal: controller.signal })
      .then(next => { setContext(next); if (!next.storageReady) setPlans(null); })
      .catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); });
    void api<Page<Plan>>(`/api/export-containers/${containerId}/blend-plans?page=${page}&pageSize=10`, { signal: controller.signal })
      .then(setPlans).catch(cause => {
        if (!controller.signal.aborted && !(cause instanceof ApiError && cause.status === 503)) setError(errorMessage(cause));
      });
    return () => controller.abort();
  }, [containerId, page, reload]);
  async function create() {
    if (busy || !context && !pending || !pending && !target) return;
    const request = pending ?? { route: `/api/export-containers/${containerId}/blend-plans/drafts`, body: JSON.stringify({ operationId: crypto.randomUUID(), orderVersion: context!.orderVersion, containerVersion: context!.containerVersion, targetProductIdentity: target }) };
    setBusy(true); setError(''); setTargetError(''); sessionStorage.setItem(key, JSON.stringify(request));
    try {
      const plan = await api<Plan>(request.route, { method: 'POST', body: request.body });
      if (!plan?.id) throw new Error('The result is uncertain. Retry the same request.');
      sessionStorage.removeItem(key); setPending(null); onNavigate(`/export/blend-plans/${plan.id}`);
    } catch (cause) {
      if (uncertain(cause)) { setPending(request); setError('The result is uncertain. Retry the same request to avoid a duplicate draft.'); }
      else {
        sessionStorage.removeItem(key); setPending(null); setError(errorMessage(cause)); setReload(n => n + 1);
        if (cause instanceof ApiError && cause.errors?.targetProductIdentity) {
          setTargetError(cause.errors.targetProductIdentity.join(' ')); targetRef.current?.focus();
        }
      }
    } finally { setBusy(false); }
  }
  const allowedProducts = context?.allowedTargetProducts ?? [];
  const shownProducts = pending && target && !allowedProducts.includes(target)
    ? [target, ...allowedProducts] : allowedProducts;
  return <section className="panel export-section" aria-label="Blend plans">
    <div className="section-toolbar"><div><h2>Stock and blend plans</h2><p className="muted">Plan the stock needed for this container's target mass. Any physical blending happens at the facility before loading. This step does not reserve stock.</p></div><button className="secondary" onClick={() => setReload(n => n + 1)}>Refresh</button></div>
    <Notice error={error} />
    {context && (!context.storageReady || context.missingDependencies.length > 0) && <div className="notice pending" role="status">
      {context.storageReady ? 'Blend planning is temporarily unavailable because the server setup is incomplete.' : 'Blend planning is unavailable because its database setup is incomplete.'}
      <details><summary>Technical details</summary><ul>{context.missingDependencies.map(item => <li key={item.code}>{item.description}</li>)}</ul></details>
    </div>}
    {context?.storageReady && allowedProducts.length === 0 && <p className="notice pending">This order commodity has no supported F14 product category. Review the order commodity before creating a plan.</p>}
    {canPropose && ((context?.canCreateDraft && allowedProducts.length > 0) || pending) && <div className="f14-actions"><label>Product to ship<select ref={targetRef} value={target} disabled={busy || !!pending} onChange={event => { setTarget(event.target.value); setTargetError(''); }}><option value="">Select product</option>{shownProducts.map(product => <option key={product} value={product}>{label(product)}</option>)}</select><small className="muted">Ledger product category, not rice variety. Options follow the order commodity.</small>{targetError && <span className="field-error" role="alert">{targetError}</span>}</label><button disabled={busy || !pending && !target} onClick={() => void create()}>{pending ? 'Retry same draft request' : busy ? 'Creating…' : '+ Create draft'}</button></div>}
    {!plans && !error && context?.storageReady && <p role="status">Loading plans…</p>}
    {plans && (plans.items.length ? <><div className="table-scroll"><table><thead><tr><th>Version</th><th>Product</th><th>Target</th><th>State</th><th /></tr></thead><tbody>{plans.items.map(plan => <tr key={plan.id}><td>v{plan.versionNo}</td><td>{plan.targetProductIdentity ? label(plan.targetProductIdentity) : 'Unknown'}</td><td>{plan.targetMassKg} kg</td><td>{plan.hasProposal ? 'Proposal saved' : label(plan.status)}{!plan.demandCurrent && ' · Demand changed'}</td><td><button className="text-button" onClick={() => onNavigate(`/export/blend-plans/${plan.id}`)}>Open</button></td></tr>)}</tbody></table></div><Pager page={page} count={plans.totalCount} more={plans.hasMore} onPage={setPage} /></> : <p className="empty">No blend plan drafts yet.</p>)}
  </section>;
}

function AssessmentView({ assessment, locations }: { assessment: Assessment; locations: Lookup['locations'] }) {
  const hitLimit = assessment.reasons?.includes('ITERATION_LIMIT_REACHED');
  const evidencePending = assessment.outcome === 'SEARCH_EXHAUSTED' && assessment.assessmentStatus === 'INCOMPLETE' && !!assessment.selection;
  const otherReasons = assessment.reasons?.filter(reason => reason !== 'SEARCH_SPACE_EXHAUSTED' && reason !== 'ITERATION_LIMIT_REACHED') ?? [];
  return <div className="f14-assessment"><dl className="export-facts"><div><dt>Search</dt><dd>{evidencePending ? 'Candidate found' : label(assessment.outcome)}</dd></div><div><dt>Assessment</dt><dd>{assessment.assessmentStatus}</dd></div><div><dt>Selected input</dt><dd>{assessment.selection?.selectedMassKg ?? '—'} kg</dd></div><div><dt>Predicted output</dt><dd>{assessment.predictedOutputKg ?? '—'} kg</dd></div></dl>
    {hitLimit && <p className="notice">The search stopped at its iteration limit. This does not prove that no feasible plan exists.</p>}
    {evidencePending && !hitLimit && <p className="notice">Enough stock was selected, but the assessment is incomplete. Review the missing evidence below.</p>}
    {assessment.outcome === 'SEARCH_EXHAUSTED' && !hitLimit && !evidencePending && <p className="notice">No checked selection passed every requirement. Review the findings below.</p>}
    {otherReasons.length > 0 && <p className="muted">Search reasons: {otherReasons.map(label).join(', ')}</p>}
    {assessment.selection && <><h3>Selected stock</h3><p className="muted">{label(assessment.selection.mode)}</p><div className="table-scroll"><table><thead><tr><th>Lot</th><th>Location</th><th>Input</th></tr></thead><tbody>{assessment.selection.allocations.map((item, index) => <tr key={`${item.lotId}-${item.locationId}-${index}`}><td>{item.lotCode}</td><td>{locations.find(location => location.id === item.locationId)?.name ?? 'Location unavailable'}</td><td>{item.quantityKg} kg</td></tr>)}</tbody></table></div></>}
    <h3>Checks and evidence</h3>{assessment.assessmentFindings?.length ? <div className="table-scroll"><table><thead><tr><th>Area</th><th>Check</th><th>Result</th><th>Actual</th><th>Limit</th><th>Next step</th></tr></thead><tbody>{assessment.assessmentFindings.map((finding, index) => <tr key={`${finding.code}-${index}`}><td>{findingArea(finding.code)}</td><td>{label(finding.code)}</td><td>{finding.status}</td><td>{finding.actual ?? '—'} {finding.unit ?? ''}</td><td>{displayFindingLimit(finding)} {finding.unit ?? ''}</td><td>{findingAction(finding.code, finding.status)}</td></tr>)}</tbody></table></div> : <p>No findings returned.</p>}
    {assessment.evidence != null && <details><summary>Evidence versions and calculation details</summary><pre className="f14-json">{JSON.stringify(assessment.evidence, null, 2)}</pre></details>}
  </div>;
}

export function BlendPlanWorkspace({ planId, userId, canPropose, canReadLedger, onNavigate }: { planId: string; userId: string; canPropose: boolean; canReadLedger: boolean; onNavigate: (path: string) => void }) {
  const [plan, setPlan] = useState<Plan | null>(null);
  const [etag, setEtag] = useState<string | null>(null);
  const [proposal, setProposal] = useState<Proposal | null>(null);
  const [proposalError, setProposalError] = useState('');
  const [proposalLoading, setProposalLoading] = useState(true);
  const [history, setHistory] = useState<Page<History> | null>(null);
  const [historyPage, setHistoryPage] = useState(1);
  const [facilities, setFacilities] = useState<Lookup['facilities']>([]);
  const [locations, setLocations] = useState<Lookup['locations']>([]);
  const [marketName, setMarketName] = useState('');
  const [commodityName, setCommodityName] = useState('');
  const [facilityId, setFacilityId] = useState('');
  const [candidatePage, setCandidatePage] = useState(1);
  const [includeExcluded, setIncludeExcluded] = useState(false);
  const [showOtherProducts, setShowOtherProducts] = useState(false);
  const [candidates, setCandidates] = useState<Candidates | null>(null);
  const [candidateLoading, setCandidateLoading] = useState(false);
  const [lineage, setLineage] = useState<Lineage | null>(null);
  const [lineageError, setLineageError] = useState('');
  const [preview, setPreview] = useState<Preview | null>(null);
  const [previewExpired, setPreviewExpired] = useState(false);
  const [error, setError] = useState('');
  const [stockError, setStockError] = useState('');
  const [previewError, setPreviewError] = useState('');
  const [cancelError, setCancelError] = useState('');
  const [busy, setBusy] = useState(false);
  const [reload, setReload] = useState(0);
  const cancelKey = pendingKey(userId, planId, 'cancel');
  const proposeKey = pendingKey(userId, planId, 'propose');
  const [pendingCancel, setPendingCancel] = useState<Pending | null>(() => readPending(cancelKey));
  const [pendingPropose, setPendingPropose] = useState<Pending | null>(() => readPending(proposeKey));
  useEffect(() => {
    const controller = new AbortController(); setError(''); setProposal(null); setProposalLoading(true);
    void apiWithEtag<Plan>(`/api/blend-plans/${planId}`, { signal: controller.signal })
      .then(result => { setPlan(result.data); setEtag(result.etag); })
      .catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); });
    void api<Proposal>(`/api/blend-plans/${planId}/proposal`, { signal: controller.signal })
      .then(value => { setProposal(value); setProposalError(''); })
      .catch(cause => { if (!controller.signal.aborted) {
        if (cause instanceof ApiError && cause.status === 404) { setProposal(null); setProposalError(''); }
        else setProposalError(errorMessage(cause));
      } }).finally(() => { if (!controller.signal.aborted) setProposalLoading(false); });
    return () => controller.abort();
  }, [planId, reload]);
  useEffect(() => {
    if (!preview?.previewReference) return;
    // The API grants ten minutes. Leave a small margin for transport and clock skew.
    const timer = window.setTimeout(() => setPreviewExpired(true), 570_000);
    return () => window.clearTimeout(timer);
  }, [preview?.previewReference]);
  useEffect(() => {
    const controller = new AbortController();
    void api<Page<History>>(`/api/blend-plans/${planId}/history?page=${historyPage}&pageSize=10`, { signal: controller.signal })
      .then(setHistory).catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); });
    return () => controller.abort();
  }, [planId, historyPage, reload]);
  useEffect(() => {
    if (!plan) return;
    const snapshot = demand(plan.demandSnapshot);
    const controller = new AbortController();
    if (typeof snapshot.marketId === 'string') void api<{ code: string; name: string }>(`/api/markets/${snapshot.marketId}`, { signal: controller.signal })
      .then(value => setMarketName(`${value.code} · ${value.name}`)).catch(() => {});
    if (typeof snapshot.commodityId === 'string') void api<{ code: string; name: string }>(`/api/commodities/${snapshot.commodityId}`, { signal: controller.signal })
      .then(value => setCommodityName(`${value.code} · ${value.name}`)).catch(() => {});
    return () => controller.abort();
  }, [plan?.id, plan?.demandSnapshot]);
  useEffect(() => {
    if (!canReadLedger) return;
    const controller = new AbortController();
    void api<Lookup>('/api/ledger/lookups', { signal: controller.signal })
      .then(value => { setFacilities(value.facilities ?? []); setLocations(value.locations ?? []); })
      .catch(cause => { if (!controller.signal.aborted) setStockError(`Facilities could not load: ${errorMessage(cause)}`); });
    return () => controller.abort();
  }, [canReadLedger]);
  useEffect(() => {
    if (!facilityId || !plan?.demandCurrent || plan.status !== 'DRAFT' || plan.hasProposal || proposal || proposalLoading || proposalError) return;
    const controller = new AbortController();
    setCandidateLoading(true);
    const query = new URLSearchParams({ facilityId, planId, page: String(candidatePage), pageSize: '25', includeExcluded: String(includeExcluded), productMatchOnly: String(!showOtherProducts) });
    if (candidatePage > 1 && candidates?.scopeRevision) query.set('scopeRevision', candidates.scopeRevision);
    void api<Candidates>(`/api/export-containers/${plan.containerId}/candidates?${query}`, { signal: controller.signal })
      .then(value => { setCandidates(value); setStockError(''); setCandidateLoading(false); })
      .catch(cause => {
        if (controller.signal.aborted) return;
        setCandidates(null); setPreview(null); setCandidateLoading(false);
        if (cause instanceof ApiError && cause.status === 409) { setCandidatePage(1); setStockError('Stock changed. Reload candidates from page one.'); }
        else setStockError(errorMessage(cause));
      });
    return () => controller.abort();
  // Reload intentionally resets scope after any change of facility, filter, or plan.
  }, [planId, plan?.containerId, plan?.demandCurrent, plan?.status, plan?.hasProposal, proposal, proposalLoading, proposalError, facilityId, candidatePage, includeExcluded, showOtherProducts, reload]);
  async function openLineage(item: Candidate) {
    if (!plan) return;
    setLineage(null); setLineageError('');
    const query = new URLSearchParams({ facilityId, locationId: item.locationId, candidateRevision: item.candidateRevision });
    try { setLineage(await api<Lineage>(`/api/export-containers/${plan.containerId}/candidates/${item.lotId}/lineage?${query}`)); }
    catch (cause) { setLineageError(errorMessage(cause)); if (cause instanceof ApiError && cause.status === 409) refreshCandidates(); }
  }
  function refreshCandidates() { setCandidatePage(1); setCandidates(null); setPreview(null); setLineage(null); setStockError(''); setReload(n => n + 1); }
  async function runPreview() {
    if (!candidates || !facilityId || busy) return;
    setBusy(true); setPreviewError(''); setPreview(null);
    try { setPreview(await api<Preview>(`/api/blend-plans/${planId}/preview`, { method: 'POST', body: JSON.stringify({ facilityId, scopeRevision: candidates.scopeRevision }) })); setPreviewExpired(false); }
    catch (cause) { setPreviewError(errorMessage(cause)); if (cause instanceof ApiError && cause.status === 409) refreshCandidates(); }
    finally { setBusy(false); }
  }
  async function propose() {
    if (busy || !pendingPropose && (!preview?.canPropose || previewExpired)) return;
    const request = pendingPropose ?? { route: `/api/blend-plans/${planId}/propose`, body: JSON.stringify({ operationId: crypto.randomUUID(), previewReference: preview!.previewReference }) };
    setBusy(true); setPreviewError(''); sessionStorage.setItem(proposeKey, JSON.stringify(request));
    try {
      const result = await api<Proposal>(request.route, { method: 'POST', body: request.body });
      if (!result?.id) throw new Error('The result is uncertain. Retry the same proposal request.');
      sessionStorage.removeItem(proposeKey); setPendingPropose(null); setPreview(null); setCandidates(null); setReload(n => n + 1);
    } catch (cause) {
      if (uncertain(cause)) { setPendingPropose(request); setPreviewError('The result is uncertain. Retry the same proposal request.'); }
      else { sessionStorage.removeItem(proposeKey); setPendingPropose(null); setPreviewError(errorMessage(cause)); if (cause instanceof ApiError && cause.status === 409) setReload(n => n + 1); }
    } finally { setBusy(false); }
  }
  async function cancel() {
    if (busy || !pendingCancel && (!plan?.canCancel || !etag)) return;
    if (!pendingCancel && !window.confirm('Cancel this draft? Its history will remain available.')) return;
    const request = pendingCancel ?? { route: `/api/blend-plans/${planId}/cancel`, body: JSON.stringify({ operationId: crypto.randomUUID() }), etag: etag! };
    setBusy(true); setCancelError(''); sessionStorage.setItem(cancelKey, JSON.stringify(request));
    try {
      const result = await api<Plan>(request.route, { method: 'POST', headers: { 'If-Match': request.etag ?? '' }, body: request.body });
      if (!result?.id) throw new Error('The result is uncertain. Retry the same cancellation request.');
      sessionStorage.removeItem(cancelKey); setPendingCancel(null); setReload(n => n + 1);
    } catch (cause) {
      if (uncertain(cause)) { setPendingCancel(request); setCancelError('The result is uncertain. Retry the same cancellation request.'); }
      else { sessionStorage.removeItem(cancelKey); setPendingCancel(null); setCancelError(errorMessage(cause)); setReload(n => n + 1); }
    } finally { setBusy(false); }
  }
  const snapshot = plan ? demand(plan.demandSnapshot) : {};
  const active = plan?.status === 'DRAFT' && plan.demandCurrent && !plan.hasProposal && !proposal && !proposalError && !proposalLoading;
  return <>
    <button className="text-button" onClick={() => onNavigate(plan ? `/export/containers/${plan.containerId}` : '/export/orders')}>← Back to container</button>
    <div className="page-heading section-toolbar"><div><span className="eyebrow">BLEND PLANNING</span><h1>Plan {plan ? `v${plan.versionNo}` : ''}</h1><p className="muted">A saved proposal records an assessment. Stock is reserved later during approval.</p></div><button className="secondary" onClick={() => setReload(n => n + 1)}>Refresh</button></div>
    <Notice error={error} />
    {!plan && !error && <p role="status">Loading plan…</p>}
    {plan && <><section className="panel export-section"><div className="section-toolbar"><h2>Demand snapshot</h2>{canPropose && plan.canCancel && !pendingCancel && !pendingPropose && <button className="secondary" disabled={busy} onClick={() => void cancel()}>Cancel draft</button>}</div>
      <Notice error={cancelError} />{pendingCancel && <p className="notice error">Cancellation outcome is uncertain. <button disabled={busy} onClick={() => void cancel()}>Retry same cancellation</button></p>}
      <dl className="export-facts"><div><dt>Status</dt><dd>{plan.hasProposal || proposal ? 'Proposal saved' : label(plan.status)}</dd></div><div><dt>Target</dt><dd>{plan.targetMassKg} kg</dd></div><div><dt>Product</dt><dd>{label(plan.targetProductIdentity || 'Unknown')}</dd></div><div><dt>Demand</dt><dd>{plan.demandCurrent ? 'Current' : 'Changed since draft'}</dd></div></dl>
      <p className="muted">Market: {marketName || 'Unavailable'} · Commodity: {commodityName || 'Unavailable'} · Shipping {String(snapshot.shippingFrom ?? '—')} to {String(snapshot.shippingTo ?? '—')}.</p>
      {plan.targetProductIdentity && !['HEAD_RICE', 'BROKEN_RICE', 'BLENDED_RICE', 'BAGGED_RICE'].includes(plan.targetProductIdentity) && <p className="notice pending" role="status">This product cannot complete the current F14 quality check. Ledger accepts confirmed planning-quality observations only for finished rice lots. To prepare an assessable proposal, first process stock into finished rice and use an order for the corresponding commodity.</p>}
      {Object.keys(snapshot).length === 0 && <p className="notice error" role="alert">This draft's demand snapshot could not be read. Open the container and create a new draft.</p>}
      {!plan.demandCurrent && <p className="notice">Order, specification or container target changed. <button className="text-button" onClick={() => onNavigate(`/export/containers/${plan.containerId}`)}>Open container to create a new draft</button></p>}
      {Array.isArray(snapshot.limits) && <details><summary>Buyer quality specification</summary><div className="table-scroll"><table><thead><tr><th>Attribute</th><th>Minimum</th><th>Maximum</th><th>Unit</th></tr></thead><tbody>{(snapshot.limits as Record<string, unknown>[]).map((row, index) => <tr key={index}><td>{String(row.attributeName ?? row.attributeCode ?? '—')}</td><td>{String(row.minValue ?? '—')}</td><td>{String(row.maxValue ?? '—')}</td><td>{String(row.unit ?? '—')}</td></tr>)}</tbody></table></div></details>}
    </section>
    {proposalError && <p className="notice error" role="alert">Proposal status could not be checked: {proposalError}</p>}
    {pendingPropose && <section className="panel export-section"><h2>Proposal request</h2><Notice error={previewError} /><p className="notice error">Proposal outcome is uncertain. <button disabled={busy} onClick={() => void propose()}>Retry same proposal</button></p></section>}
    {proposal && <section className="panel export-section"><h2>Saved proposal</h2><p>Recorded {new Date(proposal.proposedAt).toLocaleString()} · Freshness: <strong>{proposal.freshness}</strong></p>{proposal.freshness === 'STALE' && <p className="notice error">Planning inputs changed: {(proposal.freshnessReasons ?? []).map(label).join(', ')}. <button className="text-button" onClick={() => onNavigate(`/export/containers/${plan.containerId}`)}>Open container to create a new draft</button></p>}<p className="muted">Stock reserved: No</p><AssessmentView assessment={proposal.assessment} locations={locations} /></section>}
    {active && <section className="panel export-section"><h2>Stock and sources</h2><p className="muted">Facility is the mill. A location is a silo or store within it. A lot or mixed pool is stock at a location. Physical stock status does not confirm that its product matches this plan.</p>
      <Notice error={stockError} />
      {!canReadLedger && <p className="notice">Facility lookups are unavailable for this role.</p>}
      <div className="f14-actions"><label>Facility<select aria-label="Facility" value={facilityId} disabled={!canReadLedger} onChange={event => { setFacilityId(event.target.value); setCandidatePage(1); setCandidates(null); setPreview(null); setLineage(null); }}><option value="">Select facility</option>{facilities.map(facility => <option key={facility.id} value={facility.id}>{facility.name}</option>)}</select></label><label className="checkbox-label"><input type="checkbox" checked={showOtherProducts} onChange={event => { setShowOtherProducts(event.target.checked); setCandidatePage(1); setCandidates(null); setPreview(null); setLineage(null); }} /> Show other products</label><label className="checkbox-label"><input type="checkbox" checked={includeExcluded} onChange={event => { setIncludeExcluded(event.target.checked); setCandidatePage(1); setCandidates(null); setPreview(null); setLineage(null); }} /> Show excluded stock</label><button className="secondary" disabled={!facilityId} onClick={refreshCandidates}>Refresh stock</button></div>
      {facilityId && candidateLoading && <p role="status">Loading candidates…</p>}
      {candidates && !candidateLoading && <><dl className="export-facts"><div><dt>Facility positions</dt><dd>{candidates.summary.inventoryEligibleCount}</dd></div><div><dt>Facility stock</dt><dd>{candidates.summary.inventoryEligibleMassKg} kg</dd></div><div><dt>Direct product match</dt><dd>{candidates.summary.directProductAvailableMassKg ?? '—'} kg</dd></div><div><dt>Excluded positions</dt><dd>{candidates.summary.excludedCount}</dd></div></dl>
        {candidates.summary.inventoryShortfallKg !== '0' && <p className="muted">Physical stock alone is {candidates.summary.inventoryShortfallKg} kg below the target. Product and compliance checks may reduce usable stock further.</p>}
        {candidates.summary.directProductAvailableMassKg === '0' && <p className="notice pending">No stock directly matches {label(plan.targetProductIdentity || 'this product')} in this facility.{plan.targetProductIdentity === 'BLENDED_RICE' ? ' Compatible input lots may still support a blend if an approved policy and complete evidence exist.' : ' Stock at an earlier processing stage must be processed in Ledger before it can supply this plan.'}</p>}
        {candidates.items.length ? <div className="table-scroll"><table><thead><tr><th>Lot</th><th>Location</th><th>Available</th><th>Product</th><th>Plan fit</th><th>Physical status</th><th /></tr></thead><tbody>{candidates.items.map(item => <tr key={`${item.lotId}-${item.locationId}`}><td>{item.lotCode}{item.isCurrentPool && <small> · Mixed pool</small>}</td><td>{item.locationName}</td><td>{item.availableKg} kg</td><td>{label(item.productIdentity)}</td><td>{label(item.productCompatibility)}</td><td>{item.inventoryEligible ? 'In stock' : item.reasons.map(label).join(', ') || 'Excluded'}{item.restrictions.length > 0 && ` · ${item.restrictions.map(label).join(', ')}`}</td><td><button className="text-button" onClick={() => void openLineage(item)}>Sources</button></td></tr>)}</tbody></table></div> : <p className="empty">No stock matching this product is shown. Use Show other products to inspect the rest of the facility.</p>}
        <Pager page={candidatePage} count={candidates.totalCount} more={candidates.hasMore} onPage={page => { setCandidatePage(page); setPreview(null); setLineage(null); }} /></>}
      <Notice error={lineageError} />
      {lineage && <div className="f14-lineage"><div className="section-toolbar"><h3>Source composition</h3><button className="secondary" onClick={() => setLineage(null)}>Close</button></div><p>Lineage {lineage.lineage.complete ? 'complete' : 'incomplete'} · Position attribution {label(lineage.positionAttributionStatus)}</p><div className="table-scroll"><table><thead><tr><th>Source</th><th>Attributed mass</th></tr></thead><tbody>{lineage.composition.map((source, index) => <tr key={`${source.ledgerSourceLotId}-${index}`}><td>{source.captureSourceLotId ? <button className="text-button" onClick={() => onNavigate(`/capture/source-lots/${source.captureSourceLotId}`)}>Open source lot</button> : 'Ledger source'}</td><td>{source.lotAttributedKg} kg</td></tr>)}</tbody></table></div><details><summary>Processing and custody graph</summary><pre className="f14-json">{JSON.stringify(lineage.lineage, null, 2)}</pre></details></div>}
    </section>}
    {active && facilityId && candidates && !candidateLoading && <section className="panel export-section"><div className="section-toolbar"><div><h2>Assessed preview</h2><p className="muted">The server selects candidate quantities and checks the complete plan.</p></div>{canPropose && <button disabled={busy || !!pendingCancel || !!pendingPropose} onClick={() => void runPreview()}>{busy ? 'Working…' : 'Calculate plan'}</button>}</div>
      {!pendingPropose && <Notice error={previewError} />}
      {!canPropose && <p className="notice">Your role can inspect drafts but cannot calculate or propose a plan.</p>}
      {preview && <><AssessmentView assessment={preview} locations={locations} />{canPropose && preview.canPropose && preview.previewReference && <div className="f14-actions"><p className="muted">{previewExpired ? 'The preview has expired. Calculate the plan again.' : 'This preview reference expires after ten minutes. The server checks all inputs again when you propose.'}</p><button disabled={busy || previewExpired} onClick={() => void propose()}>Save proposal</button></div>}</>}
    </section>}
    <section className="panel export-section"><h2>History</h2>{history ? <>{history.items.length ? <ul className="f14-history">{history.items.map((event, index) => <li key={`${event.recordedAt}-${index}`}><strong>{label(event.action)}</strong> · {event.actorName} · {new Date(event.recordedAt).toLocaleString()}</li>)}</ul> : <p>No history yet.</p>}<Pager page={historyPage} count={history.totalCount} more={history.hasMore} onPage={setHistoryPage} /></> : <p role="status">Loading history…</p>}</section></>}
  </>;
}
