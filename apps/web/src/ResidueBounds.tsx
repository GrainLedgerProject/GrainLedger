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
  id: string;
  lotId: string;
  companyId: string;
  snapshotAt: string;
  persistedAt: string;
  engineVersion: string;
  results: ResidueResult[];
};
type EvaluationSummary = Pick<Evaluation, 'id' | 'lotId' | 'snapshotAt' | 'persistedAt' | 'engineVersion'> & { resultCount?: number; unresolvedCount?: number };
type EvaluationHistory = { items: EvaluationSummary[] };

export function ResidueBounds({ lotId, allowed }: { lotId: string; allowed: boolean }) {
  const [substances, setSubstances] = useState<Substance[]>([]);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [history, setHistory] = useState<EvaluationSummary[]>([]);
  const [evaluation, setEvaluation] = useState<Evaluation | null>(null);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
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
    void Promise.all([
      loadSubstances(controller.signal),
      api<EvaluationHistory>(`/api/ledger/lots/${encodeURIComponent(lotId)}/residue-evaluations`, { signal: controller.signal }),
    ]).then(([catalog, snapshots]) => {
      if (!active || request !== requestId.current) return;
      setSubstances(catalog);
      setHistory(snapshots.items);
    }).catch(cause => { if (active && request === requestId.current) setError(errorMessage(cause)); })
      .finally(() => { if (active && request === requestId.current) setLoading(false); });
    return () => { active = false; controller.abort(); requestId.current++; };
  }, [allowed, lotId, reload]);

  async function loadEvaluation(evaluationId: string) {
    const request = ++requestId.current;
    setBusy(true);
    setError('');
    try {
      const snapshot = await api<Evaluation>(`/api/ledger/lots/${encodeURIComponent(lotId)}/residue-evaluations/${encodeURIComponent(evaluationId)}`);
      if (request === requestId.current) setEvaluation(snapshot);
    } catch (cause) {
      if (request === requestId.current) setError(errorMessage(cause));
    } finally {
      if (request === requestId.current) setBusy(false);
    }
  }

  async function calculate(event: FormEvent) {
    event.preventDefault();
    if (!selectedIds.length || !allowed) return;
    const request = ++requestId.current;
    setBusy(true);
    setError('');
    try {
      const snapshot = await api<Evaluation>(`/api/ledger/lots/${encodeURIComponent(lotId)}/residue-evaluations`, {
        method: 'POST', body: JSON.stringify({ id: crypto.randomUUID(), substanceIds: selectedIds }),
      });
      if (request !== requestId.current) return;
      setEvaluation(snapshot);
      const snapshots = await api<EvaluationHistory>(`/api/ledger/lots/${encodeURIComponent(lotId)}/residue-evaluations`);
      if (request === requestId.current) setHistory(snapshots.items);
    } catch (cause) {
      if (request === requestId.current) setError(errorMessage(cause));
    } finally {
      if (request === requestId.current) setBusy(false);
    }
  }

  function toggleSubstance(id: string) {
    setSelectedIds(current => current.includes(id) ? current.filter(value => value !== id) : [...current, id]);
  }

  return <section className="panel residue-panel" aria-labelledby="residue-title">
    <h3 id="residue-title">Lot residue bounds</h3>
    <p>Calculated intervals inherit confirmed evidence through committed processing and custody paths. This is not a VN report comparison or a market compliance decision.</p>
    {!allowed && <p className="notice pending">Your account cannot inspect derived residue evaluations.</p>}
    {allowed && <>
      {loading && <p role="status">Loading substances and evaluation history…</p>}
      {error && <div className="notice error" role="alert">{error} <button type="button" className="secondary" onClick={() => setReload(value => value + 1)}>Try again</button></div>}
      {!loading && <form onSubmit={calculate}>
        <fieldset disabled={busy}>
          <legend>Substances to calculate</legend>
          {substances.map(substance => <label className="checkbox-label" key={substance.id}><input type="checkbox" checked={selectedIds.includes(substance.id)} onChange={() => toggleSubstance(substance.id)} />{substance.code} · {substance.name}</label>)}
          {!substances.length && <p className="empty">No active substances are available.</p>}
        </fieldset>
        <button type="submit" disabled={busy || !selectedIds.length}>{busy ? 'Calculating…' : evaluation ? 'Recalculate bounds' : 'Calculate bounds'}</button>
        <span className="sr-only" aria-live="polite">{busy ? 'Residue calculation in progress.' : ''}</span>
      </form>}
      <section aria-labelledby="residue-history-title">
        <h4 id="residue-history-title">Immutable evaluation history</h4>
        {loading ? null : history.length ? <ul className="residue-history">{history.map(item => <li key={item.id}>
          <button type="button" className="secondary" disabled={busy} aria-current={evaluation?.id === item.id ? 'true' : undefined} onClick={() => void loadEvaluation(item.id)}>
            {new Date(item.snapshotAt).toLocaleString()} · engine {item.engineVersion === 'F12-exact-1' ? 'Residue bounds exact 1' : item.engineVersion}
          </button>
          <span>Persisted {new Date(item.persistedAt).toLocaleString()}</span>
        </li>)}</ul> : <p className="empty">No saved evaluations for this lot.</p>}
      </section>
      {evaluation && <EvaluationDetail key={evaluation.id} evaluation={evaluation} substances={substances} />}
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

function EvaluationDetail({ evaluation, substances }: { evaluation: Evaluation; substances: Substance[] }) {
  const label = (id: string) => {
    const substance = substances.find(item => item.id === id);
    return substance ? `${substance.code} · ${substance.name}` : id;
  };
  return <article className="residue-snapshot">
    <h4>Historical snapshot · {new Date(evaluation.snapshotAt).toLocaleString()}</h4>
    <dl><dt>Engine version</dt><dd>{evaluation.engineVersion === 'F12-exact-1' ? 'Residue bounds exact 1' : evaluation.engineVersion}</dd><dt>Persisted at</dt><dd>{new Date(evaluation.persistedAt).toLocaleString()}</dd></dl>
    <p className="notice pending">Snapshot records evidence visible at calculation time. Recalculate to inspect current evidence. No PASS or market-compliance status is implied.</p>
    {evaluation.results.map((result, index) => <section className="residue-result" key={`${result.substanceId}-${index}`}>
      <div className="residue-result-heading"><h5>{label(result.substanceId)}</h5><span className={`badge${result.status === 'UNRESOLVED' ? ' inactive' : ''}`}>{result.status}</span></div>
      {result.status === 'COMPLETE' ? <p className="residue-interval">[{result.lower ?? '—'}, {result.upper ?? '—'}] {result.unit || 'unit unavailable'}</p> : <p className="notice pending">No finite interval established.</p>}
      <dl><dt>Analytical basis</dt><dd>{result.basis || 'Not specified'}</dd></dl>
      {result.reasons.length > 0 && <><h6>Reasons</h6><ul>{result.reasons.map((reason, reasonIndex) => <li key={`${reason}-${reasonIndex}`}>{reason}</li>)}</ul></>}
      {result.provenance.length > 0 && <><h6>Evidence provenance</h6><ul>{result.provenance.map((source, sourceIndex) => <li key={`${source.kind}-${sourceIndex}`}>
        {source.kind === 'DIRECT' ? 'Direct confirmed evidence' : source.kind === 'GROUP_FALLBACK' ? 'Approved source-group fallback' : source.kind === 'INHERITED' ? 'Inherited through lot lineage' : source.kind}
        {source.sourceLotId && ` · source lot ${source.sourceLotId}`}{source.groupVersionId && ` · source-group version ${source.groupVersionId}`}{source.selectionId && ` · reviewed selection ${source.selectionId}`}{source.reportId && ` · report reference ${source.reportId}`}{source.resultId && ` · result reference ${source.resultId}`}
        {source.uncertaintyMode && ` · uncertainty ${source.uncertaintyMode}`}{source.relativeUncertaintyPercent !== undefined && source.relativeUncertaintyPercent !== null && ` · ${source.relativeUncertaintyPercent}%`}{source.absoluteUncertainty !== undefined && source.absoluteUncertainty !== null && ` · derived absolute uncertainty ${source.absoluteUncertainty}`}
      </li>)}</ul></>}
      {result.rules.length > 0 && <><h6>Applied rules and policy settings</h6><ul>{result.rules.map((rule, ruleIndex) => <li key={`${rule.id}-${ruleIndex}`}>{rule.policyOrigin ? `${rule.policyOrigin === 'SYSTEM_DEFAULT' ? 'System default' : 'Company override'} · ` : ''}{rule.ruleCode ? `${rule.ruleCode} · ` : `Rule ${rule.id} · `}version {rule.version}{rule.policySettingVersion !== undefined && rule.policySettingVersion !== null ? ` · setting v${rule.policySettingVersion}` : ''}{rule.policySettingId ? ` (${rule.policySettingId})` : ''} · {rule.kind}{rule.evidenceReference && ` · evidence ${rule.evidenceReference}`}</li>)}</ul></>}
      {result.dependencies.length > 0 && <details><summary>Lineage dependencies</summary><ul>{result.dependencies.map((dependency, dependencyIndex) => <li key={`${dependency.lotId}-${dependencyIndex}`}>Lot {dependency.lotId}{dependency.parentLotId && ` ← parent ${dependency.parentLotId}`}{dependency.operationId && ` · operation ${dependency.operationId}`}{dependency.eventId && ` · event ${dependency.eventId}`}{dependency.kind && ` · ${dependency.kind}`}{dependency.quantityKg && ` · ${dependency.quantityKg} kg`}</li>)}</ul></details>}
    </section>)}
  </article>;
}
