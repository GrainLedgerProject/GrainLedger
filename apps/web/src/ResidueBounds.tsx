import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, errorMessage } from './api';

type Substance = { id: string; code: string; name: string };
type SubstancePage = { items: Substance[]; page: number; pageSize: number; hasMore: boolean };
type Provenance = {
  kind: string;
  sourceLotId: string;
  reportId?: string | null;
  resultId?: string | null;
  groupVersionId?: string | null;
  selectionId?: string | null;
  ruleId?: string | null;
  uncertaintyMode?: string | null;
  relativeUncertaintyPercent?: string | null;
  absoluteUncertainty?: string | null;
  originalUnit?: string | null;
  canonicalUnit?: string | null;
  concentrationScale?: string | null;
};
type RuleReference = { id: string; version: number; kind: string; evidenceReference: string; policyOrigin?: string | null; policySettingId?: string | null; policySettingVersion?: number | null; ruleCode?: string | null };
type Dependency = {
  lotId: string;
  parentLotId?: string | null;
  eventId?: string | null;
  operationId?: string | null;
  kind: string;
  quantityKg?: string | null;
};
type ResidueResult = {
  substanceId: string;
  status: 'COMPLETE' | 'UNRESOLVED';
  lower: string | null;
  upper: string | null;
  unit: string | null;
  basis: string | null;
  reasons: string[];
  provenance: Provenance[];
  rules: RuleReference[];
  dependencies: Dependency[];
};
type Evaluation = {
  id?: string;
  lotId: string;
  companyId: string;
  snapshotAt: string;
  persistedAt?: string | null;
  engineVersion: string;
  results: ResidueResult[];
};
type EvaluationSummary = { id: string; lotId: string; snapshotAt: string; persistedAt: string; engineVersion: string; resultCount?: number; unresolvedCount?: number };
type EvaluationHistory = { items: EvaluationSummary[] };

