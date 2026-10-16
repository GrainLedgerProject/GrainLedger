import { useEffect, useState, type FormEvent } from 'react';
import { api, apiWithEtag, ApiError, errorMessage } from './api';
import { loadAllPages, type Page } from './procurementTypes';

type Option = { id: string; code: string; name: string };
type Unit = Option & { quantityKind: string };
type MrlVersion = { id: string; marketId: string; commodityId: string; versionNo: number; status: 'DRAFT' | 'PUBLISHED'; effectiveFrom: string; effectiveTo: string | null; publishedBy: string | null; publishedAt: string | null; sourceRef: string };
type MrlLimit = { id: string; mrlVersionId: string; substanceId: string; limitValue: number; unit: string };
type Feedback = { scope: 'list' | 'create' | 'details' | 'limits' | 'publish'; kind: 'error' | 'success'; message: string };
const limitNumber = /^(?:0|[1-9]\d{0,11})(?:\.\d{1,12})?$/;
function localDateTime(value: string) { const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16); }
function displayDate(value: string | null) { return value ? new Date(value).toLocaleString() : 'Open-ended'; }
function label(options: Option[], id: string) { const item = options.find(option => option.id === id); return item ? `${item.code} · ${item.name}` : id; }

export function AdminMrl({ onOpenCatalogs }: { onOpenCatalogs: () => void }) {
  const [markets, setMarkets] = useState<Option[]>([]);
  const [commodities, setCommodities] = useState<Option[]>([]);
  const [substances, setSubstances] = useState<Option[]>([]);
  const [unitReady, setUnitReady] = useState(false);
  const [optionsError, setOptionsError] = useState('');
  const [optionsRetry, setOptionsRetry] = useState(0);
  const [versions, setVersions] = useState<Page<MrlVersion> | null>(null);
  const [page, setPage] = useState(1);
  const [marketFilter, setMarketFilter] = useState('');
  const [commodityFilter, setCommodityFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');
  const [listRetry, setListRetry] = useState(0);
  const [selectedId, setSelectedId] = useState('');
  const [selected, setSelected] = useState<MrlVersion | null>(null);
  const [etag, setEtag] = useState<string | null>(null);
  const [limits, setLimits] = useState<Page<MrlLimit> | null>(null);
  const [limitPage, setLimitPage] = useState(1);
  const [detailRetry, setDetailRetry] = useState(0);
  const [marketId, setMarketId] = useState('');
  const [commodityId, setCommodityId] = useState('');
  const [effectiveFrom, setEffectiveFrom] = useState('');
  const [effectiveTo, setEffectiveTo] = useState('');
  const [sourceRef, setSourceRef] = useState('');
  const [editFrom, setEditFrom] = useState('');
  const [editTo, setEditTo] = useState('');
  const [editSource, setEditSource] = useState('');
  const [substanceId, setSubstanceId] = useState('');
  const [limitValue, setLimitValue] = useState('');
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [editingDetails, setEditingDetails] = useState(false);
  const [limitFormOpen, setLimitFormOpen] = useState(false);

  useEffect(() => {
    let active = true;
    setOptionsError('');
    void Promise.all([
      loadAllPages(page => api<Page<Option>>(`/api/markets?page=${page}&pageSize=100`)),
      loadAllPages(page => api<Page<Option>>(`/api/commodities?page=${page}&pageSize=100`)),
      loadAllPages(page => api<Page<Option>>(`/api/active-substances?page=${page}&pageSize=100`)),
      loadAllPages(page => api<Page<Unit>>(`/api/measurement-units?q=MG%2FKG&page=${page}&pageSize=100`)),
    ]).then(([nextMarkets, nextCommodities, nextSubstances, units]) => {
      if (active) { setMarkets(nextMarkets); setCommodities(nextCommodities); setSubstances(nextSubstances); setUnitReady(units.some(unit => unit.code === 'MG/KG' && unit.quantityKind === 'RESIDUE_CONCENTRATION')); }
    }).catch(cause => { if (active) setOptionsError(errorMessage(cause)); });
    return () => { active = false; };
  }, [optionsRetry]);

  useEffect(() => {
    if (selectedId) return;
    let active = true;
    const params = new URLSearchParams({ page: String(page), pageSize: '20' });
    if (marketFilter) params.set('marketId', marketFilter);
    if (commodityFilter) params.set('commodityId', commodityFilter);
    if (statusFilter) params.set('status', statusFilter);
    void api<Page<MrlVersion>>(`/api/mrl-versions?${params}`)
      .then(result => { if (active) setVersions(result); })
      .catch(cause => { if (active) setFeedback({ scope: 'list', kind: 'error', message: errorMessage(cause) }); });
    return () => { active = false; };
  }, [page, marketFilter, commodityFilter, statusFilter, listRetry, selectedId]);

  useEffect(() => {
    if (!selectedId) { setSelected(null); setLimits(null); setEtag(null); return; }
    let active = true;
    void Promise.all([
      apiWithEtag<MrlVersion>(`/api/mrl-versions/${selectedId}`),
      api<Page<MrlLimit>>(`/api/mrl-versions/${selectedId}/limits?page=${limitPage}&pageSize=20`),
    ]).then(([version, nextLimits]) => {
      if (!active) return;
      setSelected(version.data); setEtag(version.etag); setLimits(nextLimits);
      setEditFrom(localDateTime(version.data.effectiveFrom));
      setEditTo(version.data.effectiveTo ? localDateTime(version.data.effectiveTo) : '');
      setEditSource(version.data.sourceRef);
    }).catch(cause => { if (active) setFeedback({ scope: 'details', kind: 'error', message: errorMessage(cause) }); });
    return () => { active = false; };
  }, [selectedId, limitPage, detailRetry]);

  function refresh() { setEtag(null); setDetailRetry(n => n + 1); setListRetry(n => n + 1); }
  function showFeedback(scope: Feedback['scope']) {
    return feedback?.scope === scope && <div className={`notice ${feedback.kind === 'error' ? 'error' : ''}`} role={feedback.kind === 'error' ? 'alert' : 'status'}>{feedback.message}</div>;
  }
  async function mutate(action: () => Promise<unknown>, success: string, scope: Feedback['scope'], successScope = scope) {
    setBusy(true); setFeedback(null);
    try { await action(); refresh(); setFeedback({ scope: successScope, kind: 'success', message: success }); }
    catch (cause) {
      if (cause instanceof ApiError && cause.status === 412) { refresh(); setFeedback({ scope, kind: 'error', message: 'This MRL version changed. The latest version has been loaded; review it before trying again.' }); }
      else setFeedback({ scope, kind: 'error', message: errorMessage(cause) });
    }
    finally { setBusy(false); }
  }

  function create(event: FormEvent) {
    event.preventDefault();
    if (effectiveTo && new Date(effectiveTo) <= new Date(effectiveFrom)) { setFeedback({ scope: 'create', kind: 'error', message: 'Effective end must be after the start.' }); return; }
    void mutate(async () => {
      const draft = await api<MrlVersion>('/api/mrl-versions', { method: 'POST', body: JSON.stringify({ marketId, commodityId, effectiveFrom: new Date(effectiveFrom).toISOString(), effectiveTo: effectiveTo ? new Date(effectiveTo).toISOString() : null, sourceRef: sourceRef.trim() }) });
      setSelectedId(draft.id); setLimitPage(1); setSourceRef('');
      setCreateOpen(false);
    }, 'Draft created. Add verified limits before publishing.', 'create', 'details');
  }

  function saveHeader(event: FormEvent) {
    event.preventDefault(); if (!selected || !etag) return;
    if (editTo && new Date(editTo) <= new Date(editFrom)) { setFeedback({ scope: 'details', kind: 'error', message: 'Effective end must be after the start.' }); return; }
    void mutate(async () => {
      await api(`/api/mrl-versions/${selected.id}`, { method: 'PATCH', headers: { 'If-Match': etag }, body: JSON.stringify({ effectiveFrom: new Date(editFrom).toISOString(), effectiveTo: editTo ? new Date(editTo).toISOString() : null, sourceRef: editSource.trim() }) });
      setEditingDetails(false);
    }, 'Draft updated.', 'details');
  }

  function saveLimit(event: FormEvent) {
    event.preventDefault(); if (!selected || !etag || !substanceId) return;
    if (!limitNumber.test(limitValue)) { setFeedback({ scope: 'limits', kind: 'error', message: 'Limit must be nonnegative, with at most 12 digits before and after the decimal point.' }); return; }
    void mutate(async () => {
      await api(`/api/mrl-versions/${selected.id}/limits/${substanceId}`, { method: 'PUT', headers: { 'If-Match': etag }, body: JSON.stringify({ limitValue: Number(limitValue), unit: 'mg/kg' }) });
      setSubstanceId(''); setLimitValue(''); setLimitPage(1); setLimitFormOpen(false);
    }, 'Limit saved.', 'limits');
  }

  function deleteLimit(limit: MrlLimit) {
    if (!selected || !etag || !window.confirm(`Remove the draft limit for ${label(substances, limit.substanceId)}?`)) return;
    void mutate(async () => { await api(`/api/mrl-versions/${selected.id}/limits/${limit.substanceId}`, { method: 'DELETE', headers: { 'If-Match': etag } }); setLimitPage(1); }, 'Draft limit removed.', 'limits');
  }

  function publish() {
    if (!selected || !etag || editingDetails || limitFormOpen || !window.confirm(`Publish MRL version ${selected.versionNo} for ${label(markets, selected.marketId)}? Its limits cannot be edited after publication. Confirm the source and all legal values first.`)) return;
    void mutate(() => api(`/api/mrl-versions/${selected.id}/publish`, { method: 'POST', headers: { 'If-Match': etag } }), 'MRL version published.', 'publish', 'details');
  }

  const draft = selected?.status === 'DRAFT';
  return <div className="admin-mrl">
    <div className="page-heading"><span className="eyebrow">RESIDUE RULES</span><h1>MRL tables</h1><p className="muted">Create versions for each export market and commodity. Only published versions can be used for compliance decisions.</p></div>
    {!selectedId && <section className="panel admin-mrl-panel" aria-label="MRL versions"><div className="section-toolbar"><div><h2>Versions</h2><p className="muted">Select a version to review its legal source and substance limits.</p></div><button type="button" onClick={() => { setFeedback(null); setCreateOpen(true); }}>+ Create draft</button></div>
      {showFeedback('list')}
      {optionsError && <div className="notice error" role="alert">Catalogs could not be loaded: {optionsError} <button className="text-button" onClick={() => setOptionsRetry(n => n + 1)}>Try again</button></div>}
      <div className="admin-form-grid"><label>Market<select value={marketFilter} onChange={event => { setPage(1); setMarketFilter(event.target.value); }}><option value="">All markets</option>{markets.map(item => <option value={item.id} key={item.id}>{item.code} · {item.name}</option>)}</select></label><label>Commodity<select value={commodityFilter} onChange={event => { setPage(1); setCommodityFilter(event.target.value); }}><option value="">All commodities</option>{commodities.map(item => <option value={item.id} key={item.id}>{item.code} · {item.name}</option>)}</select></label><label>Status<select value={statusFilter} onChange={event => { setPage(1); setStatusFilter(event.target.value); }}><option value="">All statuses</option><option value="DRAFT">Draft</option><option value="PUBLISHED">Published</option></select></label></div>
      {versions && <><div className="table-scroll"><table><thead><tr><th>Market</th><th>Commodity</th><th>Version</th><th>Status</th><th>Effective from</th><th><span className="sr-only">Open</span></th></tr></thead><tbody>{versions.items.map(item => <tr key={item.id}><td>{label(markets, item.marketId)}</td><td>{label(commodities, item.commodityId)}</td><td>v{item.versionNo}</td><td><span className={`badge ${item.status === 'DRAFT' ? 'inactive' : ''}`}>{item.status}</span></td><td>{displayDate(item.effectiveFrom)}</td><td><button className="text-button" type="button" onClick={() => { setSelected(null); setLimits(null); setEtag(null); setSelectedId(item.id); setLimitPage(1); setFeedback(null); }}>Open</button></td></tr>)}</tbody></table>{versions.items.length === 0 && <p className="empty">No MRL versions found.</p>}</div><div className="farmer-pagination"><span>Page {versions.page}</span><button className="secondary" type="button" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>Previous</button><button className="secondary" type="button" disabled={!versions.hasMore} onClick={() => setPage(p => p + 1)}>Next</button></div></>}
    </section>}
    {!selectedId && createOpen && <div className="farmer-modal-backdrop"><div className="farmer-modal capture-modal-wide" role="dialog" aria-modal="true" aria-label="Create draft"><div className="farmer-modal-heading"><div><span className="eyebrow">MRL VERSION</span><h2>Create draft</h2></div><button className="farmer-modal-close" type="button" aria-label="Close" onClick={() => { setCreateOpen(false); setFeedback(null); }}>×</button></div><p className="muted">Enter the market, commodity and verified regulatory source. Limits can be added after creation.</p>{showFeedback('create')}{optionsError && <div className="notice error" role="alert">Catalogs could not be loaded: {optionsError}</div>}<form onSubmit={create}>
      <div className="admin-form-grid"><label>Market<select value={marketId} onChange={event => setMarketId(event.target.value)} required><option value="">Select market</option>{markets.map(item => <option value={item.id} key={item.id}>{item.code} · {item.name}</option>)}</select></label><label>Commodity<select value={commodityId} onChange={event => setCommodityId(event.target.value)} required><option value="">Select commodity</option>{commodities.map(item => <option value={item.id} key={item.id}>{item.code} · {item.name}</option>)}</select></label><label>Effective from<input type="datetime-local" value={effectiveFrom} onChange={event => setEffectiveFrom(event.target.value)} required /></label><label>Effective to <span className="optional">(optional)</span><input type="datetime-local" value={effectiveTo} onChange={event => setEffectiveTo(event.target.value)} /></label></div>
      <label>Regulatory source reference<textarea value={sourceRef} onChange={event => setSourceRef(event.target.value)} required placeholder="Document title, publication date and applicable clause" /></label><div className="admin-form-actions"><button className="secondary" type="button" disabled={busy} onClick={() => { setCreateOpen(false); setFeedback(null); }}>Cancel</button><button disabled={busy || !marketId || !commodityId || !effectiveFrom || !sourceRef.trim()}>{busy ? 'Saving…' : 'Create draft'}</button></div>
    </form></div></div>}
    {selectedId && <><button className="text-button admin-mrl-back" type="button" onClick={() => { setSelectedId(''); setEditingDetails(false); setLimitFormOpen(false); setFeedback(null); }}>← Back to versions</button>
    {!selected && <section className="panel admin-mrl-panel" aria-label="Selected MRL version">{showFeedback('details') || <p className="muted">Loading version…</p>}</section>}
    {selected && <><section className="panel admin-mrl-panel" aria-label="Selected MRL version"><div className="section-toolbar"><div><span className="eyebrow">{selected.status} · VERSION {selected.versionNo}</span><h2>{label(markets, selected.marketId)} / {label(commodities, selected.commodityId)}</h2></div><div className="admin-form-actions"><button className="secondary" type="button" onClick={() => { setFeedback(null); refresh(); }}>Refresh</button>{draft && !editingDetails && <button className="secondary" type="button" onClick={() => { setEditingDetails(true); setFeedback(null); }}>Edit details</button>}</div></div>
      {showFeedback('details')}
      <div className="admin-mrl-facts"><div><span>Effective from</span><strong>{displayDate(selected.effectiveFrom)}</strong></div><div><span>Effective to</span><strong>{displayDate(selected.effectiveTo)}</strong></div><div><span>Regulatory source</span><strong>{selected.sourceRef}</strong></div></div>{selected.publishedAt && <p className="muted">Published {displayDate(selected.publishedAt)}.</p>}
      {draft && editingDetails && <form className="admin-inline-form" onSubmit={saveHeader}><h3>Edit draft details</h3><div className="admin-form-grid"><label>Effective from<input type="datetime-local" value={editFrom} onChange={event => setEditFrom(event.target.value)} required /></label><label>Effective to <span className="optional">(optional)</span><input type="datetime-local" value={editTo} onChange={event => setEditTo(event.target.value)} /></label></div><label>Regulatory source reference<textarea value={editSource} onChange={event => setEditSource(event.target.value)} required /></label><div className="admin-form-actions"><button className="secondary" type="button" disabled={busy} onClick={() => { setEditingDetails(false); setEditFrom(localDateTime(selected.effectiveFrom)); setEditTo(selected.effectiveTo ? localDateTime(selected.effectiveTo) : ''); setEditSource(selected.sourceRef); setFeedback(null); }}>Cancel</button><button disabled={busy || !etag || !editSource.trim()}>{busy ? 'Saving…' : 'Save draft details'}</button></div></form>}
    </section>
    <section className="panel admin-mrl-panel" aria-label="Substance limits"><div className="section-toolbar"><div><h2>Substance limits</h2><p className="muted">Values in mg/kg</p></div>{draft && unitReady && !limitFormOpen && <button type="button" onClick={() => { setSubstanceId(''); setLimitValue(''); setLimitFormOpen(true); setFeedback(null); }}>+ Add limit</button>}</div>
        {showFeedback('limits')}
        {optionsError && <div className="notice error" role="alert">Catalogs could not be loaded: {optionsError} <button className="text-button" onClick={() => setOptionsRetry(n => n + 1)}>Try again</button></div>}
        {limits && <><div className="table-scroll"><table><thead><tr><th>Active substance</th><th>Limit</th><th>Unit</th>{draft && <th><span className="sr-only">Actions</span></th>}</tr></thead><tbody>{limits.items.map(item => <tr key={item.id}><td>{label(substances, item.substanceId)}</td><td>{item.limitValue}</td><td>{item.unit}</td>{draft && <td className="admin-row-actions"><button className="text-button" type="button" disabled={busy} onClick={() => { setSubstanceId(item.substanceId); setLimitValue(String(item.limitValue)); setLimitFormOpen(true); setFeedback(null); }}>Edit</button><button className="text-button" type="button" disabled={busy} onClick={() => deleteLimit(item)}>Remove</button></td>}</tr>)}</tbody></table>{limits.items.length === 0 && <p className="empty">No limits in this version yet.</p>}</div><div className="farmer-pagination"><span>Page {limits.page}</span><button className="secondary" type="button" disabled={limitPage <= 1} onClick={() => setLimitPage(p => p - 1)}>Previous</button><button className="secondary" type="button" disabled={!limits.hasMore} onClick={() => setLimitPage(p => p + 1)}>Next</button></div></>}
        {draft && (unitReady ? limitFormOpen && <form onSubmit={saveLimit} className="admin-inline-form"><h3>{limits?.items.some(item => item.substanceId === substanceId) ? 'Edit limit' : 'Add limit'}</h3><div className="admin-form-grid"><label>Active substance<select value={substanceId} onChange={event => setSubstanceId(event.target.value)} required><option value="">Select substance</option>{substances.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label><label>Limit (mg/kg)<input inputMode="decimal" value={limitValue} onChange={event => setLimitValue(event.target.value)} placeholder="Enter verified legal value" required /></label></div><div className="admin-form-actions"><button className="secondary" type="button" disabled={busy} onClick={() => { setLimitFormOpen(false); setSubstanceId(''); setLimitValue(''); setFeedback(null); }}>Cancel</button><button disabled={busy || !etag || !substanceId || !limitValue}>{busy ? 'Saving…' : 'Save limit'}</button></div></form> : <div className="notice">Configure the MG/KG residue concentration unit before adding limits. <button className="text-button" type="button" onClick={onOpenCatalogs}>Open catalogs</button> <button className="text-button" type="button" onClick={() => setOptionsRetry(n => n + 1)}>Refresh units</button></div>)}
    </section>
    {draft && <section className="panel admin-mrl-panel" aria-label="Publish version"><h2>Publish version</h2>{showFeedback('publish')}<p className="muted">Publishing locks this version and makes it available to compliance decisions. It uses the saved dates and limits shown above.</p>{(editingDetails || limitFormOpen) && <p className="muted">Save or cancel your open changes before publishing.</p>}<button type="button" disabled={busy || !etag || !limits || editingDetails || limitFormOpen} onClick={publish}>Publish version</button></section>}
    </>}
    </>}
  </div>;
}
