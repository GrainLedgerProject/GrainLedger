import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage, type User } from './api';
import { Mass, type Lookup } from './ledger-ui';
import { ContainerWorkflow } from './ContainerWorkflow';

type Draft = { id: string; version: string; equipmentId: string; occurredAt: string; declaredLossKg: string;
  inputs: { lotId: string; locationId: string; quantityKg: string; poolRevision?: number }[];
  outputs: { id: string; code: string; commodityId: string; stage: string; productIdentity: string; locationId: string; quantityKg: string }[] };
type Task = { containerId: string; code: string; state: string; version: number; proposalId?: string; facilityId?: string; ownerId: string;
  outputCommodityId: string; operationId?: string; outputId?: string; outputCode?: string; targetKg: string; plannedKg?: string; remainingProductionKg?: string; operation?: Draft; mode?: string;
  inputs?: { lotId: string; lotCode: string; locationId: string; quantityKg: string; approvedKg?: string; poolRevision?: number | null }[] };
type Pending = { path: string; body: string };
const localTime = () => { const d = new Date(); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16); };
const readPending = (key: string): Pending | null => { try { const v = JSON.parse(sessionStorage.getItem(key) || 'null') as Pending | null; return v && typeof v.path === 'string' && typeof v.body === 'string' ? v : null; } catch { return null; } };
const microKg = (value: string) => { if (!/^\d+(\.\d{1,6})?$/.test(value)) throw Error('Use a positive exact kg value with at most six decimal places.'); const [whole, fraction = ''] = value.split('.'); return BigInt(whole) * 1000000n + BigInt(fraction.padEnd(6, '0')); };
const kgText = (value: bigint) => `${value / 1000000n}.${(value % 1000000n).toString().padStart(6, '0')}`;

