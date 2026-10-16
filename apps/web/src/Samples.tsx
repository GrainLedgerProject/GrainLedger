import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { newRequest, pendingRequest, submitCapture, wasRejected, type Envelope } from './captureWrite';
import { type Page } from './procurementTypes';

type Sample = { id: string; sourceLotId: string; code: string; sealCode: string; collectedBy: string; collectedAt: string; capturedAt: string; recordedAt: string; handoverCount: number };
type SampleSummary = Pick<Sample, 'id' | 'sourceLotId' | 'code' | 'sealCode' | 'collectedAt' | 'handoverCount'>;
type Handover = { id: string; sampleId: string; fromParty: string; toParty: string; handedOverAt: string; sealCondition: string; evidenceRef: string | null };
type SamplePayload = { entityId: string; sourceLotId: string; code: string; sealCode: string; collectedAt: string };
type HandoverPayload = { entityId: string; fromParty: string; toParty: string; handedOverAt: string; sealCondition: string; evidenceRef: string | null };
function localDateTime(value: string) { const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16); }
function sampleCode(collectedAt: string, entityId: string) {
  return `SMP-${collectedAt.slice(0, 10).replaceAll('-', '')}-${entityId.replaceAll('-', '').slice(0, 16).toUpperCase()}`;
}

