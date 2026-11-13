import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { Mass, positive } from './ledger-ui';

type Allocation = { lotId: string; lotCode: string; locationId: string; reservedKg: string; remainingKg: string; restricted: boolean; loadingPositionReady: boolean };
type Container = { id: string; code: string; orderId: string; targetKg: string; status: string; version: number; loadedKg: string; remainingKg: string;
  containerNumber: string | null; sealNumber: string | null; holds: { id: string; reason: string; recordedAt: string }[];
  approval: null | { proposalId: string; decisionVersion: number; mode: string; allocations: Allocation[] };
  loads: { id: string; loadedAt: string; lines: { lotId: string; lotCode: string; locationId: string; quantityKg: string }[] }[]; dossierAvailable: boolean };
type Readiness = { phase: string; checkedAt: string; assessmentStatus: string; findings: string[] };
type Pending = { path: string; body: string };
type Trace = { loadedKg: string; complete: boolean; sourceContributions: { sourceLotId: string; quantityKg: string; percentage?: string; status?: string; provenance?: { plotId: string; procurementCode: string } | null }[] };
const newId = () => crypto.randomUUID();
const findings: Record<string, string> = {
  ACTIVE_APPROVAL_REQUIRED: 'A Customer Manager must approve a current plan first.',
  BLEND_OUTPUT_HANDOFF_REQUIRED: 'This plan needs actual blend execution and reserved finished outputs from the blend workflow.',
  POSITION_OR_SOURCE_HANDOFF_REQUIRED: 'An approved position needs stock/source or pool-reservation reconciliation before loading.',
  CONTAINER_HOLD: 'This container has an active HOLD.', LOT_RESTRICTED: 'An approved lot has a current restriction.',
  APPROVAL_EVIDENCE_CHANGED: 'Evidence or policy changed after approval. Review the approval before continuing.',
  SEAL_METADATA_MISSING: 'Record the physical container number and seal before closing.',
  EXACT_TARGET_NOT_LOADED: 'The committed loading must equal the target before closing.',
  TARGET_ALREADY_LOADED: 'The target is loaded. Review the manifest and closure checks.',
  CONTAINER_CLOSED: 'The container is closed. Its manifest is read-only.',
};
function saved(key: string): Pending | null {
  try { const value = JSON.parse(sessionStorage.getItem(key) ?? 'null') as Pending | null; return value && typeof value.path === 'string' && typeof value.body === 'string' ? value : null; }
  catch { return null; }
}

