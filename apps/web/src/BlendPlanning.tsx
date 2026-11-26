import { useEffect, useRef, useState } from 'react';
import { api, apiWithEtag, ApiError, errorMessage } from './api';

type Page<T> = { items: T[]; page: number; totalCount: number; hasMore: boolean };
type Context = { orderVersion: number; containerVersion: number; storageReady: boolean; canCreateDraft: boolean; candidateReadReady: boolean; compatibilityReadReady: boolean; missingDependencies: { code: string; description: string }[]; allowedTargetProducts: string[] };
type Plan = { id: string; orderId: string; containerId: string; versionNo: number; version: number; status: string; targetMassKg: string; targetProductIdentity: string | null; demandSnapshot: string; createdAt: string; demandCurrent: boolean; demandChanged?: boolean; canCancel: boolean; hasProposal: boolean };
type Candidate = { lotId: string; lotCode: string; locationId: string; locationName: string; availableKg: string; stockKind: string; productIdentity: string; productCompatibility: string; inventoryEligible: boolean; isCurrentPool: boolean; poolRevision: string | null; sourceCount: number; candidateRevision: string; restrictions: string[]; reasons: string[] };
type Candidates = Page<Candidate> & { scopeRevision: string; summary: { inventoryEligibleCount: number; excludedCount: number; inventoryEligibleMassKg: string; inventoryShortfallKg: string; directProductAvailableMassKg: string | null } };
type Selection = { mode: string; selectedMassKg: string; allocations: { lotId: string; locationId: string; lotCode: string; quantityKg: string }[] };
type Finding = { code: string; status: string; actual: string | null; limit: string | null; unit: string | null; reference: string | null };
type Assessment = { outcome: string; iterations: number; reasons: string[]; selection: Selection | null; assessmentStatus: string; assessmentFindings: Finding[]; predictedOutputKg: string | null; evidence?: unknown };
type Optimization = { method: string; objective: string | null; positionsUsed: number | null; headRiceUsedKg?: string | null; lotsUsed?: number | null; weightedMoisture?: string | null; optimal: boolean };
type CalculationAttempt = {
  mode: string; candidateCount: number; inputKg: number; status: string;
  variables: number; constraints: number; elapsedMs: number; objective: number | null;
  bestBound: number | null; nodes: number | null; assessmentStatus: string | null;
};
type Preview = Assessment & { previewReference: string | null; canPropose: boolean;
  ratioRange?: { minimumKg: string; maximumKg: string } | null;
  optimization?: Optimization;
  calculationSteps?: { method: string; attempts: CalculationAttempt[] } | null;
  substanceNames?: Record<string, string>;
  ratioQuality?: { attributeId: string; first: string; second: string; unit: string }[] | null;
  residueHeadroom?: { substanceId: string; ratio: number } | null;
  inputResidueExclusions?: { lotId: string; lotCode: string; substanceId: string; code: string; status: string;
    upper: string | null; mrl: string | null; unit: string | null }[];
};

function CalculationSteps({ preview }: { preview: Preview }) {
  const calculation = preview.calculationSteps;
  if (!calculation) return null;
  const source = calculation.method === 'MILP' ? 'MILP'
    : calculation.method === 'GREEDY_FALLBACK' ? 'Greedy fallback'
    : 'Recheck of the selected ratio';
  return <details className="export-details"><summary>Calculation steps (development)</summary>
    <ol>
      <li><strong>Build and solve the model</strong>
        {calculation.attempts.length === 0 && <p>No solver attempt was recorded for this preview.</p>}
        {calculation.attempts.map((attempt, index) => <div key={index}>
          <p>{label(attempt.mode)} · {attempt.inputKg} kg input · {attempt.candidateCount} candidates</p>
          <p>{attempt.variables} variables · {attempt.constraints} constraints · {attempt.status} · {attempt.elapsedMs} ms
            {attempt.nodes !== null && ` · ${attempt.nodes} nodes`}
            {attempt.objective !== null && ` · Objective ${attempt.objective}`}
            {attempt.bestBound !== null && ` · Bound ${attempt.bestBound}`}
            {attempt.assessmentStatus && ` · F14 ${attempt.assessmentStatus}`}
          </p>
        </div>)}
      </li>
      <li><strong>Selected quantities · {source}</strong>
        <p>{preview.selection?.allocations.map(row => `${row.lotCode}: ${row.quantityKg} kg`).join(' · ') || 'No allocation'}</p>
        <p>Input: {preview.selection?.selectedMassKg ?? '—'} kg · Predicted output: {preview.predictedOutputKg ?? '—'} kg</p>
      </li>
      <li><strong>F14 verification · {preview.assessmentStatus}</strong>
        <p>See Checks and evidence for quality limits and the result for each residue.</p>
        {preview.reasons.length > 0 && <p>{preview.reasons.map(label).join(', ')}</p>}
      </li>
    </ol>
  </details>;
}

