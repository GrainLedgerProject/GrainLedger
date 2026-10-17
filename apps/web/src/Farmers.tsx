import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { FarmerCreate } from './FarmerCreate';
import { FarmerEdit } from './FarmerEdit';
import { PlotCreate } from './PlotCreate';

type Farmer = {
  id: string;
  code: string;
  name: string;
  contact: string | null;
  createdAt: string;
  canEdit?: boolean;
  canDelete?: boolean;
  version?: number;
};

type FarmerListResponse = {
  items: Farmer[];
  page: number;
  pageSize: number;
  hasMore: boolean;
};

type PlotSummary = { id: string; farmerId: string; code: string; areaHa: number | null };
type PlotListResponse = { items: PlotSummary[]; page: number; pageSize: number; hasMore: boolean };

export function Farmers({ selectedId, showCreateInitially, onSelect, onOpenPlot, canCreate, userId, companyId }: {
  selectedId: string | null;
  showCreateInitially?: boolean;
  onSelect: (id: string | null) => void;
  onOpenPlot: (id: string) => void;
  canCreate: boolean;
  userId: string;
  companyId: string;
}) {
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);
  const [retry, setRetry] = useState(0);
  const [list, setList] = useState<FarmerListResponse | null>(null);
  const [farmer, setFarmer] = useState<Farmer | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showCreate, setShowCreate] = useState(Boolean(showCreateInitially && canCreate));
  const [showPlotCreate, setShowPlotCreate] = useState(false);
  const [showFarmerEdit, setShowFarmerEdit] = useState(false);
  const [plotsPage, setPlotsPage] = useState(1);
  const [plotsRetry, setPlotsRetry] = useState(0);
  const [plots, setPlots] = useState<PlotListResponse | null>(null);
  const [plotsLoading, setPlotsLoading] = useState(false);
  const [plotsError, setPlotsError] = useState('');
  const [archiveTarget, setArchiveTarget] = useState<Farmer | null>(null);
  const [archiveError, setArchiveError] = useState('');
  const [archiving, setArchiving] = useState(false);

  useEffect(() => { if (showCreateInitially && canCreate) setShowCreate(true); }, [showCreateInitially, canCreate]);

  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    setLoading(true);
    setError('');
    if (selectedId) {
      setFarmer(null);
      void api<Farmer>(`/api/farmers/${encodeURIComponent(selectedId)}`, { signal: controller.signal })
        .then(value => { if (active) setFarmer(value); })
        .catch(cause => { if (active) setError(errorMessage(cause)); })
        .finally(() => { if (active) setLoading(false); });
    } else {
      setList(null);
      const params = new URLSearchParams({ page: String(page), pageSize: '20' });
      if (query) params.set('q', query);
      void api<FarmerListResponse>(`/api/farmers?${params}`, { signal: controller.signal })
        .then(value => { if (active) setList(value); })
        .catch(cause => { if (active) setError(errorMessage(cause)); })
        .finally(() => { if (active) setLoading(false); });
    }
    return () => { active = false; controller.abort(); };
  }, [selectedId, query, page, retry]);

  useEffect(() => {
    if (!selectedId) return;
    let active = true;
    const controller = new AbortController();
    setPlots(null);
    setPlotsLoading(true);
    setPlotsError('');
    const params = new URLSearchParams({ farmerId: selectedId, page: String(plotsPage), pageSize: '10' });
    void api<PlotListResponse>(`/api/plots?${params}`, { signal: controller.signal })
      .then(value => { if (active) setPlots(value); })
      .catch(cause => { if (active) setPlotsError(errorMessage(cause)); })
      .finally(() => { if (active) setPlotsLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [selectedId, plotsPage, plotsRetry]);

  function openFarmer(id: string) {
    setPlotsPage(1);
    onSelect(id);
  }

  async function archiveFarmer() {
    if (!archiveTarget?.version || archiving) return;
    setArchiving(true); setArchiveError('');
    try {
      await api(`/api/farmers/${archiveTarget.id}`, { method: 'DELETE', body: JSON.stringify({ version: archiveTarget.version }) });
      setArchiveTarget(null); setFarmer(null); onSelect(null); setPage(1); setRetry(value => value + 1);
    } catch (cause) {
      setArchiveError(cause instanceof ApiError && cause.status === 412 ? 'This farmer changed. Close this dialog and reload the record before archiving.' : errorMessage(cause));
    } finally { setArchiving(false); }
  }

  function submitSearch(event: FormEvent) {
    event.preventDefault();
    setPage(1);
    setQuery(search.trim());
  }

  if (selectedId) return <>
    <div className="page-heading"><span className="eyebrow">FIELD RECORDS</span><h1>Farmer record</h1><p className="muted">Source information recorded for your company.</p></div>
    <button className="text-button" onClick={() => onSelect(null)}>← Back to farmers</button>
    {loading && <p role="status">Loading farmer…</p>}
    {error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(value => value + 1)}>Try again</button></div>}
    {farmer && <section className="panel farmer-detail">
      <div className="farmer-detail-hero"><span className="farmer-monogram" aria-hidden="true">{farmer.name.slice(0, 1).toUpperCase()}</span><div><span className="eyebrow">REGISTERED FARMER</span><h2>{farmer.name}</h2><span className="farmer-code">{farmer.code}</span></div><div className="admin-form-actions">{farmer.canEdit && <button className="secondary farmer-edit-button" onClick={() => setShowFarmerEdit(true)}>Edit farmer</button>}{farmer.canDelete && <button className="secondary" onClick={() => { setArchiveTarget(farmer); setArchiveError(''); }}>Archive farmer</button>}</div></div>
      <div className="farmer-facts"><div><span>Contact</span><strong>{farmer.contact || 'Not provided'}</strong></div><div><span>Registered</span><strong>{new Date(farmer.createdAt).toLocaleString()}</strong></div></div>
    </section>}
    {farmer && <section className="panel farmer-plots">
      <div className="farmer-plots-heading"><div><span className="eyebrow">FIELD CONNECTIONS</span><h2>Plots</h2><p className="muted">Field records linked to this farmer.</p></div>{canCreate && <button onClick={() => setShowPlotCreate(true)}>+ Add plot</button>}</div>
      {plotsLoading && <p role="status">Loading plots…</p>}
      {plotsError && <div className="notice error" role="alert">{plotsError} <button className="text-button" onClick={() => setPlotsRetry(value => value + 1)}>Try again</button></div>}
      {plots && (plots.items.length === 0 ? <p className="empty">No plots linked to this farmer yet.</p> : <div className="table-scroll"><table><thead><tr><th scope="col">Plot code</th><th scope="col">Area</th></tr></thead><tbody>{plots.items.map(plot => <tr key={plot.id}><td><button className="text-button farmer-plot-link" onClick={() => onOpenPlot(plot.id)}>{plot.code} <span aria-hidden="true">↗</span></button></td><td>{plot.areaHa == null ? '—' : `${plot.areaHa} ha`}</td></tr>)}</tbody></table></div>)}
      {plots && <div className="farmer-pagination"><span>Page {plots.page}</span><button className="secondary" disabled={plotsPage === 1 || plotsLoading} onClick={() => setPlotsPage(value => value - 1)}>Previous</button><button className="secondary" disabled={!plots.hasMore || plotsLoading} onClick={() => setPlotsPage(value => value + 1)}>Next</button></div>}
    </section>}
    {farmer && showPlotCreate && canCreate && <PlotCreate farmerId={farmer.id} farmerName={farmer.name} userId={userId} companyId={companyId} onCancel={() => setShowPlotCreate(false)} onCreated={id => { setShowPlotCreate(false); onOpenPlot(id); }} />}
    {farmer && showFarmerEdit && <FarmerEdit id={farmer.id} onCancel={() => setShowFarmerEdit(false)} onUpdated={() => { setShowFarmerEdit(false); setRetry(value => value + 1); }} />}
    {archiveTarget && <div className="farmer-modal-backdrop"><section className="farmer-modal" role="dialog" aria-modal="true" aria-label="Archive farmer"><div className="farmer-modal-heading"><h2>Archive farmer</h2><button className="farmer-modal-close" type="button" disabled={archiving} aria-label="Close" onClick={() => setArchiveTarget(null)}>×</button></div><p>Archive <strong>{archiveTarget.name} · {archiveTarget.code}</strong>? The record will no longer appear in active lists. Historical records remain available.</p>{archiveError && <div className="notice error" role="alert">{archiveError}</div>}<div className="admin-form-actions"><button className="secondary" type="button" disabled={archiving} onClick={() => setArchiveTarget(null)}>Cancel</button><button type="button" disabled={archiving} onClick={() => void archiveFarmer()}>{archiving ? 'Archiving…' : 'Archive farmer'}</button></div></section></div>}
  </>;

  return <>
    <div className="page-heading farmer-page-heading"><div><span className="eyebrow">FIELD RECORDS</span><h1>Farmers</h1><p className="muted">Find and manage the farmers in your company's source records.</p></div>{canCreate && <button onClick={() => setShowCreate(true)}>+ Add farmer</button>}</div>
    <section className="panel">
      <div className="section-toolbar">
        <div><h2>Registered farmers</h2>{list && <p className="muted small">{list.items.length} shown on this page</p>}</div>
        <form className="farmer-search" onSubmit={submitSearch}>
          <label htmlFor="farmer-search">Search by name or code</label>
          <div className="farmer-search-controls"><input id="farmer-search" value={search} onChange={event => setSearch(event.target.value)} maxLength={100} /><button type="submit">Search</button></div>
        </form>
      </div>
      {loading && <p role="status">Loading farmers…</p>}
      {error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(value => value + 1)}>Try again</button></div>}
      {list && (list.items.length === 0 ? <p className="empty">No farmers found.</p> : <div className="table-scroll"><table>
        <thead><tr><th scope="col">Code</th><th scope="col">Farmer</th><th scope="col">Contact</th><th scope="col">Registered</th></tr></thead>
        <tbody>{list.items.map(item => <tr key={item.id}>
          <td><span className="farmer-code">{item.code}</span></td><td><button className="text-button farmer-link" onClick={() => openFarmer(item.id)}><span className="farmer-row-avatar" aria-hidden="true">{item.name.slice(0, 1).toUpperCase()}</span>{item.name}</button></td>
          <td>{item.contact || '—'}</td><td>{new Date(item.createdAt).toLocaleDateString()}</td>
        </tr>)}</tbody>
      </table></div>)}
      {list && <div className="farmer-pagination"><span>Page {list.page}</span><button className="secondary" disabled={page === 1 || loading} onClick={() => setPage(value => value - 1)}>Previous</button><button className="secondary" disabled={!list.hasMore || loading} onClick={() => setPage(value => value + 1)}>Next</button></div>}
    </section>
    {showCreate && canCreate && <FarmerCreate userId={userId} companyId={companyId} onCancel={() => { setShowCreate(false); if (showCreateInitially) onSelect(null); }} onCreated={id => { setShowCreate(false); openFarmer(id); }} />}
  </>;
}