export function ContainerProduction({ id, userId, canRequest = false, canReview = false, operator = false, containerVersion = 0, proposalId, closed = false, onChanged }: {
  id: string; userId: string; canRequest?: boolean; canReview?: boolean; operator?: boolean; containerVersion?: number; proposalId?: string; closed?: boolean; onChanged?: () => void;
}) {
  const route = `/api/export-containers/${id}/production`, key = `grainledger.production.${userId}.${id}`;
  const [task, setTask] = useState<Task>(), [look, setLook] = useState<Lookup>(), [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false), [error, setError] = useState(''), [pending, setPending] = useState<Pending | null>(() => readPending(key));
  const [equipment, setEquipment] = useState(''), [location, setLocation] = useState(''), [loss, setLoss] = useState('0'), [when, setWhen] = useState(localTime), [reason, setReason] = useState('');
  const [confirmed, setConfirmed] = useState(false), [dirty, setDirty] = useState(false), [outputKg, setOutputKg] = useState('');
  useEffect(() => {
    const controller = new AbortController();
    void api<Task>(route, { signal: controller.signal }).then(value => {
      setTask(value); setOutputKg(value.operation?.outputs[0]?.quantityKg || value.targetKg || ''); if (value.operation) { setEquipment(value.operation.equipmentId); setLocation(value.operation.outputs[0]?.locationId || ''); setLoss(value.operation.declaredLossKg); const d = new Date(value.operation.occurredAt); setWhen(new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16)); }
    }).catch(e => { if (!controller.signal.aborted) setError(errorMessage(e)); });
    if (operator) void api<Lookup>('/api/ledger/lookups', { signal: controller.signal }).then(setLook).catch(e => { if (!controller.signal.aborted) setError(errorMessage(e)); });
    return () => controller.abort();
  }, [route, reload, operator]);
  async function mutate(action: string, fields: object) {
    if (busy) return; setBusy(true); setError('');
    const request = pending ?? { path: `${route}/${action}`, body: JSON.stringify({ operationId: crypto.randomUUID(), ...fields }) };
    sessionStorage.setItem(key, JSON.stringify(request)); setPending(request);
    try {
      const result = await api<Task>(request.path, { method: 'POST', body: request.body });
      if (result.containerId !== id || !Number.isInteger(result.version)) throw new Error('The production result is uncertain. Retry the same operation.');
      sessionStorage.removeItem(key); setPending(null); setTask(result); setConfirmed(false); setDirty(false); setReload(n => n + 1); onChanged?.();
    } catch (cause) {
      if (cause instanceof ApiError && cause.status < 500 && cause.status !== 408) { sessionStorage.removeItem(key); setPending(null); }
      setError(errorMessage(cause));
    } finally { setBusy(false); }
  }
  function saveDraft(event: FormEvent) {
    event.preventDefault(); if (!task || pending) return;
    const existing = task.operation;
    let inputs: Draft['inputs'];
    try {
      const planned = microKg(task.plannedKg || task.targetKg), actual = microKg(outputKg);
      if (planned <= 0n || actual <= 0n) throw Error('Positive planned and actual output are required.');
      inputs = (task.inputs || []).map(input => {
        const scaled = microKg(input.approvedKg || input.quantityKg) * actual;
        if (scaled % planned !== 0n) throw Error('This batch cannot retain the approved recipe at six-place kg precision.');
        const quantity = scaled / planned;
        if (quantity <= 0n || quantity > microKg(input.quantityKg)) throw Error('This batch exceeds the remaining approved input quota.');
        return { lotId: input.lotId, locationId: input.locationId, quantityKg: kgText(quantity), ...(input.poolRevision != null ? { poolRevision: input.poolRevision } : {}) };
      });
    } catch (cause) { setError(errorMessage(cause)); return; }
    void mutate('draft', { version: task.version, draft: {
      id: existing?.id || crypto.randomUUID(), ownerId: task.ownerId, facilityId: task.facilityId, kind: 'BLENDING',
      occurredAt: new Date(when).toISOString(), equipmentId: equipment,
      inputs,
      outputs: [{ id: existing?.outputs[0].id || crypto.randomUUID(), code: existing?.outputs[0].code || '', commodityId: task.outputCommodityId,
        stage: 'BLENDED_RICE', productIdentity: 'BLENDED_RICE', locationId: location, quantityKg: outputKg }],
      declaredLossKg: loss, ...(existing ? { version: existing.version } : {}),
    } });
  }
  return <section className="panel export-section"><h2>Approved blend production</h2>
    <p>Produce the approved inputs before loading. Finished output requires its own Customer Manager review.</p>
    {error && <p role="alert" className="notice error">{error}</p>}
    {pending && <p role="alert" className="notice error">A production operation has an uncertain result. <button disabled={busy} onClick={() => void mutate('', {})}>Retry production operation</button></p>}
    <button className="secondary" disabled={busy || !!pending} onClick={() => { setError(''); setReload(n => n + 1); }}>Refresh production</button>
    {task && <><p>Production status: <strong>{task.state.replaceAll('_', ' ')}</strong></p>
      {task.operationId && <details><summary>Production reference for mill review</summary><p>Operation: {task.operationId}. An assigned Mill Manager can review this saved operation in Ledger → Process &amp; correct when variance confirmation is required.</p></details>}
      {task.outputId && <p>Finished output: {task.outputCode} · {task.targetKg} kg</p>}
      {task.inputs && <ul>{task.inputs.map(i => <li key={`${i.lotId}:${i.locationId}`}>{i.lotCode} · {i.quantityKg} kg approved input</li>)}</ul>}
      {['NOT_REQUESTED', 'APPROVAL_CHANGED'].includes(task.state) && canRequest && proposalId && !closed && <button disabled={busy || !!pending} onClick={() => void mutate('request', { version: containerVersion, proposalId })}>Request approved blend production</button>}
      {operator && ['REQUESTED', 'DRAFT'].includes(task.state) && <form onSubmit={saveDraft}><fieldset disabled={busy || !!pending}>
        <legend>Actual blend preparation</legend>
        <label>Production equipment<select required value={equipment} onChange={e => { setEquipment(e.target.value); setConfirmed(false); setDirty(true); }}><option value="">Select equipment</option>{look?.equipment.filter(e => e.facilityId === task.facilityId).map(e => <option key={e.id} value={e.id}>{e.name}</option>)}</select></label>
        <label>Finished output location<select required value={location} onChange={e => { setLocation(e.target.value); setConfirmed(false); setDirty(true); }}><option value="">Select ordinary location</option>{look?.locations.filter(l => l.facilityId === task.facilityId && !l.mixedBulk && (!l.ownerId || l.ownerId === task.ownerId)).map(l => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
        <label>Production occurred at<input type="datetime-local" required value={when} onChange={e => { setWhen(e.target.value); setConfirmed(false); setDirty(true); }} /></label>
        <Mass label="Actual finished output (kg)" value={outputKg} onChange={v => { setOutputKg(v); setConfirmed(false); setDirty(true); }} />
        <Mass label="Declared blend loss (kg)" value={loss} onChange={v => { setLoss(v); setConfirmed(false); setDirty(true); }} />
        <p>Remaining approved production: {task.remainingProductionKg || task.targetKg} kg. Each batch retains the approved recipe and uses only remaining quota. Stop if actual yield falls outside the approved scope; do not adjust measurements to fit.</p>
        <button>Save approved blend draft</button>
      </fieldset></form>}
      {operator && task.state === 'DRAFT' && <fieldset disabled={busy || !!pending}><p>Save changes before committing. Nonzero variance requires the existing Mill Manager confirmation for this exact operation version before commit.</p>
        <label><input type="checkbox" checked={confirmed} onChange={e => setConfirmed(e.target.checked)} />I confirm the saved draft records the actual production.</label>
        <button disabled={!confirmed || dirty} onClick={() => void mutate('commit', { version: task.version })}>Commit approved blend production</button>
      </fieldset>}
      {operator && ['PRODUCED', 'APPROVED', 'REJECTED'].includes(task.state) && task.remainingProductionKg && microKg(task.remainingProductionKg) > 0n && <button disabled={busy || !!pending} onClick={() => void mutate('next', { version: task.version })}>Prepare remaining approved production</button>}
      {canReview && task.outputId && !closed && <form onSubmit={e => { e.preventDefault(); if (!pending) void mutate('approve', { version: task.version, reason }); }}><fieldset disabled={busy || !!pending}>
        <legend>Review actual output</legend><p>Confirm finished-lot quality in Export assessment before approval. Missing or changed evidence blocks loading.</p>
        <label>Output review reason<textarea required maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} /></label>
        <button>Approve actual output</button>{' '}<button type="button" disabled={!reason.trim()} onClick={() => void mutate('reject', { version: task.version, reason })}>Reject actual output</button>
      </fieldset></form>}
    </>}
  </section>;
}

