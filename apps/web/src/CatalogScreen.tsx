import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api } from './api';
import { Feedback, ListControls, Pagination, Table, useAction, type Page, type Row } from './ledger-ui';

const kinds = ['MASS', 'AREA', 'RESIDUE_CONCENTRATION', 'APPLICATION_RATE', 'FRACTION'];
type CatalogKind = 'commodities' | 'measurement-units';
type CatalogPage = Page<Row>;
const emptyPage: CatalogPage = { items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false };

export function CatalogScreen({ active = true, selectedKind }: { active?: boolean; selectedKind?: CatalogKind }) {
  const action = useAction();
  const requestId = useRef(0);
  const [internalKind, setInternalKind] = useState<CatalogKind>('commodities');
  const kind = selectedKind ?? internalKind;
  const [commodities, setCommodities] = useState<CatalogPage>(emptyPage);
  const [units, setUnits] = useState<CatalogPage>(emptyPage);
  const [loading, setLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [cq, setCq] = useState(''), [uq, setUq] = useState(''), [cp, setCp] = useState(1), [up, setUp] = useState(1);
  const [cs, setCs] = useState('code'), [us, setUs] = useState('code');
  const [cd, setCd] = useState<'asc' | 'desc'>('asc'), [ud, setUd] = useState<'asc' | 'desc'>('asc');
  const [code, setCode] = useState(''), [name, setName] = useState('');
  const [unitCode, setUnitCode] = useState(''), [unitName, setUnitName] = useState(''), [quantityKind, setQuantityKind] = useState('MASS');

  const commodity = kind === 'commodities';
  const data = commodity ? commodities : units;
  const label = commodity ? 'commodities' : 'measurement units';
  const page = commodity ? cp : up;
  const setPage = commodity ? setCp : setUp;
  const search = commodity ? cq : uq;
  const setSearch = commodity ? setCq : setUq;
  const sort = commodity ? cs : us;
  const setSort = commodity ? setCs : setUs;
  const direction = commodity ? cd : ud;
  const setDirection = commodity ? setCd : setUd;

  async function load() {
    const request = ++requestId.current;
    setLoading(true);
    const params = new URLSearchParams({ q: search, page: String(page), pageSize: '25', sortBy: sort, sortDirection: direction });
    try {
      const result = await api<CatalogPage>(`/api/${kind}?${params}`);
      if (request === requestId.current) (commodity ? setCommodities : setUnits)(result);
    } finally {
      if (request === requestId.current) setLoading(false);
    }
  }

  useEffect(() => {
    if (active) void action.run(load);
    return () => { requestId.current++; };
  }, [active, kind, cq, cp, cs, cd, uq, up, us, ud]);

  async function create(event: FormEvent) {
    event.preventDefault();
    await action.run(async () => {
      await api(`/api/${kind}`, { method: 'POST', body: JSON.stringify(commodity
        ? { code: code.trim(), name: name.trim() }
        : { code: unitCode.trim(), name: unitName.trim(), quantityKind }) });
      setCode(''); setName(''); setUnitCode(''); setUnitName(''); setQuantityKind('MASS');
      setCreateOpen(false);
      await load();
    }, 'Catalog record saved.');
  }

  async function update(row: Row) {
    const value = window.prompt(`New ${label} name`, row.name);
    if (value === null || value === row.name) return;
    await action.run(async () => {
      await api(`/api/${kind}/${row.id}`, { method: 'PATCH', headers: { 'If-Match': `"${row.version}"` }, body: JSON.stringify({ name: value }) });
      await load();
    }, 'Catalog record updated.');
  }

  async function remove(row: Row) {
    if (!window.confirm(`Remove ${row.code} from active ${label}? Stored record/history will be retained.`)) return;
    await action.run(async () => {
      await api(`/api/${kind}/${row.id}`, { method: 'DELETE', headers: { 'If-Match': `"${row.version}"` } });
      if (data.totalCount <= (page - 1) * data.pageSize + 1 && page > 1) setPage(page - 1);
      else await load();
    }, 'Catalog record removed.');
  }

  return <div className="catalog-workspace">
    {!selectedKind && <nav className="catalog-type-nav" aria-label="Commodity and unit catalogs">
      <button type="button" aria-current={commodity ? 'page' : undefined} onClick={() => { setInternalKind('commodities'); setCreateOpen(false); }}>Commodities</button>
      <button type="button" aria-current={!commodity ? 'page' : undefined} onClick={() => { setInternalKind('measurement-units'); setCreateOpen(false); }}>Measurement units</button>
    </nav>}
    <section className={`panel catalog-workspace-panel${selectedKind ? ' admin-catalog-panel' : ''}`} aria-label={commodity ? 'Commodity catalog' : 'Measurement units'}>
      <div className="section-toolbar catalog-workspace-heading">
        <div><h2>{commodity ? 'Commodity catalog' : 'Measurement units'}</h2><p className="muted">{commodity
          ? 'Canonical commodity names for stock, processing and MRL tables.'
          : 'Units and quantity kinds used across stock, field records and residue limits.'}</p></div>
        <button type="button" onClick={() => { action.setStatus(''); action.setError(''); setCreateOpen(true); }}>+ Add {commodity ? 'commodity' : 'unit'}</button>
      </div>
      {!createOpen && <Feedback {...action} />}
      <ListControls label={label} search={search} onSearch={value => { setSearch(value); setPage(1); }} sortBy={sort} onSortBy={value => { setSort(value); setPage(1); }} sortDirection={direction} onSortDirection={setDirection} sortOptions={[{ value: 'code', label: 'Code' }, { value: 'name', label: 'Name' }]} />
      <div className="catalog-list-viewport" aria-busy={loading}>
        {loading ? <div className="catalog-list-loading" role="status">Loading {label}…</div> : <>
          <Table rows={data.items} label={label} controls={false} columns={commodity ? ['code', 'name'] : ['code', 'name', 'quantityKind']} actions={row => row.canEdit || row.canDelete ? <div className="catalog-row-actions">
            {row.canEdit && <button type="button" className="secondary" disabled={action.busy} onClick={() => void update(row)}>Edit {row.name}</button>}
            {row.canDelete && <button type="button" className="secondary" disabled={action.busy} onClick={() => void remove(row)}>{commodity ? 'Remove' : 'Delete'} {row.name}</button>}
          </div> : null} />
          {data.items.some(row => row.createdBy === null) && <p className="muted small">Legacy catalog records are read-only.</p>}
        </>}
      </div>
      <Pagination label={label} page={page} pageSize={data.pageSize} totalCount={data.totalCount} onChange={setPage} busy={loading || action.busy} />
    </section>
    {createOpen && <div className="farmer-modal-backdrop" onKeyDown={event => { if (event.key === 'Escape' && !action.busy) setCreateOpen(false); }}>
      <section className="farmer-modal catalog-create-modal" role="dialog" aria-modal="true" aria-labelledby="catalog-create-title">
        <div className="farmer-modal-heading"><div><span className="eyebrow">REFERENCE DATA</span><h2 id="catalog-create-title">Add {commodity ? 'commodity' : 'measurement unit'}</h2></div><button className="farmer-modal-close" type="button" aria-label="Close" disabled={action.busy} onClick={() => setCreateOpen(false)}>×</button></div>
        <p className="muted">Code and {commodity ? 'commodity identity' : 'quantity kind'} cannot be changed after creation.</p>
        <Feedback {...action} />
        <form onSubmit={event => void create(event)}>
          <label>Code<input autoFocus required maxLength={commodity ? 40 : 30} value={commodity ? code : unitCode} onChange={event => (commodity ? setCode : setUnitCode)(event.target.value)} /></label>
          <label>Name<input required value={commodity ? name : unitName} onChange={event => (commodity ? setName : setUnitName)(event.target.value)} /></label>
          {!commodity && <label>Quantity kind<select value={quantityKind} onChange={event => setQuantityKind(event.target.value)}>{kinds.map(value => <option key={value}>{value}</option>)}</select></label>}
          <div className="form-footer"><button className="secondary" type="button" disabled={action.busy} onClick={() => setCreateOpen(false)}>Cancel</button><button disabled={action.busy}>{action.busy ? 'Saving…' : `Create ${commodity ? 'commodity' : 'measurement unit'}`}</button></div>
        </form>
      </section>
    </div>}
  </div>;
}
