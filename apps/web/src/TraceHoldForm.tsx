import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { readWorkflowPending, storeWorkflowPending, type WorkflowPending } from './container-workflow-pending';
import { Evidence, Feedback, type Lookup } from './ledger-ui';

export type HoldTarget = { type: 'lot' | 'container'; id: string; code: string; version?: number; held?: boolean };
type HoldReceipt = { id: string; holds?: { id: string }[] };

export function TraceHoldForm({ target, userId, ownerId, disabled, onSaved }: {
  target: HoldTarget; userId: string; ownerId: string; disabled?: boolean; onSaved?: () => void;
}) {
  const key = `grainledger.hold.${userId}.${ownerId}.${target.type}.${target.id}`;
  const [open, setOpen] = useState(false), [restored, setRestored] = useState(false);
  const [facilities, setFacilities] = useState<Lookup['facilities']>([]);
  const [facility, setFacility] = useState(''), [evidence, setEvidence] = useState(''), [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false), [error, setError] = useState(''), [saved, setSaved] = useState(false);
  const [pending, setPending] = useState<WorkflowPending | null>(null);
  const mounted = useRef(false);
  useEffect(() => {
    mounted.current = true;
    let active = true;
    void readWorkflowPending(key)
      .then(request => { if (active) { setPending(request); setRestored(true); } })
      .catch(cause => { if (active) setError(errorMessage(cause)); });
    return () => { active = false; mounted.current = false; };
  }, [key]);
  useEffect(() => {
    if (!open || pending) return;
    const abort = new AbortController();
    void api<Lookup>('/api/ledger/lookups', { signal: abort.signal })
      .then(lookups => { if (!abort.signal.aborted) setFacilities(lookups.facilities); })
      .catch(cause => { if (!abort.signal.aborted) setError(errorMessage(cause)); });
    return () => abort.abort();
  }, [open, pending]);

  async function place(event?: FormEvent) {
    event?.preventDefault();
    if (busy || disabled || !restored || saved || (!pending && (target.held || !evidence || !reason.trim()))) return;
    const operationId = crypto.randomUUID();
    const request = pending ?? (target.type === 'container'
      ? { path: `/api/export-containers/${target.id}/execution/holds`, body: JSON.stringify({ operationId, version: target.version, evidenceId: evidence, reason }) }
      : { path: '/api/ledger/holds', body: JSON.stringify({ id: operationId, lotId: target.id, evidenceId: evidence, reason }) });
    setBusy(true); setError('');
    try {
      await storeWorkflowPending(key, request);
      setPending(request);
      const receipt = await api<HoldReceipt>(request.path, { method: 'POST', body: request.body });
      const submitted = JSON.parse(request.body);
      const valid = target.type === 'container'
        ? receipt?.id === target.id && receipt.holds?.some(hold => hold.id === submitted.operationId)
        : receipt?.id === submitted.id;
      if (!valid) throw Error('The HOLD result is uncertain. Retry the same operation.');
      await storeWorkflowPending(key, null);
      if (mounted.current) { setPending(null); setSaved(true); onSaved?.(); }
    } catch (cause) {
      setError(errorMessage(cause));
      if (cause instanceof ApiError && cause.status < 500 && ![401, 408, 429].includes(cause.status)) {
        try { await storeWorkflowPending(key, null); setPending(null); }
        catch (storageError) { setError(errorMessage(storageError)); }
      }
    } finally { setBusy(false); }
  }

  if (saved) return <p role="status">HOLD recorded for {target.code}.</p>;
  if (pending) return <div>
    <p role="status">The previous HOLD result is uncertain. Retry to recover the same request.</p>
    <Feedback error={error} />
    <button type="button" disabled={busy || disabled} onClick={() => void place()}>Retry HOLD</button>
  </div>;
  if (target.held) return error ? <Feedback error={error} /> : null;
  if (!open) return <><Feedback error={error} /><button type="button" disabled={!restored || disabled} onClick={() => setOpen(true)}>Place HOLD on {target.code}</button></>;
  return <form onSubmit={event => void place(event)}>
    <Feedback error={error} />
    <fieldset disabled={busy || disabled}>
      <legend>HOLD {target.code}</legend>
      <label>Evidence facility<select required value={facility} onChange={event => { setFacility(event.target.value); setEvidence(''); }}>
        <option value="">Select the affected facility</option>
        {facilities.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
      </select></label>
      <Evidence key={facility} ownerId={ownerId} facilityId={facility} value={evidence} onChange={setEvidence} />
      <label>Reason<textarea required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label>
    </fieldset>
    <button disabled={busy || disabled || !evidence || !reason.trim()}>Confirm HOLD</button>
    <button type="button" disabled={busy} onClick={() => setOpen(false)}>Cancel</button>
  </form>;
}