export function ContainerProductionTasks({ user, active = true }: { user: User; active?: boolean }) {
  const [tasks, setTasks] = useState<Task[]>([]), [selected, setSelected] = useState(''), [error, setError] = useState(''), [reload, setReload] = useState(0);
  useEffect(() => {
    if (!active) return; const controller = new AbortController();
    void api<{ items: Task[] }>('/api/container-work-items', { signal: controller.signal }).then(x => setTasks(x.items)).catch(e => { if (!controller.signal.aborted) setError(errorMessage(e)); });
    return () => controller.abort();
  }, [active, reload]);
  return <><h2>Approved production tasks</h2>{error && <p role="alert">{error}</p>}<button onClick={() => setReload(n => n + 1)}>Refresh production tasks</button>
    <ul>{tasks.map(t => <li key={t.containerId}><button onClick={() => setSelected(t.containerId)}>{t.code} · {t.state}</button></li>)}</ul>
    {selected && active && <>{tasks.find(t => t.containerId === selected)?.mode === 'BLEND_INPUT_STOCK' && user.role === 'MillOperator' && <ContainerProduction key={`${user.id}:${selected}`} id={selected} userId={user.id} operator onChanged={() => setReload(n => n + 1)} />}
      <ContainerWorkflow key={`${user.id}:${selected}:workflow`} id={selected} userId={user.id} operator={user.role === 'MillOperator'} manager={user.role === 'MillManager'} onChanged={() => setReload(n => n + 1)} /></>}
  </>;
}