export function ResidueBounds({ lotId, allowed }: { lotId: string; allowed: boolean }) {
  const [substances, setSubstances] = useState<Substance[]>([]);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [history, setHistory] = useState<EvaluationSummary[]>([]);
  const [evaluation, setEvaluation] = useState<Evaluation | null>(null);
  const [loading, setLoading] = useState(false);
  const [busyAction, setBusyAction] = useState<'history' | 'snapshot' | null>(null);
  const [error, setError] = useState('');
  const [evaluationKind, setEvaluationKind] = useState<'preview' | 'snapshot' | 'history'>('preview');
  const [reload, setReload] = useState(0);
  const requestId = useRef(0);

  useEffect(() => {
    if (!allowed) return;
    let active = true;
    const request = ++requestId.current;
    const controller = new AbortController();
    setLoading(true);
    setError('');
    setEvaluation(null);
    setEvaluationKind('preview');
    void Promise.all([
      loadSubstances(controller.signal),
      api<EvaluationHistory>(`/api/ledger/lots/${encodeURIComponent(lotId)}/residue-evaluations`, { signal: controller.signal }),
    ]).then(async ([catalog, snapshots]) => {
      if (!active || request !== requestId.current) return;
      const requested = catalog.slice(0, 200);
      setSubstances(catalog);
      setSelectedIds(requested.map(item => item.id));
      setHistory(snapshots.items);
      if (requested.length) {
        const query = new URLSearchParams({ substanceIds: requested.map(item => item.id).join(',') });
        const preview = await api<Evaluation>(`/api/ledger/lots/${encodeURIComponent(lotId)}/residue-preview?${query}`, { signal: controller.signal });
        if (active && request === requestId.current) setEvaluation(preview);
      }
    }).catch(cause => { if (active && request === requestId.current) setError(errorMessage(cause)); })
      .finally(() => { if (active && request === requestId.current) setLoading(false); });
    return () => { active = false; controller.abort(); requestId.current++; };
  }, [allowed, lotId, reload]);

  async function loadEvaluation(evaluationId: string) {
    const request = ++requestId.current;
    setBusyAction('history');
    setError('');
    try {
      const snapshot = await api<Evaluation>(`/api/ledger/lots/${encodeURIComponent(lotId)}/residue-evaluations/${encodeURIComponent(evaluationId)}`);
      if (request === requestId.current) { setEvaluation(snapshot); setEvaluationKind('history'); }
    } catch (cause) {
      if (request === requestId.current) setError(errorMessage(cause));
    } finally {
      if (request === requestId.current) setBusyAction(null);
    }
  }

  async function calculate(event: FormEvent) {
    event.preventDefault();
    if (!selectedIds.length || selectedIds.length > 200 || !allowed) return;
    const request = ++requestId.current;
    setBusyAction('snapshot');
    setError('');
    try {
      const snapshot = await api<Evaluation>(`/api/ledger/lots/${encodeURIComponent(lotId)}/residue-evaluations`, {
        method: 'POST', body: JSON.stringify({ id: crypto.randomUUID(), substanceIds: selectedIds }),
      });
      if (request !== requestId.current) return;
      setEvaluation(snapshot);
      setEvaluationKind('snapshot');
      const snapshots = await api<EvaluationHistory>(`/api/ledger/lots/${encodeURIComponent(lotId)}/residue-evaluations`);
      if (request === requestId.current) setHistory(snapshots.items);
    } catch (cause) {
      if (request === requestId.current) setError(errorMessage(cause));
    } finally {
      if (request === requestId.current) setBusyAction(null);
    }
  }

  function toggleSubstance(id: string) {
    setSelectedIds(current => current.includes(id) ? current.filter(value => value !== id) : [...current, id]);
  }

  return <section className="panel residue-panel" aria-labelledby="residue-title">
    <h3 id="residue-title">Lot residue bounds</h3>
    <p>Live preview uses current confirmed evidence and reviewed policies. It is not a saved snapshot, PASS, VN report comparison or market-compliance decision.</p>
    {!allowed && <p className="notice pending">Your account cannot inspect derived residue evaluations.</p>}
    {allowed && <>
      {loading && <p role="status">Loading current residue bounds…</p>}
      <button type="button" className="secondary" disabled={loading || busyAction !== null} onClick={() => setReload(value => value + 1)}>Refresh live preview</button>
      {error && <div className="notice error" role="alert">{error} <button type="button" className="secondary" onClick={() => setReload(value => value + 1)}>Try again</button></div>}
      {!loading && <form onSubmit={calculate}>
        <fieldset disabled={busyAction !== null}>
          <legend>Substances to include in audit snapshot</legend>
          {substances.map(substance => <label className="checkbox-label" key={substance.id}><input type="checkbox" checked={selectedIds.includes(substance.id)} onChange={() => toggleSubstance(substance.id)} />{substance.code} · {substance.name}</label>)}
          {!substances.length && <p className="empty">No active substances are available.</p>}
        </fieldset>
        <button type="submit" disabled={busyAction !== null || !selectedIds.length || selectedIds.length > 200}>{busyAction === 'history' ? 'Loading history…' : busyAction === 'snapshot' ? 'Saving snapshot…' : 'Lock snapshot for audit'}</button>
        <span className="sr-only" aria-live="polite">{busyAction === 'history' ? 'Loading historical snapshot.' : busyAction === 'snapshot' ? 'Saving residue snapshot.' : ''}</span>
      </form>}
      <section aria-labelledby="residue-history-title">
        <h4 id="residue-history-title">Immutable evaluation history</h4>
        {loading ? null : history.length ? <ul className="residue-history">{history.map(item => <li key={item.id}>
          <button type="button" className="secondary" disabled={busyAction !== null} aria-current={evaluation?.id === item.id ? 'true' : undefined} onClick={() => void loadEvaluation(item.id)}>
            {new Date(item.snapshotAt).toLocaleString()} · engine {item.engineVersion === 'F12-exact-1' ? 'Residue bounds exact 1' : item.engineVersion}
          </button>
          <span>Persisted {new Date(item.persistedAt).toLocaleString()}</span>
        </li>)}</ul> : <p className="empty">No saved evaluations for this lot.</p>}
      {substances.length > 200 && <p className="notice pending">Preview limited to first 200 active substances. Reduce selected substances to lock smaller audit snapshot.</p>}
      {selectedIds.length > 200 && <p className="notice error" role="alert">Select no more than 200 substances before locking a snapshot.</p>}
      </section>
      {evaluation && <EvaluationDetail key={evaluation.id ?? `${evaluation.snapshotAt}-${evaluationKind}`} evaluation={evaluation} substances={substances} kind={evaluationKind} />}
    </>}
  </section>;
}


async function loadSubstances(signal: AbortSignal): Promise<Substance[]> {
  const collected: Substance[] = [];
  for (let page = 1; page <= 100; page++) {
    const result = await api<SubstancePage>(`/api/active-substances?page=${page}&pageSize=100`, { signal });
    collected.push(...result.items);
    if (!result.hasMore) return collected;
  }
  throw new Error('Too many active substances to load.');
}

