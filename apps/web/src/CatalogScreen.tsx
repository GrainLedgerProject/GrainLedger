import { useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { Feedback, Panel, Table, useAction, type Row } from './ledger-ui';

type Page = { items: Row[]; page: number; pageSize: number; hasMore: boolean };
const kinds = ['MASS', 'AREA', 'RESIDUE_CONCENTRATION', 'APPLICATION_RATE', 'FRACTION'];
export function CatalogScreen() {
  const action = useAction();
  const [commodities, setCommodities] = useState<Page>({ items: [], page: 1, pageSize: 25, hasMore: false });
  const [units, setUnits] = useState<Page>({ items: [], page: 1, pageSize: 25, hasMore: false });
  const [commodityQuery, setCommodityQuery] = useState(''), [unitQuery, setUnitQuery] = useState('');
  const [commodityPage, setCommodityPage] = useState(1), [unitPage, setUnitPage] = useState(1);
  const [code, setCode] = useState(''), [name, setName] = useState(''), [unitCode, setUnitCode] = useState(''), [unitName, setUnitName] = useState(''), [quantityKind, setQuantityKind] = useState('MASS');
  async function load() {
    const params = (q: string, page: number) => `q=${encodeURIComponent(q)}&page=${page}&pageSize=25`;
    const [cs, us] = await Promise.all([api<Page>(`/api/commodities?${params(commodityQuery, commodityPage)}`), api<Page>(`/api/measurement-units?${params(unitQuery, unitPage)}`)]);
    setCommodities(cs); setUnits(us);
  }
  useEffect(() => { void action.run(load); }, [commodityQuery, commodityPage, unitQuery, unitPage]);
  async function create(event: FormEvent, path: string, body: Row) {
    event.preventDefault();
    await action.run(async () => { await api(path, { method: 'POST', body: JSON.stringify(body) }); await load(); }, 'Catalog record saved.');
  }
  function pager(title: string, data: Page, page: number, change: (n: number) => void) {
    return <div className="ledger-actions"><button disabled={page <= 1 || action.busy} onClick={() => change(page - 1)}>Previous {title} page</button><span>Page {page}</span><button disabled={!data.hasMore || action.busy} onClick={() => change(page + 1)}>Next {title} page</button></div>;
  }
  return <><Feedback {...action} /><div className="ledger-grid"><Panel title="Commodities"><label>Search commodities<input value={commodityQuery} onChange={e => { setCommodityQuery(e.target.value); setCommodityPage(1); }} /></label><Table rows={commodities.items} />{pager('commodity', commodities, commodityPage, setCommodityPage)}<form onSubmit={e => void create(e, '/api/commodities', { code, name })}><label>Code<input required maxLength={40} value={code} onChange={e => setCode(e.target.value)} /></label><label>Name<input required value={name} onChange={e => setName(e.target.value)} /></label><button disabled={action.busy}>Create commodity</button></form></Panel><Panel title="Measurement units"><label>Search measurement units<input value={unitQuery} onChange={e => { setUnitQuery(e.target.value); setUnitPage(1); }} /></label><Table rows={units.items} />{pager('measurement unit', units, unitPage, setUnitPage)}<form onSubmit={e => void create(e, '/api/measurement-units', { code: unitCode, name: unitName, quantityKind })}><label>Code<input required maxLength={30} value={unitCode} onChange={e => setUnitCode(e.target.value)} /></label><label>Name<input required value={unitName} onChange={e => setUnitName(e.target.value)} /></label><label>Quantity kind<select value={quantityKind} onChange={e => setQuantityKind(e.target.value)}>{kinds.map(kind => <option key={kind}>{kind}</option>)}</select></label><button disabled={action.busy}>Create measurement unit</button></form></Panel></div></>;
}
