import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { Evidence, Feedback, type Lookup } from './ledger-ui';

export type HoldTarget = { type: 'lot' | 'container'; id: string; code: string; version?: number };

export function TraceHoldForm({ target, ownerId, onSaved }: { target: HoldTarget; ownerId: string; onSaved?: () => void }) {
  const [open, setOpen] = useState(false);
  const [facilities, setFacilities] = useState<Lookup['facilities']>([]);
  const [facility, setFacility] = useState(''), [evidence, setEvidence] = useState(''), [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false), [error, setError] = useState(''), [saved, setSaved] = useState(false);
  const [pending, setPending] = useState<string>();
  useEffect(() => {
    if (!open) return;
    const abort = new AbortController();
    void api<Lookup>('/api/ledger/lookups', { signal: abort.signal })
      .then(lookups => setFacilities(lookups.facilities))
      .catch(cause => { if (!abort.signal.aborted) setError(errorMessage(cause)); });
    return () => abort.abort();
  }, [open]);
  async function place(event: FormEvent) {
    event.preventDefault();
    if (busy || saved || !evidence || !reason.trim()) return;
    const operationId = crypto.randomUUID();
    const body = pending ?? JSON.stringify(target.type === 'container'
      ? { operationId, version: target.version, evidenceId: evidence, reason }
      : { id: operationId, lotId: target.id, evidenceId: evidence, reason });
    const url = target.type === 'container' ? `/api/export-containers/${target.id}/execution/holds` : '/api/ledger/holds';
    setBusy(true); setError(''); setPending(body);
    try {
      await api(url, { method: 'POST', body });
      setPending(undefined); setSaved(true); onSaved?.();
    } catch (cause) {
      if (cause instanceof ApiError && cause.status < 500 && cause.status !== 408) setPending(undefined);
      setError(errorMessage(cause));
    } finally { setBusy(false); }
  }
  if (saved) return <p role="status">HOLD recorded for {target.code}. Run the trace again to see current restrictions.</p>;
  if (!open) return <button type="button" onClick={() => setOpen(true)}>Place HOLD on {target.code}</button>;
  return <form onSubmit={event => void place(event)}>
    <Feedback error={error} />
    <fieldset disabled={busy || !!pending}>
      <legend>HOLD {target.code}</legend>
      <label>Evidence facility<select required value={facility} onChange={event => { setFacility(event.target.value); setEvidence(''); }}>
        <option value="">Select the affected facility</option>
        {facilities.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
      </select></label>
      <Evidence key={facility} ownerId={ownerId} facilityId={facility} value={evidence} onChange={setEvidence} />
      <label>Reason<textarea required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} /></label>
    </fieldset>
    <button disabled={busy || !evidence || !reason.trim()}>{pending ? 'Retry HOLD' : 'Confirm HOLD'}</button>
    {!pending && <button type="button" onClick={() => setOpen(false)}>Cancel</button>}
  </form>;
}
