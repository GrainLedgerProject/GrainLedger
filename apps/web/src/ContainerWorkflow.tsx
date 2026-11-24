import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage, type User } from './api';
import { Evidence, Mass, positive, type Lookup } from './ledger-ui';
import { readWorkflowPending, storeWorkflowPending, type WorkflowPending } from './container-workflow-pending';

type Output = { lotId: string; lotCode: string; locationId: string; locationName: string; quantityKg: string; remainingKg: string; poolRevision?: number | null };
type Proposal = { id: string; planId: string; version: number; context: { facilityId: string; mode: string; inputs: { lotCode: string; quantityKg: string }[]; outputs: Output[]; remainingProductionKg: string } };
type Handoff = { id: string; proposalId: string; facilityId: string; kind: string; state: string; version: number; content: { lotId: string; quantityKg: string; fromLocationId: string; toLocationId: string } };
type Workflow = { containerId: string; ownerId: string; status: string; version: number; proposals: Proposal[];
  reviews: { id: string; proposalId: string; scope: string; decision: string; reason: string; recordedAt: string }[];
  handoffs: Handoff[]; documents: { id: string; documentType: string; version: number; origin?: 'GENERATED' | 'UPLOADED' | 'UNKNOWN'; filename: string; sha256: string }[];
  supplements: { id: string; evidenceId: string; reason: string; recordedAt: string }[];
  holds: { id: string; reason: string }[]; holdResponses: { id: string; action: string; reason: string; recordedAt: string }[] };
const localTime = () => { const now = new Date(); return new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 16); };