export function SampleList({ sourceLotId, onOpen, onCreate, canCreate }: { sourceLotId?: string; onOpen: (id: string) => void; onCreate?: () => void; canCreate?: boolean }) {
  const [page, setPage] = useState(1); const [retry, setRetry] = useState(0);
  const [list, setList] = useState<Page<SampleSummary> | null>(null); const [loading, setLoading] = useState(true); const [error, setError] = useState('');
  useEffect(() => {
    let active = true; const controller = new AbortController(); setLoading(true); setList(null); setError('');
    const params = new URLSearchParams({ page: String(page), pageSize: '20' }); if (sourceLotId) params.set('sourceLotId', sourceLotId);
    void api<Page<SampleSummary>>(`/api/samples?${params}`, { signal: controller.signal })
      .then(value => { if (active) setList(value); }).catch(cause => { if (active) setError(errorMessage(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [sourceLotId, page, retry]);
  return <section className="panel capture-section"><div className="capture-section-heading"><div><span className="eyebrow">SAMPLE CUSTODY</span><h2>Samples</h2><p className="muted">Physical samples and their recorded handover count.</p></div>{canCreate && onCreate && <button onClick={onCreate}>+ Take sample</button>}</div>
    {loading && <p role="status">Loading samples…</p>}{error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(n => n + 1)}>Try again</button></div>}
    {list && (list.items.length ? <div className="table-scroll"><table><thead><tr><th>Sample</th><th>Seal</th><th>Collected</th><th>Handovers</th></tr></thead><tbody>{list.items.map(item => <tr key={item.id}><td><button className="text-button farmer-plot-link" onClick={() => onOpen(item.id)}>{item.code} ↗</button></td><td>{item.sealCode}</td><td>{new Date(item.collectedAt).toLocaleString()}</td><td>{item.handoverCount}</td></tr>)}</tbody></table></div> : <p className="empty">No samples found.</p>)}
    {list && <div className="farmer-pagination"><span>Page {list.page}</span><button className="secondary" disabled={page === 1 || loading} onClick={() => setPage(p => p - 1)}>Previous</button><button className="secondary" disabled={!list.hasMore || loading} onClick={() => setPage(p => p + 1)}>Next</button></div>}
  </section>;
}

export function SourceSamples({ sourceLotId, canCreate, userId, companyId, onOpen }: { sourceLotId: string; canCreate: boolean; userId: string; companyId: string; onOpen: (id: string) => void }) {
  const [creating, setCreating] = useState(false);
  return <><SampleList sourceLotId={sourceLotId} canCreate={canCreate} onCreate={() => setCreating(true)} onOpen={onOpen} />
    {creating && <SampleCreate sourceLotId={sourceLotId} userId={userId} companyId={companyId} onCancel={() => setCreating(false)} onCreated={onOpen} />}</>;
}

function SampleCreate({ sourceLotId, userId, companyId, onCancel, onCreated }: { sourceLotId: string; userId: string; companyId: string; onCancel: () => void; onCreated: (id: string) => void }) {
  const key = `grainledger:sample-create:${userId}:${companyId}:${sourceLotId}`;
  const [pending, setPending] = useState<Envelope<SamplePayload> | null>(() => {
    const value = pendingRequest<SamplePayload>(key); return value?.payload.sourceLotId === sourceLotId ? value : null;
  });
  const [entityId, setEntityId] = useState(pending?.payload.entityId ?? crypto.randomUUID());
  const [sealCode, setSealCode] = useState(pending?.payload.sealCode ?? '');
  const [collectedAt, setCollectedAt] = useState(pending ? localDateTime(pending.payload.collectedAt) : localDateTime(new Date().toISOString()));
  const code = pending?.payload.code ?? sampleCode(collectedAt, entityId);
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  async function save(event: FormEvent) {
    event.preventDefault(); if (busy) return;
    if (!pending && (!sealCode.trim() || sealCode.trim().length > 100 || !collectedAt || !Number.isFinite(new Date(collectedAt).getTime()) || new Date(collectedAt).getTime() > Date.now())) {
      setError('Enter the code on the physical seal and a collection time no later than now.'); return;
    }
    setBusy(true); setError('');
    const request = pending ?? newRequest<SamplePayload>({ entityId, sourceLotId, code, sealCode: sealCode.trim(), collectedAt: new Date(collectedAt).toISOString() });
    if (!pending) setPending(request);
    try { const result = await submitCapture<SamplePayload, Sample>(key, '/api/samples', request); onCreated(result.id); }
    catch (cause) {
      if (wasRejected(cause)) setPending(null);
      if (cause instanceof ApiError && cause.code === 'SAMPLE_CODE_EXISTS') {
        setEntityId(crypto.randomUUID());
        setError('The generated sample code was already used. A new code is ready; save again.');
      } else setError(errorMessage(cause));
    }
    finally { setBusy(false); }
  }
  return <div className="farmer-modal-backdrop"><section className="farmer-modal" role="dialog" aria-modal="true" aria-labelledby="sample-create-title" onKeyDown={e => { if (e.key === 'Escape' && !busy) onCancel(); }}><div className="farmer-modal-heading"><div><span className="eyebrow">SAMPLE CUSTODY</span><h2 id="sample-create-title">Take sample</h2></div><button className="farmer-modal-close" aria-label="Close sample form" type="button" disabled={busy} onClick={onCancel}>×</button></div><p className="muted">Record a physical sample. Handover can be added later.</p><form onSubmit={save}>
    <label>Sample code<input value={code} readOnly aria-describedby="sample-code-help" /></label><p id="sample-code-help" className="field-help">Generated automatically. Write or print this code on the sample bag.</p><label>Seal code<input value={sealCode} maxLength={100} onChange={e => setSealCode(e.target.value)} required disabled={busy || !!pending} /></label><p className="field-help">Enter the code printed on the physical seal.</p><label>Collected at<input type="datetime-local" value={collectedAt} onChange={e => setCollectedAt(e.target.value)} required disabled={busy || !!pending} /></label>
    {pending && !busy && <div className="notice pending">The outcome is uncertain. Retry sends the exact same sample request.</div>}{error && <div className="notice error" role="alert">{error}</div>}<div className="form-footer"><button className="secondary" type="button" onClick={onCancel} disabled={busy}>Cancel</button><button type="submit" disabled={busy}>{busy ? 'Saving…' : pending ? 'Retry save' : 'Save sample'}</button></div>
  </form></section></div>;
}

export function SampleDetail({ id, userId, companyId, canHandover, onBack }: { id: string; userId: string; companyId: string; canHandover: boolean; onBack: (sourceLotId: string | null) => void }) {
  const [sample, setSample] = useState<Sample | null>(null); const [error, setError] = useState(''); const [retry, setRetry] = useState(0);
  const [handovers, setHandovers] = useState<Page<Handover> | null>(null); const [handoverError, setHandoverError] = useState(''); const [handoverPage, setHandoverPage] = useState(1); const [handoverRetry, setHandoverRetry] = useState(0); const [creating, setCreating] = useState(false);
  useEffect(() => {
    let active = true; const controller = new AbortController(); setSample(null); setError('');
    void api<Sample>(`/api/samples/${encodeURIComponent(id)}`, { signal: controller.signal })
      .then(value => { if (active) setSample(value); }).catch(cause => { if (active) setError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, [id, retry]);
  useEffect(() => {
    let active = true; const controller = new AbortController(); setHandovers(null); setHandoverError('');
    void api<Page<Handover>>(`/api/samples/${encodeURIComponent(id)}/handovers?page=${handoverPage}&pageSize=20`, { signal: controller.signal })
      .then(value => { if (active) setHandovers(value); }).catch(cause => { if (active) setHandoverError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, [id, handoverPage, handoverRetry]);
  return <><div className="page-heading"><span className="eyebrow">SAMPLE CUSTODY</span><h1>Sample details</h1><p className="muted">A physical sample and its handover history; not a lab result.</p></div><button className="text-button" onClick={() => onBack(sample?.sourceLotId ?? null)}>← Back to source lot</button>
    {!sample && !error && <p role="status">Loading sample…</p>}{error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(n => n + 1)}>Try again</button></div>}
    {sample && <><section className="panel capture-detail"><h2>{sample.code}</h2><div className="capture-stat-grid"><div><span>Seal</span><strong>{sample.sealCode}</strong></div><div><span>Collected</span><strong>{new Date(sample.collectedAt).toLocaleString()}</strong></div><div><span>Handovers</span><strong>{sample.handoverCount}</strong></div></div></section>
      <section className="panel capture-section"><div className="capture-section-heading"><div><span className="eyebrow">CHAIN OF CUSTODY</span><h2>Handovers</h2></div>{canHandover && <button onClick={() => setCreating(true)}>+ Record handover</button>}</div>
        {handoverError && <div className="notice error" role="alert">{handoverError} <button className="text-button" onClick={() => setHandoverRetry(n => n + 1)}>Try again</button></div>}
        {handovers && (handovers.items.length ? <div className="table-scroll"><table><thead><tr><th>Time</th><th>From</th><th>To</th><th>Seal</th><th>Evidence</th></tr></thead><tbody>{handovers.items.map(item => <tr key={item.id}><td>{new Date(item.handedOverAt).toLocaleString()}</td><td>{item.fromParty}</td><td>{item.toParty}</td><td>{item.sealCondition}</td><td>{item.evidenceRef || '—'}</td></tr>)}</tbody></table></div> : <p className="empty">No handover recorded yet.</p>)}
        {handovers && <div className="farmer-pagination"><span>Page {handovers.page}</span><button className="secondary" disabled={handoverPage === 1} onClick={() => setHandoverPage(p => p - 1)}>Previous</button><button className="secondary" disabled={!handovers.hasMore} onClick={() => setHandoverPage(p => p + 1)}>Next</button></div>}
      </section>{creating && <HandoverCreate sample={sample} userId={userId} companyId={companyId} onCancel={() => setCreating(false)} onCreated={() => { setCreating(false); setHandoverPage(1); setHandoverRetry(n => n + 1); setRetry(n => n + 1); }} />}</>}
  </>;
}

function HandoverCreate({ sample, userId, companyId, onCancel, onCreated }: { sample: Sample; userId: string; companyId: string; onCancel: () => void; onCreated: () => void }) {
  const key = `grainledger:handover-create:${userId}:${companyId}:${sample.id}`;
  const [pending, setPending] = useState<Envelope<HandoverPayload> | null>(() => pendingRequest<HandoverPayload>(key));
  const [fromParty, setFromParty] = useState(pending?.payload.fromParty ?? ''); const [toParty, setToParty] = useState(pending?.payload.toParty ?? '');
  const [handedOverAt, setHandedOverAt] = useState(pending ? localDateTime(pending.payload.handedOverAt) : localDateTime(new Date().toISOString()));
  const [sealCondition, setSealCondition] = useState(pending?.payload.sealCondition ?? ''); const [evidenceRef, setEvidenceRef] = useState(pending?.payload.evidenceRef ?? '');
  const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  async function save(event: FormEvent) {
    event.preventDefault(); if (busy) return;
    if (!pending && (!fromParty.trim() || !toParty.trim() || !sealCondition.trim() || !handedOverAt || new Date(handedOverAt).getTime() < new Date(sample.collectedAt).getTime() || new Date(handedOverAt).getTime() > Date.now())) { setError('Handover time must be after collection and no later than now. Complete both parties and the seal condition.'); return; }
    setBusy(true); setError('');
    const request = pending ?? newRequest<HandoverPayload>({ entityId: crypto.randomUUID(), fromParty: fromParty.trim(), toParty: toParty.trim(), handedOverAt: new Date(handedOverAt).toISOString(), sealCondition: sealCondition.trim(), evidenceRef: evidenceRef.trim() || null });
    if (!pending) setPending(request);
    try { await submitCapture<HandoverPayload, Handover>(key, `/api/samples/${encodeURIComponent(sample.id)}/handovers`, request); onCreated(); }
    catch (cause) { if (wasRejected(cause)) setPending(null); setError(errorMessage(cause)); }
    finally { setBusy(false); }
  }
  return <div className="farmer-modal-backdrop"><section className="farmer-modal" role="dialog" aria-modal="true" aria-labelledby="handover-title" onKeyDown={e => { if (e.key === 'Escape' && !busy) onCancel(); }}><div className="farmer-modal-heading"><div><span className="eyebrow">CHAIN OF CUSTODY</span><h2 id="handover-title">Record handover</h2></div><button className="farmer-modal-close" type="button" aria-label="Close handover form" disabled={busy} onClick={onCancel}>×</button></div><p className="muted">Append a handover for sample {sample.code}.</p><form onSubmit={save}>
    <div className="form-columns"><label>From party<input value={fromParty} onChange={e => setFromParty(e.target.value)} required disabled={busy || !!pending} /></label><label>To party<input value={toParty} onChange={e => setToParty(e.target.value)} required disabled={busy || !!pending} /></label></div>
    <label>Handed over at<input type="datetime-local" value={handedOverAt} onChange={e => setHandedOverAt(e.target.value)} required disabled={busy || !!pending} /></label><label>Seal condition<input value={sealCondition} onChange={e => setSealCondition(e.target.value)} required disabled={busy || !!pending} placeholder="Intact" /></label><label>Evidence reference <span className="optional">(optional)</span><input value={evidenceRef} onChange={e => setEvidenceRef(e.target.value)} disabled={busy || !!pending} /></label>
    {pending && !busy && <div className="notice pending">The outcome is uncertain. Retry sends the same handover request.</div>}{error && <div className="notice error" role="alert">{error}</div>}<div className="form-footer"><button className="secondary" type="button" onClick={onCancel} disabled={busy}>Cancel</button><button type="submit" disabled={busy}>{busy ? 'Saving…' : pending ? 'Retry save' : 'Save handover'}</button></div>
  </form></section></div>;
}