function EvaluationDetail({ evaluation, substances, kind }: { evaluation: Evaluation; substances: Substance[]; kind: 'preview' | 'snapshot' | 'history' }) {
  const label = (id: string) => {
    const substance = substances.find(item => item.id === id);
    return substance ? `${substance.code} · ${substance.name}` : id;
  };
  return <article className="residue-snapshot">
    <h4>{kind === 'preview' ? 'Live preview' : kind === 'history' ? 'Historical snapshot' : 'Saved snapshot'} · {new Date(evaluation.snapshotAt).toLocaleString()}</h4>
    <dl><dt>Engine version</dt><dd>{evaluation.engineVersion === 'F12-exact-1' ? 'Residue bounds exact 1' : evaluation.engineVersion}</dd>{evaluation.persistedAt && <><dt>Persisted at</dt><dd>{new Date(evaluation.persistedAt).toLocaleString()}</dd></>}</dl>
    <p className="notice pending">{kind === 'preview' ? 'Captured current policy and evidence; not saved to history. Refresh lot detail to recalculate.' : 'Snapshot records evidence visible at calculation time. No PASS or market-compliance status is implied.'}</p>
    {evaluation.results.map((result, index) => <section className="residue-result" key={`${result.substanceId}-${index}`}>
      <div className="residue-result-heading"><h5>{label(result.substanceId)}</h5><span className={`badge${result.status === 'UNRESOLVED' ? ' inactive' : ''}`}>{result.status}</span></div>
      {result.status === 'COMPLETE' ? <p className="residue-interval">[{result.lower ?? '—'}, {result.upper ?? '—'}] {result.unit || 'unit unavailable'}</p> : <p className="notice pending">No finite interval established.</p>}
      <dl><dt>Analytical basis</dt><dd>{result.basis || 'Not specified'}</dd></dl>
      {result.reasons.length > 0 && <><h6>Reasons</h6><ul>{result.reasons.map((reason, reasonIndex) => <li key={`${reason}-${reasonIndex}`}>{reason}</li>)}</ul></>}
      {result.provenance.length > 0 && <><h6>Evidence provenance</h6><ul>{result.provenance.map((source, sourceIndex) => <li key={`${source.kind}-${sourceIndex}`}>
        {source.kind === 'DIRECT' ? 'Direct confirmed evidence' : source.kind === 'GROUP_FALLBACK' ? 'Approved source-group fallback' : source.kind === 'INHERITED' ? 'Inherited through lot lineage' : source.kind}
        {source.sourceLotId && ` · source lot ${source.sourceLotId}`}{source.groupVersionId && ` · source-group version ${source.groupVersionId}`}{source.selectionId && ` · reviewed selection ${source.selectionId}`}{source.reportId && ` · report reference ${source.reportId}`}{source.resultId && ` · result reference ${source.resultId}`}
        {source.concentrationScale && source.concentrationScale !== '1' && source.originalUnit && source.canonicalUnit && ` · ${source.originalUnit} × ${source.concentrationScale} → ${source.canonicalUnit}`}
        {source.uncertaintyMode && ` · uncertainty ${source.uncertaintyMode}`}{source.relativeUncertaintyPercent !== undefined && source.relativeUncertaintyPercent !== null && ` · ${source.relativeUncertaintyPercent}%`}{source.absoluteUncertainty !== undefined && source.absoluteUncertainty !== null && ` · derived absolute uncertainty ${source.absoluteUncertainty}`}
      </li>)}</ul></>}
      {result.rules.length > 0 && <><h6>Applied rules and policy settings</h6><ul>{result.rules.map((rule, ruleIndex) => <li key={`${rule.id}-${ruleIndex}`}>{rule.policyOrigin ? `${rule.policyOrigin === 'SYSTEM_DEFAULT' ? 'System default' : 'Company override'} · ` : ''}{rule.ruleCode ? `${rule.ruleCode} · ` : `Rule ${rule.id} · `}version {rule.version}{rule.policySettingVersion !== undefined && rule.policySettingVersion !== null ? ` · setting v${rule.policySettingVersion}` : ''}{rule.policySettingId ? ` (${rule.policySettingId})` : ''} · {rule.kind}{rule.evidenceReference && ` · evidence ${rule.evidenceReference}`}</li>)}</ul></>}
      {result.dependencies.length > 0 && <details><summary>Lineage dependencies</summary><ul>{result.dependencies.map((dependency, dependencyIndex) => <li key={`${dependency.lotId}-${dependencyIndex}`}>Lot {dependency.lotId}{dependency.parentLotId && ` ← parent ${dependency.parentLotId}`}{dependency.operationId && ` · operation ${dependency.operationId}`}{dependency.eventId && ` · event ${dependency.eventId}`}{dependency.kind && ` · ${dependency.kind}`}{dependency.quantityKg && ` · ${dependency.quantityKg} kg`}</li>)}</ul></details>}
    </section>)}
  </article>;
}
