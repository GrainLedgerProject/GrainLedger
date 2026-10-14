import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api } from './api';
import { Feedback, ListControls, Pagination, Panel, Table, useAction, type Page, type Row } from './ledger-ui';

const kinds = ['MASS', 'AREA', 'RESIDUE_CONCENTRATION', 'APPLICATION_RATE', 'FRACTION'];
type CatalogPage = Page<Row>;
export function CatalogScreen({ active = true }: { active?: boolean }) {
  const action = useAction();
  const requestId = useRef(0);
  const [commodities, setCommodities] = useState<CatalogPage>({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
  const [units, setUnits] = useState<CatalogPage>({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
  const [cq, setCq] = useState(''), [uq, setUq] = useState(''), [cp, setCp] = useState(1), [up, setUp] = useState(1);
  const [cs, setCs] = useState('code'), [us, setUs] = useState('code'), [cd, setCd] = useState<'asc'|'desc'>('asc'), [ud, setUd] = useState<'asc'|'desc'>('asc');
  const [code, setCode] = useState(''), [name, setName] = useState(''), [unitCode, setUnitCode] = useState(''), [unitName, setUnitName] = useState(''), [quantityKind, setQuantityKind] = useState('MASS');
  async function load() {
    const request = ++requestId.current;
    const query = (q: string, page: number, sortBy: string, sortDirection: string) => `q=${encodeURIComponent(q)}&page=${page}&pageSize=25&sortBy=${sortBy}&sortDirection=${sortDirection}`;
    const [c, u] = await Promise.all([api<CatalogPage>(`/api/commodities?${query(cq, cp, cs, cd)}`), api<CatalogPage>(`/api/measurement-units?${query(uq, up, us, ud)}`)]);
    if (request === requestId.current) { setCommodities(c); setUnits(u); }
  }
  useEffect(() => { if (active) void action.run(load); return () => { requestId.current++; }; }, [active, cq, cp, cs, cd, uq, up, us, ud]);
  async function create(event: FormEvent, path: string, body: Row) { event.preventDefault(); await action.run(async () => { await api(path, { method: 'POST', body: JSON.stringify(body) }); await load(); }, 'Catalog record saved.'); }
  async function update(path: string, row: Row, label: string) {
    const value = window.prompt(`New ${label} name`, row.name); if (value === null || value === row.name) return;
    await action.run(async () => { await api(`/api/${path}/${row.id}`, { method: 'PATCH', headers: { 'If-Match': `\"${row.version}\"` }, body: JSON.stringify({ name: value }) }); await load(); }, 'Catalog record updated.');
  }
  async function remove(path: string, row: Row, label: string, page: number, pageSize: number, totalCount: number, setPage: (value: number) => void) {
    if (!window.confirm(`Remove ${row.code} from active ${label}? Stored record/history will be retained.`)) return;
    await action.run(async () => { await api(`/api/${path}/${row.id}`, { method: 'DELETE', headers: { 'If-Match': `\"${row.version}\"` } }); if (totalCount <= (page - 1) * pageSize + 1 && page > 1) setPage(page - 1); await load(); }, 'Catalog record removed.');
  }
  function catalogTable(label: string, data: CatalogPage, query: string, setQuery: (v: string) => void, page: number, setPage: (v: number) => void, sort: string, setSort: (v: string) => void, direction: 'asc'|'desc', setDirection: (v: 'asc'|'desc') => void, path: string, columns: string[]) {
    return <><ListControls label={label} search={query} onSearch={v => { setQuery(v); setPage(1); }} sortBy={sort} onSortBy={v => { setSort(v); setPage(1); }} sortDirection={direction} onSortDirection={setDirection} sortOptions={[{value:'code',label:'Code'},{value:'name',label:'Name'}]} /><Table rows={data.items} label={label} controls={false} columns={columns} actions={row => row.canEdit || row.canDelete ? <>{row.canEdit && <button type="button" onClick={() => void update(path, row, label)}>Edit {row.name}</button>}{row.canDelete && <button type="button" onClick={() => void remove(path, row, label, page, data.pageSize, data.totalCount, setPage)}>{label === 'commodities' ? 'Remove' : 'Delete'} {row.name}</button>}</> : undefined} /><Pagination label={label} page={page} pageSize={data.pageSize} totalCount={data.totalCount} onChange={setPage} busy={action.busy} />{data.items.some(row => row.createdBy === null) && <p>Legacy catalog records are read-only.</p>}</>;
  }
  return <><Feedback {...action} /><div className="ledger-grid"><Panel title="Commodity catalog"><p>Canonical commodity master. Ledger stock and processing use these Catalog names.</p>{catalogTable('commodities', commodities, cq, setCq, cp, setCp, cs, setCs, cd, setCd, 'commodities', ['code','name'])}<form onSubmit={e => void create(e, '/api/commodities', { code, name })}><label>Code<input required maxLength={40} value={code} onChange={e => setCode(e.target.value)} /></label><label>Name<input required value={name} onChange={e => setName(e.target.value)} /></label><button disabled={action.busy}>Create commodity</button></form></Panel><Panel title="Measurement units">{catalogTable('measurement units', units, uq, setUq, up, setUp, us, setUs, ud, setUd, 'measurement-units', ['code','name','quantityKind'])}<form onSubmit={e => void create(e, '/api/measurement-units', { code: unitCode, name: unitName, quantityKind })}><label>Code<input required maxLength={20} value={unitCode} onChange={e => setUnitCode(e.target.value)} /></label><label>Name<input required value={unitName} onChange={e => setUnitName(e.target.value)} /></label><label>Quantity kind<select value={quantityKind} onChange={e => setQuantityKind(e.target.value)}>{kinds.map(value => <option key={value}>{value}</option>)}</select></label><button disabled={action.busy}>Create measurement unit</button></form></Panel></div></>;
}