function remainingKg(total: string, first: string) {
  const toUnits = (kg: string) => {
    const [whole, fraction = ''] = kg.split('.');
    return BigInt(whole) * 1000000n + BigInt(fraction.padEnd(6, '0'));
  };
  const units = toUnits(total) - toUnits(first);
  const fraction = (units % 1000000n).toString().padStart(6, '0').replace(/0+$/, '');
  return fraction ? `${units / 1000000n}.${fraction}` : (units / 1000000n).toString();
}

function BlendRatioCard({ preview, value, busy, attributeName, onChange, onCheck }: {
  preview: Preview; value: string | null; busy: boolean; attributeName: (id: string) => string;
  onChange: (kg: string | null) => void; onCheck: (kg: string) => void;
}) {
  const selection = preview.selection;
  if (!preview.ratioRange || !selection || selection.allocations.length !== 2) return null;
  const [first, second] = selection.allocations;
  const [minWhole, minFraction] = preview.ratioRange.minimumKg.split('.');
  const minimum = Number(minWhole) + (Number(minFraction ?? '0') > 0 ? 1 : 0);
  const maximum = Number(preview.ratioRange.maximumKg.split('.')[0]);
  const total = Number(selection.selectedMassKg);
  const [wholeTotal] = selection.selectedMassKg.split('.');
  if (remainingKg(selection.selectedMassKg, wholeTotal) !== '0' || minimum > maximum)
    return <p className="muted small">Whole-kg adjustments are unavailable for this plan.</p>;

  const initial = Math.max(minimum, Math.min(maximum, Math.round(Number(first.quantityKg))));
  const firstKg = value ?? String(initial);
  const secondKg = remainingKg(selection.selectedMassKg, firstKg);
  const share = Number(firstKg) / total;
  const edited = remainingKg(first.quantityKg, firstKg) !== '0';
  const residues = preview.assessmentFindings.filter(finding => finding.code === 'RESIDUE_LIMIT');
  const kg = new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 });
  return <section className="f14-ratio-card">
    <h3>Try a different ratio</h3>
    <label className="f14-ratio-label">
      <span>Quantity from {first.lotCode}</span><strong>{kg.format(Number(firstKg))} kg</strong>
      <input type="range" min={minimum} max={maximum} step={1}
        value={firstKg} disabled={busy || minimum === maximum}
        aria-label={`Quantity from ${first.lotCode}`}
        onChange={event => {
          const next = event.target.valueAsNumber;
          if (Number.isInteger(next) && next >= minimum && next <= maximum)
            onChange(remainingKg(first.quantityKg, String(next)) === '0' ? null : String(next));
        }} />
    </label>
    <div className="f14-ratio-row muted small"><span>{kg.format(minimum)} kg</span><span>{kg.format(maximum)} kg</span></div>
    <div className="f14-ratio-bar" aria-hidden="true"><span style={{ width: `${(1 - share) * 100}%` }} /></div>
    <div className="f14-ratio-row f14-ratio-lots" aria-label="Lot quantities">
      <span>{second.lotCode}: <strong>{kg.format(Number(secondKg))} kg</strong></span>
      <span>{first.lotCode}: <strong>{kg.format(Number(firstKg))} kg</strong></span>
    </div>
    <dl className="f14-ratio-metrics">
      {preview.ratioQuality?.map(row => {
        const estimate = share * Number(row.first) + (1 - share) * Number(row.second);
        return <div key={row.attributeId}><dt>{attributeName(row.attributeId)}{edited && ' (estimated)'}</dt>
          <dd>{estimate.toFixed(3)} {row.unit}</dd></div>;
      })}
      {edited ? <div><dt>Residue</dt><dd>Pending check</dd></div> : residues.map((finding, index) =>
        <div key={finding.reference ?? index}>
          <dt>{(finding.reference && preview.substanceNames?.[finding.reference]) || 'Residue'} (upper bound)</dt>
          <dd>{finding.actual} {finding.unit}</dd>
        </div>)}
    </dl>
    <div className="f14-ratio-row f14-ratio-footer">
      <p className="muted small">{edited
        ? 'Whole kg only. Check this ratio before using it in the proposal.'
        : 'Whole kg only. The other lot adjusts automatically.'}</p>
      <button disabled={busy || !edited} onClick={() => { onChange(firstKg); onCheck(firstKg); }}>Check ratio</button>
    </div>
  </section>;
}

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
  if (code.startsWith('SOURCE_LAB_') || code.includes('MRL') || code.startsWith('RESIDUE') || code.startsWith('ASSESSMENT')) return 'Residues and market';
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
  if (code === 'SOURCE_LAB_SELECTION_MISSING') return 'Customer Manager: review and confirm a laboratory report selection for every contributing source and required substance.';
  if (code === 'SOURCE_LAB_SELECTION_CONFLICT') return 'Customer Manager: review conflicting laboratory reports and confirm supersession before planning.';
  if (code === 'SOURCE_LAB_REQUIRED') return 'Customer Manager: confirm laboratory results and review report selections for every contributing source and required substance. Source-group bounds cannot replace these results.';
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
    {plans && (plans.items.length ? <><div className="table-scroll"><table><thead><tr><th>Version</th><th>Product</th><th>Target</th><th>State</th><th /></tr></thead><tbody>{plans.items.map(plan => <tr key={plan.id}><td>v{plan.versionNo}</td><td>{plan.targetProductIdentity ? label(plan.targetProductIdentity) : 'Unknown'}</td><td>{plan.targetMassKg} kg</td><td>{plan.hasProposal ? 'Proposal saved' : label(plan.status)}{(plan.demandChanged ?? !plan.demandCurrent) && ' · Demand changed'}</td><td><button className="text-button" onClick={() => onNavigate(`/export/blend-plans/${plan.id}`)}>Open</button></td></tr>)}</tbody></table></div><Pager page={page} count={plans.totalCount} more={plans.hasMore} onPage={setPage} /></> : <p className="empty">No blend plan drafts yet.</p>)}
  </section>;
}

