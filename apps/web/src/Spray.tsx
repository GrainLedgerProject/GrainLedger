import { useEffect, useState, type FormEvent } from 'react';
import { api, errorMessage } from './api';
import { newRequest, pendingRequest, submitCapture, wasRejected, type Envelope } from './captureWrite';

type SprayItem = { id?: string; productName: string | null; substanceId: string | null };
type Spray = { id: string; plotSeasonId: string; appliedOn: string; informationSource: string; evidenceRef: string | null; capturedAt: string; recordedAt: string; items: SprayItem[] };
type SpraySummary = { id: string; appliedOn: string; itemCount: number; recordedAt: string };
type SprayList = { items: SpraySummary[]; page: number; hasMore: boolean };
type Substance = { id: string; code: string; name: string };
type SubstanceList = { items: Substance[]; hasMore: boolean };
type SprayPayload = { entityId: string; plotSeasonId: string; appliedOn: string; informationSource: string; evidenceRef: string | null; items: { productName: string | null; substanceId: string | null }[] };

function vietnamToday() { return new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date()); }

export function SpraySection({ plotSeasonId, canCreate, userId, companyId, onOpen }: { plotSeasonId: string; canCreate: boolean; userId: string; companyId: string; onOpen: (id: string) => void }) {
  const [page, setPage] = useState(1);
  const [retry, setRetry] = useState(0);
  const [list, setList] = useState<SprayList | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [creating, setCreating] = useState(false);
  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    setLoading(true); setError(''); setList(null);
    void api<SprayList>(`/api/spray-declarations?plotSeasonId=${encodeURIComponent(plotSeasonId)}&page=${page}&pageSize=10`, { signal: controller.signal })
      .then(value => { if (active) setList(value); })
      .catch(cause => { if (active) setError(errorMessage(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [plotSeasonId, page, retry]);
  return <section className="panel capture-section">
    <div className="capture-section-heading"><div><span className="eyebrow">SOURCE EVIDENCE</span><h2>Pesticide declarations</h2><p className="muted">Farmer-reported applications; these are not lab results or MRL approval.</p></div>{canCreate && <button onClick={() => setCreating(true)}>+ Record application</button>}</div>
    {loading && <p role="status">Loading declarations…</p>}
    {error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(value => value + 1)}>Try again</button></div>}
    {list && (list.items.length ? <div className="table-scroll"><table><thead><tr><th>Date applied</th><th>Items</th><th>Recorded</th></tr></thead><tbody>{list.items.map(item => <tr key={item.id}><td><button className="text-button farmer-plot-link" onClick={() => onOpen(item.id)}>{item.appliedOn} ↗</button></td><td>{item.itemCount}</td><td>{new Date(item.recordedAt).toLocaleString()}</td></tr>)}</tbody></table></div> : <p className="empty">No pesticide declarations for this plot season.</p>)}
    {list && <div className="farmer-pagination"><span>Page {list.page}</span><button className="secondary" disabled={page === 1 || loading} onClick={() => setPage(p => p - 1)}>Previous</button><button className="secondary" disabled={!list.hasMore || loading} onClick={() => setPage(p => p + 1)}>Next</button></div>}
    {creating && <SprayCreate plotSeasonId={plotSeasonId} userId={userId} companyId={companyId} onCancel={() => setCreating(false)} onCreated={onOpen} />}
  </section>;
}

function SprayCreate({ plotSeasonId, userId, companyId, onCancel, onCreated }: { plotSeasonId: string; userId: string; companyId: string; onCancel: () => void; onCreated: (id: string) => void }) {
  const key = `grainledger:spray-create:${userId}:${companyId}:${plotSeasonId}`;
  const [pending, setPending] = useState<Envelope<SprayPayload> | null>(() => {
    const value = pendingRequest<SprayPayload>(key); return value?.payload.plotSeasonId === plotSeasonId ? value : null;
  });
  const [appliedOn, setAppliedOn] = useState(pending?.payload.appliedOn ?? '');
  const [source, setSource] = useState(pending?.payload.informationSource ?? '');
  const [evidence, setEvidence] = useState(pending?.payload.evidenceRef ?? '');
  const [items, setItems] = useState<{ productName: string; substanceId: string }[]>(pending?.payload.items.map(item => ({ productName: item.productName ?? '', substanceId: item.substanceId ?? '' })) ?? [{ productName: '', substanceId: '' }]);
  const [substances, setSubstances] = useState<Substance[]>([]);
  const [catalogError, setCatalogError] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    void (async () => {
      const found: Substance[] = [];
      for (let page = 1; page <= 100; page++) {
        const result = await api<SubstanceList>(`/api/active-substances?page=${page}&pageSize=100`, { signal: controller.signal });
        found.push(...result.items);
        if (!result.hasMore) return found;
      }
      throw new Error('Too many active substances to load.');
    })().then(value => { if (active) setSubstances(value); }).catch(cause => { if (active) setCatalogError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, []);
  function editItem(index: number, patch: Partial<{ productName: string; substanceId: string }>) { setItems(rows => rows.map((row, i) => i === index ? { ...row, ...patch } : row)); }
  async function save(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    if (!pending && (appliedOn > vietnamToday() || items.length === 0 || items.some(item => !item.productName.trim() && !item.substanceId))) {
      setError('Enter a valid application date and a product name or active substance for every item.'); return;
    }
    setBusy(true); setError('');
    const request = pending ?? newRequest<SprayPayload>({ entityId: crypto.randomUUID(), plotSeasonId, appliedOn,
      informationSource: source.trim(), evidenceRef: evidence.trim() || null,
      items: items.map(item => ({ productName: item.productName.trim() || null, substanceId: item.substanceId || null })) }, 2);
    if (!pending) setPending(request);
    try {
      const created = await submitCapture<SprayPayload, Spray>(key, '/api/spray-declarations', request);
      onCreated(created.id);
    } catch (cause) { if (wasRejected(cause)) setPending(null); setError(errorMessage(cause)); }
    finally { setBusy(false); }
  }
  return <div className="farmer-modal-backdrop"><section className="farmer-modal capture-modal-wide" role="dialog" aria-modal="true" aria-labelledby="spray-create-title" onKeyDown={event => { if (event.key === 'Escape' && !busy) onCancel(); }}>
    <div className="farmer-modal-heading"><div><span className="eyebrow">SOURCE EVIDENCE</span><h2 id="spray-create-title">Record pesticide application</h2></div><button className="farmer-modal-close" type="button" aria-label="Close declaration form" disabled={busy} onClick={onCancel}>×</button></div>
    <p className="muted">Record what the farmer reported. This declaration does not prove MRL compliance.</p>
    <form onSubmit={save}>
      <div className="form-columns"><label>Application date<input type="date" value={appliedOn} max={vietnamToday()} onChange={e => setAppliedOn(e.target.value)} required disabled={busy || !!pending} /></label><label>Information source<input value={source} onChange={e => setSource(e.target.value)} required disabled={busy || !!pending} placeholder="Farmer's notebook" /></label></div>
      <label>Evidence reference <span className="optional">(optional)</span><input value={evidence} onChange={e => setEvidence(e.target.value)} disabled={busy || !!pending} placeholder="Notebook page or document reference" /></label>
      <h3 className="capture-form-subheading">Declared products or substances</h3>
      {catalogError && <p className="muted small">Active-substance catalog unavailable. You can still enter a product name.</p>}
      {items.map((item, index) => <div className="capture-item-row" key={index}><div className="form-columns"><label>Product name <span className="optional">(or substance)</span><input value={item.productName} onChange={e => editItem(index, { productName: e.target.value })} disabled={busy || !!pending} /></label><label>Active substance <span className="optional">(optional)</span><select value={item.substanceId} onChange={e => editItem(index, { substanceId: e.target.value })} disabled={busy || !!pending}><option value="">Not specified</option>{substances.map(s => <option value={s.id} key={s.id}>{s.name}</option>)}</select></label></div>{items.length > 1 && !pending && <button className="text-button" type="button" onClick={() => setItems(rows => rows.filter((_, i) => i !== index))}>Remove item</button>}</div>)}
      {!pending && <button className="secondary" type="button" onClick={() => setItems(rows => [...rows, { productName: '', substanceId: '' }])}>+ Add item</button>}
      {pending && <div className="notice pending">The outcome is uncertain. Retry sends the exact same request.</div>}
      {error && <div className="notice error" role="alert">{error}</div>}
      <div className="form-footer"><button className="secondary" type="button" onClick={onCancel} disabled={busy}>Cancel</button><button type="submit" disabled={busy}>{busy ? 'Saving…' : pending ? 'Retry save' : 'Save declaration'}</button></div>
    </form>
  </section></div>;
}

export function SprayDetail({ id, onBack }: { id: string; onBack: (plotSeasonId: string | null) => void }) {
  const [record, setRecord] = useState<Spray | null>(null);
  const [error, setError] = useState('');
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true; const controller = new AbortController(); setRecord(null); setError('');
    void api<Spray>(`/api/spray-declarations/${encodeURIComponent(id)}`, { signal: controller.signal })
      .then(value => { if (active) setRecord(value); }).catch(cause => { if (active) setError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, [id, retry]);
  return <><div className="page-heading"><span className="eyebrow">SOURCE EVIDENCE</span><h1>Pesticide declaration</h1><p className="muted">Farmer-reported information, not a laboratory result.</p></div><button className="text-button" onClick={() => onBack(record?.plotSeasonId ?? null)}>← Back to plot season</button>
    {!record && !error && <p role="status">Loading declaration…</p>}{error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(n => n + 1)}>Try again</button></div>}
    {record && <section className="panel capture-detail"><h2>Applied on {record.appliedOn}</h2><dl><dt>Information source</dt><dd>{record.informationSource}</dd><dt>Evidence reference</dt><dd>{record.evidenceRef || 'Not provided'}</dd><dt>Captured</dt><dd>{new Date(record.capturedAt).toLocaleString()}</dd></dl><h3 className="capture-form-subheading">Declared items</h3><div className="table-scroll"><table><thead><tr><th>Product</th><th>Active substance ID</th></tr></thead><tbody>{record.items.map((item, index) => <tr key={item.id ?? index}><td>{item.productName || '—'}</td><td>{item.substanceId || '—'}</td></tr>)}</tbody></table></div></section>}
  </>;
}
