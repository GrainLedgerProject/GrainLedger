import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { FarmerDuplicateNotice } from './DuplicateNotice';
import { useFarmerDuplicateCheck } from './useDuplicateCheck';

type FarmerReference = {
  id: string;
  code: string;
  name: string;
  contact: string | null;
  version: number;
  canEdit: boolean;
};

export function FarmerEdit({ id, onCancel, onUpdated }: {
  id: string;
  onCancel: () => void;
  onUpdated: () => void;
}) {
  const [original, setOriginal] = useState<FarmerReference | null>(null);
  const [name, setName] = useState('');
  const [contact, setContact] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const sending = useRef(false);
  const nameInput = useRef<HTMLInputElement>(null);
  const duplicates = useFarmerDuplicateCheck(name, contact, id, loading || busy || !original?.canEdit);

  useEffect(() => {
    const controller = new AbortController();
    void api<FarmerReference>(`/api/farmers/${encodeURIComponent(id)}`, { signal: controller.signal })
      .then(value => {
        setOriginal(value);
        setName(value.name);
        setContact(value.contact ?? '');
        if (!value.canEdit) setError('You cannot edit this farmer record.');
      })
      .catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [id]);

  useEffect(() => { if (original?.canEdit) nameInput.current?.focus(); }, [original]);

  async function save(event: FormEvent) {
    event.preventDefault();
    if (sending.current || !original?.canEdit || !name.trim()) return;
    sending.current = true;
    setBusy(true);
    setError('');
    const nextName = name.trim();
    const nextContact = contact.trim() || null;
    try {
      await api<FarmerReference>(`/api/farmers/${encodeURIComponent(id)}`, {
        method: 'PUT', body: JSON.stringify({
          code: original.code, name: nextName, contact: nextContact, expectedVersion: original.version,
        }),
      });
      onUpdated();
    } catch (cause) {
      if (cause instanceof ApiError && cause.code === 'REFERENCE_IN_USE') {
        setError('This farmer already has a plot or procurement record and cannot be edited.');
      } else if (cause instanceof ApiError && cause.code === 'REFERENCE_STALE') {
        setError('This farmer changed while you were editing. Close this form, reopen it, and review the current values.');
      } else if (!(cause instanceof ApiError)) {
        try {
          const latest = await api<FarmerReference>(`/api/farmers/${encodeURIComponent(id)}`);
          if (latest.version === original.version + 1 && latest.name === nextName && latest.contact === nextContact) {
            onUpdated();
            return;
          }
        } catch { /* Keep the draft if the readback also fails. */ }
        setError('The save result is unknown. Close and reopen this form to check the latest record before trying again.');
      } else setError(errorMessage(cause));
    } finally {
      sending.current = false;
      setBusy(false);
    }
  }

  return <div className="farmer-modal-backdrop">
    <section className="farmer-modal" role="dialog" aria-modal="true" aria-labelledby="edit-farmer-title"
      onKeyDown={event => { if (event.key === 'Escape' && !busy) onCancel(); }}>
      <div className="farmer-modal-heading"><div><span className="eyebrow">FIELD RECORDS</span><h2 id="edit-farmer-title">Edit farmer</h2></div><button className="farmer-modal-close" type="button" onClick={onCancel} disabled={busy} aria-label="Close edit farmer form">×</button></div>
      {loading && <p role="status">Loading current farmer record…</p>}
      {original && <form onSubmit={save}>
        <p className="muted">Farmer code: <strong>{original.code}</strong></p>
        <label htmlFor="edit-farmer-name">Full name <span className="required-mark">*</span><input ref={nameInput} id="edit-farmer-name" value={name} onChange={event => setName(event.target.value)} required maxLength={200} disabled={busy || !original.canEdit} /></label>
        <label htmlFor="edit-farmer-contact">Contact <span className="optional">(optional)</span><input id="edit-farmer-contact" value={contact} onChange={event => setContact(event.target.value)} disabled={busy || !original.canEdit} /></label>
        <FarmerDuplicateNotice state={duplicates} />
        {error && <div className="notice error" role="alert">{error}</div>}
        <div className="form-footer"><button type="button" className="secondary" onClick={onCancel} disabled={busy}>Cancel</button><button type="submit" disabled={busy || !original.canEdit || !name.trim()}>{busy ? 'Saving…' : 'Save changes'}</button></div>
      </form>}
      {!original && error && <div className="notice error" role="alert">{error}</div>}
    </section>
  </div>;
}