function AssessmentView({ assessment, locations, substanceNames = {} }: { assessment: Assessment; locations: Lookup['locations']; substanceNames?: Record<string, string> }) {
  const hitLimit = assessment.reasons?.includes('ITERATION_LIMIT_REACHED');
  const evidencePending = assessment.outcome === 'SEARCH_EXHAUSTED' && assessment.assessmentStatus === 'INCOMPLETE' && !!assessment.selection;
  const otherReasons = assessment.reasons?.filter(reason => reason !== 'SEARCH_SPACE_EXHAUSTED' && reason !== 'ITERATION_LIMIT_REACHED') ?? [];
  return <div className="f14-assessment"><dl className="export-facts"><div><dt>Search</dt><dd>{evidencePending ? 'Candidate found' : label(assessment.outcome)}</dd></div><div><dt>Assessment</dt><dd>{assessment.assessmentStatus}</dd></div><div><dt>Selected input</dt><dd>{assessment.selection?.selectedMassKg ?? '—'} kg</dd></div><div><dt>Predicted output</dt><dd>{assessment.predictedOutputKg ?? '—'} kg</dd></div></dl>
    {hitLimit && <p className="notice">The search stopped at its iteration limit. This does not prove that no feasible plan exists.</p>}
    {evidencePending && !hitLimit && <p className="notice">Enough stock was selected, but the assessment is incomplete. Review the missing evidence below.</p>}
    {assessment.outcome === 'SEARCH_EXHAUSTED' && !hitLimit && !evidencePending && <p className="notice">No checked selection passed every requirement. Review the findings below.</p>}
    {otherReasons.length > 0 && <p className="muted">Search reasons: {otherReasons.map(label).join(', ')}</p>}
    {assessment.selection && <><h3>Selected stock</h3><p className="muted">{label(assessment.selection.mode)}</p><div className="table-scroll"><table><thead><tr><th>Lot</th><th>Location</th><th>Input</th></tr></thead><tbody>{assessment.selection.allocations.map((item, index) => <tr key={`${item.lotId}-${item.locationId}-${index}`}><td>{item.lotCode}</td><td>{locations.find(location => location.id === item.locationId)?.name ?? 'Location unavailable'}</td><td>{item.quantityKg} kg</td></tr>)}</tbody></table></div></>}
    <h3>Checks and evidence</h3>{assessment.assessmentFindings?.length ? <div className="table-scroll"><table><thead><tr><th>Area</th><th>Check</th><th>Result</th><th>Actual</th><th>Limit</th><th>Next step</th></tr></thead><tbody>{assessment.assessmentFindings.map((finding, index) => <tr key={`${finding.code}-${index}`}><td>{findingArea(finding.code)}</td><td>{label(finding.code)}{finding.code.startsWith('RESIDUE_') && finding.reference && substanceNames[finding.reference] && ` · ${substanceNames[finding.reference]}`}</td><td>{finding.status}</td><td>{finding.actual ?? '—'} {finding.unit ?? ''}</td><td>{displayFindingLimit(finding)} {finding.unit ?? ''}</td><td>{findingAction(finding.code, finding.status)}</td></tr>)}</tbody></table></div> : <p>No findings returned.</p>}
    {assessment.evidence != null && <details><summary>Evidence versions and calculation details</summary><pre className="f14-json">{JSON.stringify(assessment.evidence, null, 2)}</pre></details>}
  </div>;
}

