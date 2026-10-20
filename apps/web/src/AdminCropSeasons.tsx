import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage, type Organization } from './api';
import { type Page } from './procurementTypes';

type Season = { id: string; companyId: string; name: string; startsOn: string; endsOn: string; version: number; canEdit: boolean; canDelete: boolean };

export function AdminCropSeasons() {
  const [companies, setCompanies] = useState<Organization[]>([]);
  const [companyId, setCompanyId] = useState('');
  const [companyError, setCompanyError] = useState('');
  const [search, setSearch] = useState(''); const [query, setQuery] = useState('');
  const [sortBy, setSortBy] = useState('startsOn'); const [sortDirection, setSortDirection] = useState('desc');
  const [page, setPage] = useState(1); const [retry, setRetry] = useState(0);
  const [list, setList] = useState<Page<Season> | null>(null); const [loading, setLoading] = useState(false); const [error, setError] = useState('');
  const [editing, setEditing] = useState<Season | 'new' | null>(null);
  const [name, setName] = useState(''); const [startsOn, setStartsOn] = useState(''); const [endsOn, setEndsOn] = useState('');
  const [formError, setFormError] = useState(''); const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  useEffect(() => {
    let active = true; const controller = new AbortController();
    void api<Organization[]>('/api/admin/organizations', { signal: controller.signal })
      .then(value => { if (active) setCompanies(value.filter(item => item.kind === 'Customer')); })
      .catch(cause => { if (active) setCompanyError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, []);
  useEffect(() => {
    if (!companyId) { setList(null); return; }
    let active = true; const controller = new AbortController(); setLoading(true); setError(''); setList(null);
    const params = new URLSearchParams({ companyId, page: String(page), pageSize: '20', sortBy, sortDirection });
    if (query) params.set('search', query);
    void api<Page<Season>>(`/api/crop-seasons?${params}`, { signal: controller.signal })
      .then(value => { if (active) setList(value); })
      .catch(cause => { if (active) setError(errorMessage(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [companyId, query, page, sortBy, sortDirection, retry]);
  function openForm(season: Season | 'new') {
    setEditing(season); setName(season === 'new' ? '' : season.name); setStartsOn(season === 'new' ? '' : season.startsOn);
    setEndsOn(season === 'new' ? '' : season.endsOn); setFormError(''); setNotice('');
  }
  async function save(event: FormEvent) {
    event.preventDefault(); if (!editing || busy) return;
    if (endsOn < startsOn) { setFormError('End date must be on or after start date.'); return; }
    setBusy(true); setFormError('');
    try {
      if (editing === 'new') await api('/api/crop-seasons', { method: 'POST', body: JSON.stringify({ companyId, name: name.trim(), startsOn, endsOn }) });
      else await api(`/api/crop-seasons/${editing.id}`, { method: 'PUT', body: JSON.stringify({ companyId, name: name.trim(), startsOn, endsOn, expectedVersion: editing.version }) });
      setNotice(editing === 'new' ? 'Crop season created.' : 'Crop season updated.'); setEditing(null); setPage(1); setRetry(value => value + 1);
    } catch (cause) { setFormError(cause instanceof ApiError && cause.status === 412 ? 'This crop season changed. Close the form and reload the list before editing.' : errorMessage(cause)); }
    finally { setBusy(false); }
  }
  async function archive(season: Season) {
    if (!window.confirm(`Archive crop season “${season.name}”? Historical records remain available.`)) return;
    setBusy(true); setError(''); setNotice('');
    try {
      await api(`/api/crop-seasons/${season.id}`, { method: 'DELETE', body: JSON.stringify({ version: season.version }) });
      setNotice('Crop season archived.'); if (list?.items.length === 1 && page > 1) setPage(value => value - 1); else setRetry(value => value + 1);
    } catch (cause) { setError(cause instanceof ApiError && cause.status === 412 ? 'This crop season changed. Reload the list before archiving.' : errorMessage(cause)); }
    finally { setBusy(false); }
  }
  return <div><div className="page-heading"><span className="eyebrow">FIELD REFERENCE DATA</span><h1>Crop seasons</h1><p className="muted">Manage the seasons shared by each customer company.</p></div>
    <section className="panel admin-mrl-panel"><div className="section-toolbar"><div><h2>Company seasons</h2><p className="muted">Choose a company to view its crop seasons.</p></div>{companyId && <button type="button" onClick={() => openForm('new')}>+ Add crop season</button>}</div>
      {companyError && <div className="notice error" role="alert">{companyError}</div>}
      <div className="admin-form-grid"><label>Customer company<select value={companyId} onChange={event => { setCompanyId(event.target.value); setSearch(''); setQuery(''); setPage(1); setEditing(null); setNotice(''); }}><option value="">Select company</option>{companies.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        {companyId && <><label>Search seasons<input value={search} onChange={event => setSearch(event.target.value)} maxLength={100} /></label><label>Sort by<select value={sortBy} onChange={event => { setSortBy(event.target.value); setPage(1); }}><option value="startsOn">Start date</option><option value="name">Name</option></select></label><label>Direction<select value={sortDirection} onChange={event => { setSortDirection(event.target.value); setPage(1); }}><option value="desc">Descending</option><option value="asc">Ascending</option></select></label></>}
      </div>{companyId && <div className="admin-form-actions"><button className="secondary" type="button" onClick={() => { setQuery(search.trim()); setPage(1); }}>Search</button><button className="secondary" type="button" onClick={() => { setSearch(''); setQuery(''); setPage(1); }}>Clear</button></div>}
      {notice && <div className="notice" role="status">{notice}</div>}{loading && <p role="status">Loading crop seasons…</p>}{error && <div className="notice error" role="alert">{error} <button className="text-button" type="button" onClick={() => setRetry(value => value + 1)}>Try again</button></div>}
      {list && (list.items.length ? <div className="table-scroll"><table><thead><tr><th>Name</th><th>Starts on</th><th>Ends on</th><th>Actions</th></tr></thead><tbody>{list.items.map(item => <tr key={item.id}><td>{item.name}</td><td>{item.startsOn}</td><td>{item.endsOn}</td><td className="admin-row-actions">{item.canEdit && <button className="text-button" type="button" onClick={() => openForm(item)}>Edit</button>}{item.canDelete && <button className="text-button" type="button" disabled={busy} onClick={() => void archive(item)}>Archive</button>}{!item.canEdit && !item.canDelete && <span className="muted">Read only</span>}</td></tr>)}</tbody></table></div> : <p className="empty">No crop seasons found.</p>)}
      {list && <div className="farmer-pagination"><span>Page {list.page}</span><button className="secondary" type="button" disabled={page === 1 || loading} onClick={() => setPage(value => value - 1)}>Previous</button><button className="secondary" type="button" disabled={!list.hasMore || loading} onClick={() => setPage(value => value + 1)}>Next</button></div>}
    </section>
    {editing && <div className="farmer-modal-backdrop"><section className="farmer-modal" role="dialog" aria-modal="true" aria-label={editing === 'new' ? 'Add crop season' : 'Edit crop season'} onKeyDown={event => { if (event.key === 'Escape' && !busy) setEditing(null); }}><div className="farmer-modal-heading"><h2>{editing === 'new' ? 'Add crop season' : 'Edit crop season'}</h2><button className="farmer-modal-close" type="button" aria-label="Close" disabled={busy} onClick={() => setEditing(null)}>×</button></div><form onSubmit={save}><label>Name<input value={name} onChange={event => setName(event.target.value)} required maxLength={100} /></label><label>Starts on<input type="date" value={startsOn} onChange={event => setStartsOn(event.target.value)} required /></label><label>Ends on<input type="date" value={endsOn} onChange={event => setEndsOn(event.target.value)} required /></label>{formError && <div className="notice error" role="alert">{formError}</div>}<div className="admin-form-actions"><button className="secondary" type="button" disabled={busy} onClick={() => setEditing(null)}>Cancel</button><button disabled={busy || !name.trim() || !startsOn || !endsOn}>{busy ? 'Saving…' : 'Save crop season'}</button></div></form></section></div>}
  </div>;
}
