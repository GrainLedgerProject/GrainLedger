import { useEffect, useState, type FormEvent } from 'react';
import { api, apiWithEtag, errorMessage } from './api';
import { CatalogScreen } from './CatalogScreen';

type CatalogKind = 'active-substances' | 'markets' | 'rice-varieties' | 'quality-attributes' | 'commodities' | 'measurement-units';
type CatalogRow = { id: string; code: string; name: string; unit?: string };
type CatalogPage = { items: CatalogRow[]; page: number; pageSize: number; hasMore: boolean };
const definitions = {
  'active-substances': { title: 'Active substances', singular: 'active substance', maxCode: 60, canDelete: true },
  markets: { title: 'Export markets', singular: 'market', maxCode: 40, canDelete: false },
  'rice-varieties': { title: 'Rice varieties', singular: 'rice variety', maxCode: 40, canDelete: false },
  'quality-attributes': { title: 'Quality attributes', singular: 'quality attribute', maxCode: 60, canDelete: false },
} as const;

export function AdminCatalogs({ canManageResidueRules = false }: { canManageResidueRules?: boolean }) {
  const [kind, setKind] = useState<CatalogKind>('active-substances');
  return <div className="admin-catalogs">
    <div className="page-heading"><span className="eyebrow">REFERENCE DATA</span><h1>Catalogs</h1><p className="muted">Maintain the shared values used by field records and MRL tables.</p></div>
    <nav className="admin-subnav" aria-label="Catalog types">
      {Object.entries(definitions).map(([key, value]) => <button key={key} type="button" aria-current={kind === key ? 'page' : undefined} onClick={() => setKind(key as CatalogKind)}>{value.title}</button>)}
      <button type="button" aria-current={kind === 'commodities' ? 'page' : undefined} onClick={() => setKind('commodities')}>Commodities</button>
      <button type="button" aria-current={kind === 'measurement-units' ? 'page' : undefined} onClick={() => setKind('measurement-units')}>Measurement units</button>
    </nav>
    {kind === 'commodities' || kind === 'measurement-units'
      ? <CatalogScreen key={kind} selectedKind={kind} canManageResidueRules={canManageResidueRules} />
      : <CatalogEditor key={kind} kind={kind} />}
  </div>;
}