type DecisionAction = 'approve' | 'reject' | 'revoke';
type Decision = {
  proposalId: string;
  status: 'PENDING' | 'APPROVED' | 'REJECTED' | 'REVOKED';
  decisionVersion: number;
  recordedAt: string | null;
  reason: string | null;
  activeApproval: boolean;
  reservation: { status: string; reservedKg: string; releasedKg: string; consumedKg: string; remainingKg: string };
};

function ProposalDecision({ planId, containerId, proposal, userId, canApprove, proposalLoading, reload, onDecisionChange }: {
  planId: string; containerId: string; proposal: Proposal; userId: string; canApprove: boolean; proposalLoading: boolean; reload: number; onDecisionChange: () => void;
}) {
  const route = `/api/blend-plans/${planId}`;
  const key = `grainledger.f15.${userId}.${planId}.decision`;
  const [pending, setPending] = useState(() => {
    const request = readPending(key);
    return request && ['approve', 'reject', 'revoke'].some(action => request.route === `${route}/${action}`) ? request : null;
  });
  const [decision, setDecision] = useState<Decision | null>(null);
  const [containerStatus, setContainerStatus] = useState('');
  const [action, setAction] = useState<DecisionAction | null>(null);
  const [reason, setReason] = useState('');
  const [readError, setReadError] = useState('');
  const [writeError, setWriteError] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    const options = { signal: controller.signal };
    async function read() {
      setReadError('');
      setDecision(null);
      setContainerStatus('');
      setAction(null);
      setReason('');
      try {
        const value = await api<Decision>(`${route}/decision`, options);
        if (controller.signal.aborted) return;
        if (!value || value.proposalId !== proposal.id) throw new Error('The decision does not match this proposal. Refresh and try again.');
        setDecision(value);
        if (canApprove && value.status === 'APPROVED' && value.activeApproval) {
          const container = await api<{ status: string }>(`/api/export-containers/${containerId}`, options);
          if (!controller.signal.aborted) setContainerStatus(container.status);
        }
      } catch (cause) {
        if (controller.signal.aborted) return;
        setReadError(cause instanceof ApiError && cause.code === 'DECISION_SCHEMA_NOT_READY'
          ? 'Decision and reservation details are unavailable until the database update is deployed.'
          : errorMessage(cause));
      }
    }
    void read();
    return () => controller.abort();
  }, [route, proposal.id, containerId, canApprove, reload]);

  function refreshDecision() {
    setDecision(null);
    setContainerStatus('');
    onDecisionChange();
  }

  async function submit() {
    if (!canApprove || busy) return;
    if (!pending && (!decision || !action || (action === 'approve' ? proposalLoading || proposal.freshness !== 'CURRENT' : !reason.trim()))) return;
    const request = pending ?? {
      route: `${route}/${action}`,
      body: JSON.stringify({
        operationId: crypto.randomUUID(),
        proposalId: decision!.proposalId,
        expectedDecisionVersion: decision!.decisionVersion,
        ...(action !== 'approve' && { reason: reason.trim() }),
      }),
    };
    setBusy(true);
    setWriteError('');
    try {
      sessionStorage.setItem(key, JSON.stringify(request));
      const result = await api<Decision>(request.route, { method: 'POST', body: request.body });
      if (result?.proposalId !== proposal.id) throw new Error('The decision response could not be confirmed.');
      sessionStorage.removeItem(key);
      setPending(null);
      setAction(null);
      setReason('');
      refreshDecision();
    } catch (cause) {
      if (uncertain(cause)) {
        setPending(request);
        refreshDecision();
      } else {
        sessionStorage.removeItem(key);
        setPending(null);
        setAction(null);
        setReason('');
        setWriteError(errorMessage(cause));
        refreshDecision();
      }
    } finally { setBusy(false); }
  }

  const reservation = decision?.reservation;
  const canRevoke = decision?.status === 'APPROVED' && decision.activeApproval &&
    containerStatus === 'PLANNED' && /^0(?:\\.0+)?$/.test(decision.reservation.consumedKg);
  const actions: Record<DecisionAction, string> = {
    approve: 'Approve and reserve stock',
    reject: 'Reject proposal',
    revoke: 'Revoke approval and release stock',
  };
  return <div className="f14-assessment">
    <div className="section-toolbar"><h3>Proposal decision</h3>{readError && <button className="secondary" disabled={busy} onClick={refreshDecision}>Reload decision</button>}</div>
    <Notice error={readError} />
    <Notice error={writeError} />
    {!decision && !readError && <p role="status">Loading decision…</p>}
    {decision && reservation && <>
      <dl className="export-facts">
        <div><dt>Decision</dt><dd>{decision.status === 'PENDING' ? 'Pending review' : label(decision.status)}</dd></div>
        <div><dt>Reservation</dt><dd>{label(reservation.status)}</dd></div>
        <div><dt>Reserved</dt><dd>{reservation.reservedKg} kg</dd></div>
        <div><dt>Still held</dt><dd>{reservation.remainingKg} kg</dd></div>
        <div><dt>Released</dt><dd>{reservation.releasedKg} kg</dd></div>
        <div><dt>Consumed</dt><dd>{reservation.consumedKg} kg</dd></div>
      </dl>
      {decision.recordedAt && <p className="muted">Decided {new Date(decision.recordedAt).toLocaleString()}</p>}
      {decision.reason && <p><strong>Reason</strong> {decision.reason}</p>}
      {canApprove && decision.status === 'PENDING' && proposal.freshness === 'STALE' && <p className="notice">The proposal is out of date. Prepare a new proposal before approval.</p>}
      {canApprove && decision.status === 'APPROVED' && containerStatus && !canRevoke && <p className="notice">Revocation is unavailable once container execution begins.</p>}
      {canApprove && !pending && !action && <div className="f14-actions">
        {decision.status === 'PENDING' && <>
          {!proposalLoading && proposal.freshness === 'CURRENT' && <button onClick={() => setAction('approve')}>Approve proposal</button>}
          <button className="secondary" onClick={() => setAction('reject')}>Reject proposal</button>
        </>}
        {canRevoke && <button className="secondary" onClick={() => setAction('revoke')}>Revoke approval</button>}
      </div>}
    </>}
    {canApprove && pending && <div className="notice pending" role="status">
      The decision outcome is uncertain. <button disabled={busy} onClick={() => void submit()}>Retry same decision</button>
    </div>}
    {canApprove && !pending && action && decision && <form className="f14-actions" onSubmit={event => { event.preventDefault(); void submit(); }}>
      {action === 'approve' ? <p>The server will recheck this proposal and reserve its selected stock.</p>
        : <label>Reason<textarea required maxLength={1000} value={reason} disabled={busy} onChange={event => setReason(event.target.value)} /></label>}
      <button type="submit" disabled={busy || (action === 'approve' ? proposalLoading || proposal.freshness !== 'CURRENT' : !reason.trim())}>{actions[action]}</button>
      <button type="button" className="secondary" disabled={busy} onClick={() => { setAction(null); setReason(''); }}>Cancel</button>
    </form>}
  </div>;
}


