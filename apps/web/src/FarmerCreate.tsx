import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { FarmerDuplicateNotice } from './DuplicateNotice';
import { useFarmerDuplicateCheck } from './useDuplicateCheck';

type RegisterFarmerRequest = {
  deviceId: string;
  clientOperationId: string;
  capturedAt: string;
  payloadSchemaVersion: 1;
  payload: { entityId: string; name: string; contact: string | null };
};

type CreatedFarmer = { id: string; code: string; name: string };

function readPending(key: string): RegisterFarmerRequest | null {
  try {
    const value = window.sessionStorage.getItem(key);
    if (!value) return null;
    const request = JSON.parse(value) as RegisterFarmerRequest;
    return request.clientOperationId && request.payload?.entityId && request.deviceId ? request : null;
  } catch { return null; }
}

function rememberPending(key: string, request: RegisterFarmerRequest) {
  try { window.sessionStorage.setItem(key, JSON.stringify(request)); } catch { /* In-memory retry still works. */ }
}

function clearPending(key: string) {
  try { window.sessionStorage.removeItem(key); } catch { /* Storage may be disabled. */ }
}

function deviceId() {
  const key = 'grainledger:capture:device-id';
  try {
    const existing = window.localStorage.getItem(key);
    if (existing) return existing;
    const created = `web-${crypto.randomUUID()}`;
    window.localStorage.setItem(key, created);
    return created;
  } catch { return `web-${crypto.randomUUID()}`; }
}

export function FarmerCreate({ userId, companyId, onCancel, onCreated }: {
  userId: string;
  companyId: string;
  onCancel: () => void;
  onCreated: (id: string) => void;
}) {
  const key = `grainledger:farmer-create:${userId}:${companyId}`;
  const [pending, setPending] = useState<RegisterFarmerRequest | null>(() => readPending(key));
  const [name, setName] = useState(() => readPending(key)?.payload.name ?? '');
  const [contact, setContact] = useState(() => readPending(key)?.payload.contact ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const sending = useRef(false);
  const nameInput = useRef<HTMLInputElement>(null);
  const duplicates = useFarmerDuplicateCheck(name, contact, undefined, !!pending || busy);

  useEffect(() => { nameInput.current?.focus(); }, []);

  async function save(event: FormEvent) {
    event.preventDefault();
    if (sending.current || !name.trim()) return;
    sending.current = true;
    setBusy(true);
    setError('');
    const request: RegisterFarmerRequest = pending ?? {
      deviceId: deviceId(),
      clientOperationId: crypto.randomUUID(),
      capturedAt: new Date().toISOString(),
      payloadSchemaVersion: 1,
      payload: { entityId: crypto.randomUUID(), name: name.trim(), contact: contact.trim() || null },
    };
    if (!pending) { setPending(request); rememberPending(key, request); }
    try {
      const farmer = await api<CreatedFarmer>('/api/farmers', { method: 'POST', body: JSON.stringify(request) });
      clearPending(key);
      setPending(null);
      onCreated(farmer.id);
    } catch (cause) {
      if (cause instanceof ApiError && [400, 409].includes(cause.status)) {
        clearPending(key);
        setPending(null);
      }
      setError(errorMessage(cause));
    } finally {
      sending.current = false;
      setBusy(false);
    }
  }

  return <div className="farmer-modal-backdrop">
    <section className="farmer-modal" role="dialog" aria-modal="true" aria-labelledby="add-farmer-title" onKeyDown={event => { if (event.key === 'Escape' && !busy) onCancel(); }}>
      <div className="farmer-modal-heading"><div><span className="eyebrow">FIELD RECORDS</span><h2 id="add-farmer-title">Add farmer</h2></div><button className="farmer-modal-close" type="button" onClick={onCancel} disabled={busy} aria-label="Close add farmer form">×</button></div>
      <p className="muted">Enter the farmer's details. The system creates the farmer code after saving.</p>
      <form onSubmit={save}>
        <label htmlFor="new-farmer-name">Full name <span className="required-mark">*</span><input ref={nameInput} id="new-farmer-name" value={name} onChange={event => setName(event.target.value)} required maxLength={200} disabled={busy || !!pending} autoComplete="name" placeholder="e.g. Nguyễn Văn A" /></label>
        <label htmlFor="new-farmer-contact">Contact <span className="optional">(optional)</span><input id="new-farmer-contact" value={contact} onChange={event => setContact(event.target.value)} disabled={busy || !!pending} placeholder="Phone number or other contact detail" /></label>
        <FarmerDuplicateNotice state={duplicates} />
        {pending && <div className="notice pending">The request may already have reached the server. Retry will send the same request to prevent a duplicate farmer.</div>}
        {error && <div className="notice error" role="alert">{error}</div>}
        <div className="form-footer"><button type="button" className="secondary" onClick={onCancel} disabled={busy}>Cancel</button><button type="submit" disabled={busy || !name.trim()}>{busy ? 'Saving…' : pending ? 'Retry save' : 'Save farmer'}</button></div>
      </form>
    </section>
  </div>;
}