export function ContainerExecution({ id, userId, canWrite, canHold, onNavigate }: {
  id: string; userId: string; canWrite: boolean; canHold: boolean; onNavigate: (path: string) => void;
}) {
  const route = `/api/export-containers/${id}/execution`, key = `grainledger.container.execution.${userId}.${id}`;
  const [detail, setDetail] = useState<Container | null>(null), [readiness, setReadiness] = useState<Readiness | null>(null);
  const [reload, setReload] = useState(0), [busy, setBusy] = useState(false), [error, setError] = useState('');
  const [number, setNumber] = useState(''), [seal, setSeal] = useState(''), [reason, setReason] = useState('');
  const [selection, setSelection] = useState(''), [kg, setKg] = useState(''), [confirmation, setConfirmation] = useState(false);
  const [pending, setPending] = useState<Pending | null>(() => saved(key));
  const [history, setHistory] = useState<{ items: { id: string; kind: string; recorded_at: string }[]; page: number; hasMore: boolean } | null>(null);
  const [historyPage, setHistoryPage] = useState(1);
  const [trace, setTrace] = useState<Trace | null>(null), [dossier, setDossier] = useState<unknown>(null);
  useEffect(() => {
    const controller = new AbortController(); setError(''); setDetail(null); setReadiness(null); setTrace(null); setDossier(null); setHistory(null); setHistoryPage(1);
    void api<{ container: Container; readiness: Readiness }>(route, { signal: controller.signal }).then(result => {
      setDetail(result.container); setReadiness(result.readiness); setNumber(result.container.containerNumber ?? ''); setSeal(result.container.sealNumber ?? '');
    }).catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); });
    return () => controller.abort();
  }, [id, userId, canWrite, canHold, reload]);
  async function mutate(action: string, fields: object) {
    if (!detail || busy) return; setBusy(true); setError('');
    const request = pending ?? { path: `${route}/${action}`, body: JSON.stringify({ operationId: newId(), version: detail.version, ...fields }) };
    sessionStorage.setItem(key, JSON.stringify(request)); setPending(request);
    try {
      const receipt = await api<Container>(request.path, { method: 'POST', body: request.body });
      if (!receipt || receipt.id !== id || !Number.isInteger(receipt.version) || typeof receipt.loadedKg !== 'string') throw new Error('The operation result is uncertain. Retry the same operation.');
      sessionStorage.removeItem(key); setPending(null); setConfirmation(false); setKg(''); setSelection(''); setReason(''); setReload(n => n + 1);
    } catch (cause) {
      if (cause instanceof ApiError && cause.status < 500 && cause.status !== 408) { sessionStorage.removeItem(key); setPending(null); }
      setError(errorMessage(cause));
    } finally { setBusy(false); }
  }
  async function inspect(action: 'trace' | 'dossier') {
    setBusy(true); setError(''); setTrace(null); setDossier(null);
    try {
      if (action === 'trace') setTrace(await api<Trace>(`${route}/trace`, { method: 'POST', body: '{}' }));
      else setDossier(await api<unknown>(`${route}/dossier`));
    } catch (cause) { setError(errorMessage(cause)); }
    finally { setBusy(false); }
  }
  async function readHistory(page: number) {
    setBusy(true); setError('');
    try { const next = await api<NonNullable<typeof history>>(`${route}/history?page=${page}&pageSize=20`); setHistory(next); setHistoryPage(page); }
    catch (cause) { setError(errorMessage(cause)); }
    finally { setBusy(false); }
  }
  const allocations = detail?.approval?.allocations ?? [];
  const eligible = allocations.filter(item => positive(item.remainingKg));
  const selected = eligible.find(item => `${item.lotId}:${item.locationId}` === selection);
  const mayLoad = canWrite && detail?.status !== 'CLOSED' && readiness?.assessmentStatus === 'PASS' && readiness.phase === 'LOADING';
  function load(event: FormEvent) {
    event.preventDefault(); if (!selected || !detail?.approval || !confirmation || !mayLoad || pending) return;
    void mutate('loads', { proposalId: detail.approval.proposalId, decisionVersion: detail.approval.decisionVersion,
      loadedAt: new Date().toISOString(), lines: [{ lotId: selected.lotId, locationId: selected.locationId, quantityKg: kg }] });
  }
  return <section className="panel export-section"><h2>Loading and closure</h2>
    <p className="muted">Only committed loading counts toward this container. Planned allocations and preparation drafts do not.</p>
    {error && <div className="notice error" role="alert">{error}</div>}
    {pending && <div className="notice error" role="alert">A previous operation has an uncertain result. Retry the same operation before starting another. <button disabled={busy} onClick={() => void mutate('', {})}>Retry container operation</button></div>}
    {detail && <><dl className="export-facts"><div><dt>Actual loaded</dt><dd>{detail.loadedKg} kg</dd></div><div><dt>Remaining to target</dt><dd>{detail.remainingKg} kg</dd></div><div><dt>Execution status</dt><dd>{detail.status}</dd></div></dl>
      {detail.holds.map(hold => <div key={hold.id} className="notice error"><strong>Container HOLD</strong>: {hold.reason}</div>)}
      {readiness && <div className="notice"><strong>{readiness.phase === 'CLOSE' ? 'Closure' : 'Loading'} checks: {readiness.assessmentStatus}</strong><p className="muted">Checked {new Date(readiness.checkedAt).toLocaleString()}. Every commit checks again.</p>
        {readiness.findings.length > 0 && <ul>{readiness.findings.map(code => <li key={code}>{findings[code] ?? code.replaceAll('_', ' ')}</li>)}</ul>}</div>}
      <button className="secondary" disabled={busy || !!pending} onClick={() => setReload(n => n + 1)}>Refresh container checks</button>
      {canWrite && detail.status !== 'CLOSED' && <form onSubmit={event => { event.preventDefault(); if (!pending) void mutate('metadata', { containerNumber: number, sealNumber: seal }); }}>
        <fieldset disabled={busy || !!pending}><legend>Physical container and seal</legend><div className="form-columns"><label>Container number<input value={number} maxLength={80} onChange={event => setNumber(event.target.value)} /></label><label>Seal number<input value={seal} maxLength={80} onChange={event => setSeal(event.target.value)} /></label></div><button>Save container metadata</button></fieldset></form>}
      <h3>Approved reserved stock</h3>
      <p className="muted">{detail.approval ? `Selection: ${detail.approval.mode}. Reserved amounts below belong to this approval; they are separate from free stock.` : 'No active approved allocation.'}</p>
      <div className="table-scroll"><table aria-label="Approved reserved stock"><thead><tr><th>Lot</th><th>Reserved (kg)</th><th>Reservation remaining (kg)</th><th>Restriction</th></tr></thead><tbody>{allocations.map(item => <tr key={`${item.lotId}:${item.locationId}`}><td>{item.lotCode}</td><td>{item.reservedKg}</td><td>{item.remainingKg}</td><td>{item.restricted ? 'Blocked' : item.loadingPositionReady ? 'Current position' : 'Handoff needed'}</td></tr>)}</tbody></table></div>
      {canWrite && detail.status !== 'CLOSED' && positive(detail.remainingKg) && <form onSubmit={load}><fieldset disabled={busy || !!pending || !mayLoad}><legend>Record actual loading</legend>
        <label>Approved position<select value={selection} onChange={event => { setSelection(event.target.value); setConfirmation(false); }} required><option value="">Select reserved stock</option>{eligible.map(item => <option key={`${item.lotId}:${item.locationId}`} value={`${item.lotId}:${item.locationId}`}>{item.lotCode} · {item.remainingKg} kg reserved remaining</option>)}</select></label>
        <Mass value={kg} onChange={value => { setKg(value); setConfirmation(false); }} label="Actual loaded quantity (kg)" />
        <label><input type="checkbox" checked={confirmation} onChange={event => setConfirmation(event.target.checked)} />I confirm this is the actual loading to record.</label>
        <button disabled={!selected || !confirmation || !kg}>Commit actual loading</button></fieldset></form>}
      <h3>Committed manifest</h3>{detail.loads.length ? detail.loads.map(load => <div key={load.id}><p>{new Date(load.loadedAt).toLocaleString()}</p><ul>{load.lines.map(line => <li key={`${line.lotId}:${line.locationId}`}><button className="text-button" onClick={() => onNavigate(`/ledger/lots/${line.lotId}`)}>{line.lotCode}</button> · {line.quantityKg} kg</li>)}</ul></div>) : <p className="empty">No actual loading recorded.</p>}
      {canWrite && detail.status === 'LOADING' && <button disabled={busy || !!pending || readiness?.phase !== 'CLOSE' || readiness.assessmentStatus !== 'PASS'} onClick={() => { if (window.confirm('Close this container and freeze its manifest?')) void mutate('close', {}); }}>Close container</button>}
      {canHold && <form onSubmit={event => { event.preventDefault(); if (!pending) void mutate('holds', { reason }); }}><fieldset disabled={busy || !!pending}><legend>Place container HOLD</legend><label>HOLD reason<textarea required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label><button>Place container HOLD</button></fieldset></form>}
      {(canHold || canWrite) && <div className="admin-form-actions">{canHold && <button className="secondary" disabled={busy || !!pending} onClick={() => void inspect('trace')}>Trace container sources</button>}{detail.dossierAvailable && <button className="secondary" disabled={busy || !!pending} onClick={() => void inspect('dossier')}>Read closure dossier</button>}</div>}
      {trace && <section><h3>Actual container sources</h3>{!trace.complete && <p className="notice error">Source provenance is incomplete; review the findings.</p>}<div className="table-scroll"><table aria-label="Actual container sources"><thead><tr><th>Source</th><th>Actual kg</th><th>Share (%)</th><th>Plot / provenance</th></tr></thead><tbody>{trace.sourceContributions.map(source => <tr key={source.sourceLotId}><td>{source.sourceLotId}</td><td>{source.quantityKg}</td><td>{source.percentage ?? '—'}</td><td>{source.provenance?.plotId ?? source.status ?? 'Unavailable'}</td></tr>)}</tbody></table></div></section>}
      <section><h3>Container history</h3><button className="secondary" disabled={busy || !!pending} onClick={() => void readHistory(1)}>Read container history</button>{history && <><ul>{history.items.map(item => <li key={item.id}>{item.kind} · {new Date(item.recorded_at).toLocaleString()}</li>)}</ul><div className="form-footer"><span>Page {historyPage}</span><button className="secondary" disabled={busy || historyPage === 1} onClick={() => void readHistory(historyPage - 1)}>Previous history page</button><button className="secondary" disabled={busy || !history.hasMore} onClick={() => void readHistory(historyPage + 1)}>Next history page</button></div></>}</section>
      {dossier != null && <section><h3>Immutable closure dossier</h3><p className="muted">Historical decision and actual manifest; later HOLDs do not rewrite this snapshot.</p><pre className="table-scroll">{JSON.stringify(dossier, null, 2)}</pre><a className="text-button" download={`${detail.code}-dossier.json`} href={`data:application/json;charset=utf-8,${encodeURIComponent(JSON.stringify(dossier, null, 2))}`}>Download dossier JSON</a></section>}
    </>}
  </section>;
}