export function BlendPlanWorkspace({ planId, userId, canPropose, canApprove = false, canReadLedger, onNavigate }: { planId: string; userId: string; canPropose: boolean; canApprove?: boolean; canReadLedger: boolean; onNavigate: (path: string) => void }) {
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
  const [objective, setObjective] = useState('');
  const [moistureAttributeId, setMoistureAttributeId] = useState('');
  const [testRatio, setTestRatio] = useState<string | null>(null);
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
    const controller = new AbortController(); setError(''); setProposalLoading(true);
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
  async function runPreview(firstQuantityKg?: string) {
    if (!candidates || !facilityId || busy) return;
    const adjust = firstQuantityKg !== undefined;
    const adjustment = adjust && preview?.previewReference
      ? { previewReference: preview.previewReference, firstQuantityKg } : undefined;
    if (adjust && (!adjustment || previewExpired)) return;
    setBusy(true); setPreviewError('');
    if (!adjust) { setPreview(null); setTestRatio(null); }
    try {
      const result = await api<Preview>(`/api/blend-plans/${planId}/preview`, { method: 'POST',
        body: JSON.stringify({ facilityId, scopeRevision: candidates.scopeRevision,
          objective: objective || undefined, adjustment,
          moistureAttributeId: objective === 'MIN_MOISTURE' ? moistureAttributeId : undefined }) });
      if (adjust && !result.canPropose) {
        setPreviewError(`The adjusted ratio did not pass: ${result.assessmentFindings.filter(f => f.status !== 'PASS').map(f => label(f.code)).join(', ')}.`);
      } else {
        setPreview(result); setTestRatio(null); setPreviewExpired(false);
      }
    }
    catch (cause) { setPreviewError(errorMessage(cause)); if (cause instanceof ApiError && cause.status === 409) refreshCandidates(); }
    finally { setBusy(false); }
  }
  async function propose() {
    if (busy || !pendingPropose && (!preview?.canPropose || previewExpired || testRatio !== null)) return;
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
  const percentageAttributes = (Array.isArray(snapshot.limits) ? snapshot.limits : [])
    .filter((row): row is { attributeId: string; attributeName?: string; attributeCode?: string; unit: string } =>
      !!row && typeof row === 'object' && typeof row.attributeId === 'string' && row.unit === '%');
  const attributeName = (id: string) => {
    const row = percentageAttributes.find(attribute => attribute.attributeId === id);
    return row?.attributeName ?? row?.attributeCode ?? 'Quality';
  };
  const active = plan?.status === 'DRAFT' && plan.demandCurrent && !plan.hasProposal && !proposal && !proposalError && !proposalLoading;
  return <>
    <button className="text-button" onClick={() => onNavigate(plan ? `/export/containers/${plan.containerId}` : '/export/orders')}>← Back to container</button>
    <div className="page-heading section-toolbar"><div><span className="eyebrow">BLEND PLANNING</span><h1>Plan {plan ? `v${plan.versionNo}` : ''}</h1><p className="muted">A saved proposal records an assessment. Stock is reserved later during approval.</p></div><button className="secondary" onClick={() => setReload(n => n + 1)}>Refresh</button></div>
    <Notice error={error} />
    {!plan && !error && <p role="status">Loading plan…</p>}
    {plan && <><section className="panel export-section"><div className="section-toolbar"><h2>Demand snapshot</h2>{canPropose && plan.canCancel && !pendingCancel && !pendingPropose && <button className="secondary" disabled={busy} onClick={() => void cancel()}>Cancel draft</button>}</div>
      <Notice error={cancelError} />{pendingCancel && <p className="notice error">Cancellation outcome is uncertain. <button disabled={busy} onClick={() => void cancel()}>Retry same cancellation</button></p>}
      <dl className="export-facts"><div><dt>Status</dt><dd>{plan.hasProposal || proposal ? 'Proposal saved' : label(plan.status)}</dd></div><div><dt>Target</dt><dd>{plan.targetMassKg} kg</dd></div><div><dt>Product</dt><dd>{label(plan.targetProductIdentity || 'Unknown')}</dd></div><div><dt>Demand</dt><dd>{(plan.demandChanged ?? !plan.demandCurrent) ? 'Changed since draft' : 'Current'}</dd></div></dl>
      <p className="muted">Market: {marketName || 'Unavailable'} · Commodity: {commodityName || 'Unavailable'} · Shipping {String(snapshot.shippingFrom ?? '—')} to {String(snapshot.shippingTo ?? '—')}.</p>
      {plan.targetProductIdentity && !['HEAD_RICE', 'BROKEN_RICE', 'BLENDED_RICE', 'BAGGED_RICE'].includes(plan.targetProductIdentity) && <p className="notice pending" role="status">This product cannot complete the current F14 quality check. Ledger accepts confirmed planning-quality observations only for finished rice lots. To prepare an assessable proposal, first process stock into finished rice and use an order for the corresponding commodity.</p>}
      {Object.keys(snapshot).length === 0 && <p className="notice error" role="alert">This draft's demand snapshot could not be read. Open the container and create a new draft.</p>}
      {(plan.demandChanged ?? !plan.demandCurrent) && <p className="notice">Order, specification or container target changed. <button className="text-button" onClick={() => onNavigate(`/export/containers/${plan.containerId}`)}>Open container to create a new draft</button></p>}
      {Array.isArray(snapshot.limits) && <details><summary>Buyer quality specification</summary><div className="table-scroll"><table><thead><tr><th>Attribute</th><th>Minimum</th><th>Maximum</th><th>Unit</th></tr></thead><tbody>{(snapshot.limits as Record<string, unknown>[]).map((row, index) => <tr key={index}><td>{String(row.attributeName ?? row.attributeCode ?? '—')}</td><td>{String(row.minValue ?? '—')}</td><td>{String(row.maxValue ?? '—')}</td><td>{String(row.unit ?? '—')}</td></tr>)}</tbody></table></div></details>}
    </section>
    {proposalError && <p className="notice error" role="alert">Proposal status could not be checked: {proposalError}</p>}
    {pendingPropose && <section className="panel export-section"><h2>Proposal request</h2><Notice error={previewError} /><p className="notice error">Proposal outcome is uncertain. <button disabled={busy} onClick={() => void propose()}>Retry same proposal</button></p></section>}
    {proposal && <section className="panel export-section"><h2>Saved proposal</h2><p>Recorded {new Date(proposal.proposedAt).toLocaleString()} · Freshness: <strong>{proposal.freshness === 'NOT_RECHECKED' ? 'See decision' : proposal.freshness}</strong></p>{proposal.freshness === 'STALE' && <p className="notice error">Planning inputs changed: {(proposal.freshnessReasons ?? []).map(label).join(', ')}. <button className="text-button" onClick={() => onNavigate(`/export/containers/${plan.containerId}`)}>Open container to create a new draft</button></p>}<AssessmentView assessment={proposal.assessment} locations={locations} /><ProposalDecision key={`${userId}-${proposal.id}`} planId={planId} containerId={plan.containerId} proposal={proposal} userId={userId} canApprove={canApprove} proposalLoading={proposalLoading} reload={reload} onDecisionChange={() => { setHistoryPage(1); setReload(n => n + 1); }} /></section>}
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
    {active && facilityId && candidates && !candidateLoading && <section className="panel export-section"><div className="section-toolbar"><div><h2>Assessed preview</h2><p className="muted">The server selects candidate quantities and checks the complete plan.</p></div>{canPropose && <><label>Planning priority<select value={objective}
      disabled={busy || !!pendingCancel || !!pendingPropose} onChange={event => {
        setObjective(event.target.value); setPreview(null); setTestRatio(null);
      }}><option value="">No additional priority</option><option value="MIN_LOCATIONS">Fewest stock locations</option>
        <option value="MIN_LOTS">Fewest lots</option><option value="RETAIN_HEAD_RICE">Retain head rice</option>
        <option value="MIN_MOISTURE" disabled={percentageAttributes.length === 0}>Lowest moisture</option>
        <option value="MAX_RESIDUE_HEADROOM">Farthest from residue limits</option></select></label>
      {objective === 'MIN_MOISTURE' && <label>Moisture attribute<select value={moistureAttributeId} disabled={busy}
        onChange={event => { setMoistureAttributeId(event.target.value); setPreview(null); setTestRatio(null); }}>
        <option value="">Select the moisture attribute</option>
        {percentageAttributes.map(row => <option key={row.attributeId} value={row.attributeId}>{attributeName(row.attributeId)}</option>)}
      </select></label>}
      <button disabled={busy || !!pendingCancel || !!pendingPropose || (objective === 'MIN_MOISTURE' && !moistureAttributeId)} onClick={() => void runPreview()}>{busy ? 'Working…' : 'Calculate plan'}</button></>}</div>
      {!pendingPropose && <Notice error={previewError} />}
      {!canPropose && <p className="notice">Your role can inspect drafts but cannot calculate or propose a plan.</p>}
      {preview && <>{!!preview.inputResidueExclusions?.length && <div className="notice pending" role="status">
        <strong>Lots excluded by source evidence or input MRL checks</strong>
        <ul>{preview.inputResidueExclusions.map(row => <li key={`${row.lotId}:${row.substanceId}`}>
          {row.lotCode} · {preview.substanceNames?.[row.substanceId] ?? 'Required substance'} · {label(row.code)}
          {row.code.startsWith('SOURCE_LAB_') && <p>{findingAction(row.code, row.status)}</p>}
          {row.upper !== null && ` · ${row.upper} ${row.unit} upper bound / ${row.mrl} ${row.unit} MRL`}
        </li>)}</ul>
      </div>}
      {preview.optimization?.positionsUsed != null && <p>Stock locations: <strong>{preview.optimization.positionsUsed}</strong>
        {preview.optimization.objective === 'MIN_LOCATIONS' && (preview.optimization.optimal ? ' · Optimal' : ' · Optimality not proven')}</p>}
        {preview.optimization?.objective === 'MIN_LOTS' && preview.optimization.lotsUsed != null &&
          <p>Lots used: <strong>{preview.optimization.lotsUsed}</strong> · {preview.optimization.optimal ? 'Optimal' : 'Optimality not proven'}</p>}
        {preview.optimization?.weightedMoisture != null && testRatio === null &&
          <p>Weighted moisture: <strong>{preview.optimization.weightedMoisture} %</strong> · {preview.optimization.optimal ? 'Optimal' : 'Optimality not proven'}</p>}
        {preview.residueHeadroom && testRatio === null && <p>Closest residue to its limit: <strong>
          {preview.substanceNames?.[preview.residueHeadroom.substanceId] ?? 'Required substance'} · {(preview.residueHeadroom.ratio * 100).toFixed(2)}% of limit</strong>
          {preview.optimization?.objective === 'MAX_RESIDUE_HEADROOM' && (preview.optimization.optimal ? ' · Optimal' : ' · Optimality not proven')}</p>}
        {preview.optimization?.headRiceUsedKg != null && testRatio === null && <p>Head rice used: <strong>{preview.optimization.headRiceUsedKg} kg</strong>
          {preview.optimization.optimal ? ' · Optimal' : ' · Optimality not proven'}</p>}
        {preview.previewReference && !previewExpired &&
          <BlendRatioCard preview={preview} value={testRatio} busy={busy} attributeName={attributeName}
            onChange={setTestRatio} onCheck={kg => void runPreview(kg)} />}
        {testRatio === null && <>
          <AssessmentView assessment={preview} locations={locations} substanceNames={preview.substanceNames} />
          <CalculationSteps preview={preview} />
        </>}
        {canPropose && preview.canPropose && preview.previewReference && <div className="f14-actions"><p className="muted">{previewExpired ? 'The preview has expired. Calculate the plan again.' : 'This preview reference expires after ten minutes. The server checks all inputs again when you propose.'}</p><button disabled={busy || previewExpired || testRatio !== null} onClick={() => void propose()}>Save proposal</button></div>}</>}
    </section>}
    <section className="panel export-section"><h2>History</h2>{history ? <>{history.items.length ? <ul className="f14-history">{history.items.map((event, index) => <li key={`${event.recordedAt}-${index}`}><strong>{label(event.action)}</strong> · {event.actorName} · {new Date(event.recordedAt).toLocaleString()}</li>)}</ul> : <p>No history yet.</p>}<Pager page={historyPage} count={history.totalCount} more={history.hasMore} onPage={setHistoryPage} /></> : <p role="status">Loading history…</p>}</section></>}
  </>;
}