function CatalogEditor({ kind }: { kind: Exclude<CatalogKind, 'commodities' | 'measurement-units'> }) {
  const definition = definitions[kind];
  const [list, setList] = useState<CatalogPage | null>(null);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [reload, setReload] = useState(0);
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [unit, setUnit] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<{ row: CatalogRow; name: string; etag: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  useEffect(() => {
    let active = true;
    setLoading(true); setError('');
    const params = new URLSearchParams({ q: query, page: String(page), pageSize: '20' });
    void api<CatalogPage>(`/api/${kind}?${params}`)
      .then(result => { if (active) setList(result); })
      .catch(cause => { if (active) setError(errorMessage(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [kind, page, query, reload]);

  async function create(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError(''); setNotice('');
    try {
      await api(`/api/${kind}`, { method: 'POST', body: JSON.stringify({ code: code.trim(), name: name.trim(), ...(kind === 'quality-attributes' ? { unit: unit.trim() } : {}) }) });
      setCode(''); setName(''); setUnit(''); setPage(1); setReload(n => n + 1);
      setCreateOpen(false);
      setNotice(`${definition.singular} created.`);
    } catch (cause) { setError(errorMessage(cause)); }
    finally { setBusy(false); }
  }

  async function startEdit(row: CatalogRow) {
    setBusy(true); setError(''); setNotice('');
    try {
      const { data, etag } = await apiWithEtag<CatalogRow>(`/api/${kind}/${row.id}`);
      if (!etag) throw new Error('The server did not return an ETag. Reload and try again.');
      setEditing({ row: data, name: data.name, etag });
    } catch (cause) { setError(errorMessage(cause)); }
    finally { setBusy(false); }
  }

  async function saveEdit(event: FormEvent) {
    event.preventDefault(); if (!editing) return;
    setBusy(true); setError(''); setNotice('');
    try {
      await api(`/api/${kind}/${editing.row.id}`, { method: 'PATCH', headers: { 'If-Match': editing.etag }, body: JSON.stringify({ name: editing.name.trim() }) });
      setEditing(null); setReload(n => n + 1); setNotice('Name updated.');
    } catch (cause) { setError(errorMessage(cause)); }
    finally { setBusy(false); }
  }

  async function remove(row: CatalogRow) {
    if (!window.confirm(`Delete ${row.code}? This is allowed only while it is unused.`)) return;
    setBusy(true); setError(''); setNotice('');
    try {
      const { etag } = await apiWithEtag<CatalogRow>(`/api/${kind}/${row.id}`);
      if (!etag) throw new Error('The server did not return an ETag. Reload and try again.');
      await api(`/api/${kind}/${row.id}`, { method: 'DELETE', headers: { 'If-Match': etag } });
      setReload(n => n + 1); setNotice('Active substance deleted.');
    } catch (cause) { setError(errorMessage(cause)); }
    finally { setBusy(false); }
  }

  return <><section className="panel admin-catalog-panel">
    <div className="section-toolbar"><div><h2>{definition.title}</h2><p className="muted">Codes remain fixed after creation. Edit a name if its description needs correction.</p></div><button type="button" onClick={() => { setError(''); setNotice(''); setCreateOpen(true); }}>+ Add {definition.singular}</button></div>
    <form className="admin-search" onSubmit={event => { event.preventDefault(); setPage(1); setQuery(search.trim()); }}>
      <label>Search by code or name<input type="search" value={search} onChange={event => setSearch(event.target.value)} /></label><button className="secondary" type="submit">Search</button>
    </form>
    {!createOpen && error && <div className="notice error" role="alert">{error}</div>}{notice && <div className="notice" role="status">{notice}</div>}
    <div className="admin-catalog-results" aria-busy={loading}>{loading ? <div className="catalog-list-loading" role="status">Loading {definition.title.toLowerCase()}…</div> : list && <>
      <div className="table-scroll"><table><thead><tr><th>Code</th><th>Name</th>{kind === 'quality-attributes' && <th>Unit</th>}<th><span className="sr-only">Actions</span></th></tr></thead><tbody>
        {list.items.map(row => <tr key={row.id}><td><strong>{row.code}</strong></td><td>{row.name}</td>{kind === 'quality-attributes' && <td>{row.unit}</td>}<td className="admin-row-actions"><button className="text-button" type="button" disabled={busy} onClick={() => void startEdit(row)}>Edit</button>{definition.canDelete && <button className="text-button" type="button" disabled={busy} onClick={() => void remove(row)}>Delete</button>}</td></tr>)}
      </tbody></table>{list.items.length === 0 && <p className="empty">No records found.</p>}</div>
    </>}</div>
    <div className="farmer-pagination"><span>Page {loading ? page : list?.page ?? page}</span><button className="secondary" type="button" disabled={loading || page <= 1} onClick={() => setPage(p => p - 1)}>Previous</button><button className="secondary" type="button" disabled={loading || !list?.hasMore} onClick={() => setPage(p => p + 1)}>Next</button></div>
    {editing && <form className="admin-inline-form" onSubmit={saveEdit}><h3>Edit {editing.row.code}</h3><label>Name<input required value={editing.name} onChange={event => setEditing({ ...editing, name: event.target.value })} disabled={busy} /></label><div className="admin-form-actions"><button className="secondary" type="button" onClick={() => setEditing(null)} disabled={busy}>Cancel</button><button disabled={busy || !editing.name.trim()}>{busy ? 'Saving…' : 'Save name'}</button></div></form>}
  </section>
    {createOpen && <div className="farmer-modal-backdrop" onKeyDown={event => { if (event.key === 'Escape' && !busy) setCreateOpen(false); }}>
      <section className="farmer-modal catalog-create-modal" role="dialog" aria-modal="true" aria-labelledby="catalog-entry-title">
        <div className="farmer-modal-heading"><div><span className="eyebrow">REFERENCE DATA</span><h2 id="catalog-entry-title">Add {definition.singular}</h2></div><button className="farmer-modal-close" type="button" aria-label="Close" disabled={busy} onClick={() => setCreateOpen(false)}>×</button></div>
        <p className="muted">Choose a unique code. Only the name can be changed after creation.</p>
        {error && <div className="notice error" role="alert">{error}</div>}
        <form onSubmit={create}>
          <label>Code<input autoFocus value={code} onChange={event => setCode(event.target.value)} maxLength={definition.maxCode} required disabled={busy} /></label>
          <label>Name<input value={name} onChange={event => setName(event.target.value)} required disabled={busy} /></label>
          {kind === 'quality-attributes' && <label>Unit<input value={unit} onChange={event => setUnit(event.target.value)} maxLength={30} required disabled={busy} /></label>}
          <div className="form-footer"><button className="secondary" type="button" onClick={() => setCreateOpen(false)} disabled={busy}>Cancel</button><button disabled={busy || !code.trim() || !name.trim() || kind === 'quality-attributes' && !unit.trim()}>{busy ? 'Saving…' : `Add ${definition.singular}`}</button></div>
        </form>
      </section>
    </div>}
  </>;
}