export function ContainerWorkflow({ id, userId, canReview = false, canWrite = false, operator = false, manager = false, noticeId, onChanged }: {
  id: string; userId: string; canReview?: boolean; canWrite?: boolean; operator?: boolean; manager?: boolean; noticeId?: string; onChanged?: () => void;
}) {
  const route = `/api/export-containers/${id}/workflow`, key = `grainledger.container-workflow.${userId}.${id}`;
  const [data, setData] = useState<Workflow>(), [look, setLook] = useState<Lookup>(), [reload, setReload] = useState(0), [error, setError] = useState(''), [busy, setBusy] = useState(false);
  const [pending, setPending] = useState<WorkflowPending | null>(null), [restored, setRestored] = useState(false), [proposal, setProposal] = useState(''), [scope, setScope] = useState('OUTPUT_ONLY'), [reason, setReason] = useState('');
  const [source, setSource] = useState(''), [kind, setKind] = useState('TRANSFER'), [destination, setDestination] = useState(''), [kg, setKg] = useState(''), [outputKg, setOutputKg] = useState(''), [equipment, setEquipment] = useState(''), [when, setWhen] = useState(localTime);
  const [documentType, setDocumentType] = useState(''), [file, setFile] = useState<File | null>(null), [documentReason, setDocumentReason] = useState('');
  const [evidence, setEvidence] = useState(''), [facility, setFacility] = useState(''), [supplementReason, setSupplementReason] = useState(''), [hold, setHold] = useState(''), [responseAction, setResponseAction] = useState('ACKNOWLEDGE');
  useEffect(() => {
    let alive = true;
    void readWorkflowPending(key).then(p => { if (alive) { setPending(p); setRestored(true); } }).catch(e => { if (alive) setError(errorMessage(e)); });
    return () => { alive = false; };
  }, [key]);
  useEffect(() => {
    const controller = new AbortController();
    void api<Workflow>(route, { signal: controller.signal }).then(setData).catch(e => { if (!controller.signal.aborted) setError(errorMessage(e)); });
    if (operator || manager) void api<Lookup>('/api/ledger/lookups', { signal: controller.signal }).then(setLook).catch(e => { if (!controller.signal.aborted) setError(errorMessage(e)); });
    return () => controller.abort();
  }, [route, reload, operator, manager]);
  async function mutate(path: string, fields: object) {
    if (busy || !data || !restored) return; setBusy(true); setError('');
    const request = pending ?? { path: `${route}/${path}`, body: JSON.stringify({ operationId: crypto.randomUUID(), version: data.version, ...fields }) };
    setPending(request);
    try {
      await storeWorkflowPending(key, request);
      const result = await api<Workflow>(request.path, { method: 'POST', body: request.body });
      if (result.containerId !== id || !Number.isInteger(result.version)) throw Error('The workflow result is uncertain. Retry the same operation.');
      await storeWorkflowPending(key, null); setPending(null); setData(result); setReload(n => n + 1); onChanged?.();
    } catch (cause) {
      if (cause instanceof ApiError && cause.status < 500 && cause.status !== 408) { await storeWorkflowPending(key, null); setPending(null); setReload(n => n + 1); }
      setError(errorMessage(cause));
    } finally { setBusy(false); }
  }
  const positions = data?.proposals.flatMap(p => p.context.outputs.filter(o => positive(o.remainingKg)).map(o => ({ ...o, proposalId: p.id, facilityId: p.context.facilityId }))) ?? [];
  const selected = positions.find(o => `${o.proposalId}:${o.lotId}:${o.locationId}` === source);
  const facilities = [...new Set(data?.proposals.map(p => p.context.facilityId) ?? [])];
  async function uploadDocument(event: FormEvent) {
    event.preventDefault(); if (!file || pending) return;
    if (!file.size || file.size > 10485760) { setError('Choose a nonempty shipment document, maximum 10 MiB.'); return; }
    try {
      const content = await new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onerror = () => reject(Error('Could not read document.')); reader.onload = () => resolve(String(reader.result).split(',')[1]); reader.readAsDataURL(file); });
      await mutate('documents', { documentType, filename: file.name, contentType: file.type || 'application/octet-stream', content, reason: documentReason });
    } catch (cause) { setError(errorMessage(cause)); }
  }
  function saveHandoff(event: FormEvent) {
    event.preventDefault(); if (!selected || pending) return;
    void mutate('handoffs', { proposalId: selected.proposalId, kind, content: { lotId: selected.lotId, fromLocationId: selected.locationId, toLocationId: destination, quantityKg: kg,
      occurredAt: new Date(when).toISOString(), poolRevision: selected.poolRevision ?? null,
      ...(kind === 'BAGGING' ? { equipmentId: equipment, outputKg, declaredLossKg: '0' } : {}) } });
  }
  return <section className="panel export-section"><h2>Container reviews and shipment records</h2>
    {error && <p role="alert" className="notice error">{error}</p>}
    {pending && <p role="alert" className="notice error">A workflow operation has an uncertain result. <button disabled={busy || !data} onClick={() => void mutate('', {})}>Retry workflow operation</button></p>}
    <button className="secondary" disabled={busy || !!pending} onClick={() => { setError(''); setReload(n => n + 1); }}>Refresh container workflow</button>
    {!restored && <p role="status">Restoring pending workflow operations…</p>}
    {data && restored && <>
      {noticeId && <button disabled={busy || !!pending} onClick={() => void mutate('notifications/read', { noticeId })}>Mark container notification read</button>}
      {canReview && data.status !== 'CLOSED' && <form onSubmit={event => { event.preventDefault(); if (!pending) void mutate('reviews', { proposalId: proposal, scope, decision: 'APPROVED', reason }); }}><fieldset disabled={busy || !!pending}>
        <legend>Review remaining plan scope</legend>
        <p>A new review retains earlier production and loading. Activating another plan releases the previous plan's unconsumed reservations. An active HOLD still blocks execution.</p>
        <label>Proposal to review<select required value={proposal} onChange={event => setProposal(event.target.value)}><option value="">Select proposal</option>{data.proposals.map(p => <option key={p.id} value={p.id}>Plan v{p.version} · {p.context.mode.replaceAll('_', ' ')} · {p.context.remainingProductionKg} kg not produced</option>)}</select></label>
        <label>Review scope<select value={scope} onChange={event => setScope(event.target.value)}><option value="OUTPUT_ONLY">Existing outputs only</option><option value="ALL_REMAINING">Remaining inputs and existing outputs</option></select></label>
        <label>Scope review reason<textarea required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label>
        <button disabled={!proposal || !reason.trim()}>Approve and activate scope</button>{' '}
        <button type="button" disabled={!proposal || !reason.trim()} onClick={() => void mutate('reviews', { proposalId: proposal, scope, decision: 'REJECTED', reason })}>Reject scope</button>
      </fieldset></form>}
      {data.reviews.length > 0 && <details><summary>Retained scope reviews</summary><ul>{data.reviews.map(r => <li key={r.id}>{r.scope} · {r.decision} · {r.reason} · {new Date(r.recordedAt).toLocaleString()}</li>)}</ul></details>}
      {operator && data.status !== 'CLOSED' && <form onSubmit={saveHandoff}><fieldset disabled={busy || !!pending}><legend>Prepare reviewed stock handoff</legend>
        <p>The Customer Manager reviews the saved handoff before execution. The resulting output requires a new loading review.</p>
        <label>Output to move or bag<select required value={source} onChange={event => { setSource(event.target.value); setDestination(''); setEquipment(''); }}><option value="">Select output position</option>{positions.map(o => <option key={`${o.proposalId}:${o.lotId}:${o.locationId}`} value={`${o.proposalId}:${o.lotId}:${o.locationId}`}>{o.lotCode} · {o.locationName} · {o.remainingKg} kg</option>)}</select></label>
        <label>Handoff operation<select value={kind} onChange={event => setKind(event.target.value)}><option value="TRANSFER">Transfer</option><option value="BAGGING">Bagging</option></select></label>
        <label>Handoff destination<select required value={destination} onChange={event => setDestination(event.target.value)}><option value="">Select location</option>{look?.locations.filter(l => l.facilityId === selected?.facilityId && (!l.ownerId || l.ownerId === data.ownerId)).map(l => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
        <Mass label="Handoff actual input (kg)" value={kg} onChange={setKg} />
        <label>Handoff occurred at<input type="datetime-local" required value={when} onChange={event => setWhen(event.target.value)} /></label>
        {kind === 'BAGGING' && <><label>Bagging equipment<select required value={equipment} onChange={event => setEquipment(event.target.value)}><option value="">Select equipment</option>{look?.equipment.filter(e => e.facilityId === selected?.facilityId).map(e => <option key={e.id} value={e.id}>{e.name}</option>)}</select></label>
          <Mass label="Actual bagged output (kg)" value={outputKg} onChange={setOutputKg} /><p>This handoff supports equal input/output with zero declared loss. Stop if measured quantities differ; do not adjust measurements.</p></>}
        <button disabled={!selected}>Save handoff for review</button>
      </fieldset></form>}
      {data.handoffs.length > 0 && <section><h3>Stock handoffs</h3>{data.handoffs.map(h => <div key={h.id}><p>{h.kind} · {h.state} · {h.content.quantityKg} kg</p>
        {canReview && h.state === 'DRAFT' && <button disabled={busy || !!pending || !reason.trim()} onClick={() => void mutate('handoffs/approve', { handoffId: h.id, handoffVersion: h.version, reason })}>Approve saved handoff</button>}
        {operator && h.state === 'APPROVED' && <button disabled={busy || !!pending} onClick={() => { if (window.confirm('Commit this actual reviewed handoff?')) void mutate('handoffs/commit', { handoffId: h.id, handoffVersion: h.version }); }}>Commit reviewed handoff</button>}
      </div>)}</section>}
      {canWrite && data.status === 'CLOSED' && <button type="button" disabled={busy || !!pending} onClick={() => void mutate('documents/packing-list', {})}>Generate Packing List</button>}
      {canWrite && data.status === 'CLOSED' && <form onSubmit={event => void uploadDocument(event)}><fieldset disabled={busy || !!pending}><legend>Retain a shipment document version</legend>
        <label>Document type<input required maxLength={80} value={documentType} onChange={event => setDocumentType(event.target.value)} /></label>
        <label>Shipment document file<input type="file" required onChange={event => setFile(event.target.files?.[0] || null)} /></label>
        <label>Document version reason<textarea required maxLength={1000} value={documentReason} onChange={event => setDocumentReason(event.target.value)} /></label>
        <button>Save document version</button>
      </fieldset></form>}
      {data.documents.length > 0 && <section><h3>Versioned shipment documents</h3><ul>{data.documents.map(d => <li key={d.id}>{d.documentType} v{d.version} · {d.origin === 'GENERATED' ? 'Generated from closure dossier' : d.origin === 'UPLOADED' ? 'Uploaded file' : 'Origin unrecorded'} · <a href={`${route}/documents/${d.id}/file`}>{d.filename}</a></li>)}</ul></section>}
      {(canWrite && data.status === 'CLOSED' || manager && data.holds.length > 0) && <section>
        <label>Evidence facility<select required value={facility} onChange={event => { setFacility(event.target.value); setEvidence(''); }}><option value="">Select facility</option>{facilities.map(f => <option key={f} value={f}>{look?.facilities.find(l => l.id === f)?.name || `Shipment facility ${facilities.indexOf(f) + 1}`}</option>)}</select></label>
        <Evidence ownerId={data.ownerId} facilityId={facility} value={evidence} onChange={setEvidence} />
        {canWrite && data.status === 'CLOSED' && <form onSubmit={event => { event.preventDefault(); if (!pending) void mutate('supplements', { evidenceId: evidence, reason: supplementReason }); }}><fieldset disabled={busy || !!pending}><legend>Append dossier evidence supplement</legend>
          <p>The closing snapshot and CLOSED state remain unchanged.</p><label>Supplement reason<textarea required maxLength={1000} value={supplementReason} onChange={event => setSupplementReason(event.target.value)} /></label>
          <button disabled={!evidence}>Append evidence supplement</button>
        </fieldset></form>}
        {manager && data.holds.length > 0 && <form onSubmit={event => { event.preventDefault(); if (!pending) void mutate('hold-responses', { holdId: hold, facilityId: facility, responseAction, evidenceId: evidence, reason: supplementReason }); }}><fieldset disabled={busy || !!pending}><legend>Record container HOLD handling</legend>
          <label>Container HOLD<select required value={hold} onChange={event => setHold(event.target.value)}><option value="">Select HOLD</option>{data.holds.map(h => <option key={h.id} value={h.id}>{h.reason}</option>)}</select></label>
          <label>Handling action<select value={responseAction} onChange={event => setResponseAction(event.target.value)}><option value="ACKNOWLEDGE">Acknowledge</option><option value="RECORD_RESPONSE">Record response</option></select></label>
          <p>Remaining lot stock is isolated through Ledger HOLD handling. Container unloading and HOLD release are outside this workflow.</p>
          <label>Handling response<textarea required maxLength={1000} value={supplementReason} onChange={event => setSupplementReason(event.target.value)} /></label><button disabled={!evidence || !hold || !facility}>Save HOLD handling</button>
        </fieldset></form>}
      </section>}
      {data.supplements.length > 0 && <section><h3>Dossier evidence supplements</h3><ul>{data.supplements.map(s => <li key={s.id}>{s.reason} · {new Date(s.recordedAt).toLocaleString()} · <a href={`/api/ledger/evidence/${s.evidenceId}`} target="_blank" rel="noreferrer">Evidence</a></li>)}</ul></section>}
      {data.holdResponses.length > 0 && <section><h3>HOLD handling history</h3><ul>{data.holdResponses.map(h => <li key={h.id}>{h.action} · {h.reason} · {new Date(h.recordedAt).toLocaleString()}</li>)}</ul></section>}
    </>}
  </section>;
}

export function ContainerNotices({ user, active = true }: { user: User; active?: boolean }) {
  const [items, setItems] = useState<{ id: string; containerId: string; code: string; message: string; readAt: string | null }[]>([]), [error, setError] = useState(''), [reload, setReload] = useState(0), [selected, setSelected] = useState<{ id: string; containerId: string }>();
  useEffect(() => {
    if (!active || !['CustomerManager', 'ExportStaff', 'MillManager'].includes(user.role || '')) return;
    const controller = new AbortController();
    void api<{ items: typeof items }>('/api/container-notifications', { signal: controller.signal }).then(x => setItems(x.items)).catch(e => { if (!controller.signal.aborted) setError(errorMessage(e)); });
    return () => controller.abort();
  }, [active, user.id, user.role, reload]);
  if (!['CustomerManager', 'ExportStaff', 'MillManager'].includes(user.role || '')) return null;
  return <section><h3>Container HOLD notifications</h3>{error && <p role="alert">{error}</p>}<button onClick={() => setReload(n => n + 1)}>Refresh container alerts</button>
    <ul>{items.map(n => <li key={n.id}>{n.code} · {n.message} · {n.readAt ? 'Read' : 'Unread'} <button onClick={() => setSelected(n)}>Open container alert</button></li>)}</ul>
    {selected && <ContainerWorkflow key={`${user.id}:${selected.containerId}`} id={selected.containerId} userId={user.id} noticeId={selected.id} manager={user.role === 'MillManager'} onChanged={() => setReload(n => n + 1)} />}
  </section>;
}
