import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';

type Stage = 'BRAN' | 'HUSK';
type Policy = { stage: Stage; allowCrossVariety: boolean; source: 'SYSTEM_DEFAULT' | 'COMPANY_OVERRIDE'; revision: string | null };
type Choice = 'default' | 'allow' | 'separate';

export function ByproductVarietyPolicy() {
  const [items, setItems] = useState<Policy[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  async function load() {
    setLoading(true);
    try { setItems((await api<{ items: Policy[] }>('/api/ledger/byproduct-variety-policy')).items); }
    catch (e) { setError(errorMessage(e)); }
    finally { setLoading(false); }
  }
  useEffect(() => { void load(); }, []);
  return <>
    <div className="page-heading"><span className="eyebrow">COMPANY SETTINGS</span><h1>Byproduct mixing</h1><p className="muted">Choose whether different rice varieties may be mixed for each byproduct. Changes apply to future mixing only.</p></div>
    {error && <div className="notice error" role="alert">{error} <button type="button" className="text-button" onClick={() => { setError(''); void load(); }}>Reload</button></div>}
    {loading && <p role="status">Loading policies…</p>}
    {!loading && <div className="stack">{items.map(item => <PolicyRow key={`${item.stage}:${item.revision}`} policy={item} onSaved={updated => setItems(previous => previous.map(row => row.stage === updated.stage ? updated : row))} onConflict={() => { setError('Policy changed. Reloaded settings; review before saving again.'); void load(); }} />)}</div>}
  </>;
}

function PolicyRow({ policy, onSaved, onConflict }: { policy: Policy; onSaved: (policy: Policy) => void; onConflict: () => void }) {
  const [selected, setSelected] = useState<Choice>(policy.source === 'SYSTEM_DEFAULT' ? 'default' : policy.allowCrossVariety ? 'allow' : 'separate');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function save(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError('');
    try {
      const updated = await api<Policy>(`/api/ledger/byproduct-variety-policy/${policy.stage}`, { method: 'PUT', body: JSON.stringify({ allowCrossVariety: selected === 'default' ? null : selected === 'allow', expectedRevision: policy.revision, reason: reason.trim() }) });
      onSaved(updated); setReason('');
    } catch (e) { if (e instanceof ApiError && e.status === 409) onConflict(); else setError(errorMessage(e)); }
    finally { setBusy(false); }
  }
  return <section className="panel"><div className="panel-heading"><h2>{policy.stage === 'BRAN' ? 'Bran' : 'Husk'}</h2><p className="muted">Effective: {policy.allowCrossVariety ? 'Allow different varieties' : 'Keep varieties separate'} · {policy.source === 'SYSTEM_DEFAULT' ? 'System default' : 'Company override'}</p></div>
    <form onSubmit={save}>
      <label>{policy.stage === 'BRAN' ? 'Bran' : 'Husk'} mixing policy<select value={selected} onChange={e => setSelected(e.target.value as Choice)} disabled={busy}><option value="default">Use system default (Allow)</option><option value="allow">Allow different varieties</option><option value="separate">Keep varieties separate</option></select></label>
      <label>Reason<input value={reason} onChange={e => setReason(e.target.value)} required maxLength={500} disabled={busy} /></label>
      {error && <div className="notice error" role="alert">{error}</div>}
      <div className="form-footer"><button type="submit" disabled={busy || !reason.trim()}>{busy ? 'Saving…' : 'Save'}</button></div>
    </form>
  </section>;
}
