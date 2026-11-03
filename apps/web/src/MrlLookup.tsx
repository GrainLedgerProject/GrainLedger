import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { type Page } from './procurementTypes';

type Option = { id: string; code: string; name: string };
type Version = { id: string; versionNo: number; status: string; effectiveFrom: string; effectiveTo: string | null; sourceRef: string };
type Limit = { id: string; substanceId: string; limitValue: number; unit: string };

export function MrlLookup() {
  const [markets, setMarkets] = useState<Option[]>([]); const [commodities, setCommodities] = useState<Option[]>([]);
  const [substances, setSubstances] = useState<Option[]>([]);
  const [marketId, setMarketId] = useState(''); const [commodityId, setCommodityId] = useState('');
  const [decisionAt, setDecisionAt] = useState('');
  const [version, setVersion] = useState<Version | null>(null); const [limits, setLimits] = useState<Limit[]>([]);
  const [loading, setLoading] = useState(false); const [error, setError] = useState(''); const [catalogError, setCatalogError] = useState('');
  const requestVersion = useRef(0);
  function clearResult() { requestVersion.current++; setVersion(null); setLimits([]); setError(''); setLoading(false); }
  useEffect(() => {
    let active = true; const controller = new AbortController();
    async function all(path: string) {
      const rows: Option[] = [];
      for (let page = 1; page <= 100; page++) {
        const result = await api<Page<Option>>(`${path}?page=${page}&pageSize=100`, { signal: controller.signal });
        rows.push(...result.items); if (!result.hasMore) return rows;
      }
      throw new Error('Catalog is too large to load.');
    }
    void Promise.all([all('/api/markets'), all('/api/commodities'), all('/api/active-substances')])
      .then(([m, c, s]) => { if (active) { setMarkets(m); setCommodities(c); setSubstances(s); } })
      .catch(cause => { if (active) setCatalogError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, []);
  async function resolve(event: FormEvent) {
    event.preventDefault(); if (loading) return;
    const current = ++requestVersion.current;
    setLoading(true); setError(''); setVersion(null); setLimits([]);
    try {
      const params = new URLSearchParams({ marketId, commodityId, decisionAt: new Date(decisionAt).toISOString() });
      const found = await api<Version>(`/api/mrl-versions/resolve?${params}`);
      const rows: Limit[] = [];
      for (let page = 1; page <= 100; page++) {
        const result = await api<Page<Limit>>(`/api/mrl-versions/${found.id}/limits?page=${page}&pageSize=100`);
        rows.push(...result.items);
        if (!result.hasMore) { if (current === requestVersion.current) { setVersion(found); setLimits(rows); } return; }
      }
      throw new Error('This version has too many limits to display.');
    } catch (cause) {
      if (current === requestVersion.current) setError(cause instanceof ApiError && cause.status === 404 ? 'No published MRL version applies at this time.' : cause instanceof ApiError && cause.status === 409 ? 'Several MRL versions apply at this time. Ask an administrator to resolve the overlap.' : errorMessage(cause));
    } finally { if (current === requestVersion.current) setLoading(false); }
  }
  return <div><div className="page-heading"><span className="eyebrow">RESIDUE RULES</span><h1>Applicable MRL</h1><p className="muted">Find the published MRL version that applied to a market, commodity and decision time.</p></div>
    <section className="panel admin-mrl-panel"><h2>Find applicable version</h2>{catalogError && <div className="notice error" role="alert">{catalogError}</div>}<form onSubmit={resolve}><div className="admin-form-grid"><label>Export market<select value={marketId} onChange={event => { setMarketId(event.target.value); clearResult(); }} required><option value="">Select market</option>{markets.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label><label>Commodity<select value={commodityId} onChange={event => { setCommodityId(event.target.value); clearResult(); }} required><option value="">Select commodity</option>{commodities.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label><label>Decision time<input type="datetime-local" value={decisionAt} onChange={event => { setDecisionAt(event.target.value); clearResult(); }} required /></label></div><button disabled={loading || !marketId || !commodityId || !decisionAt}>{loading ? 'Looking up…' : 'Find version'}</button></form>{error && <div className="notice error" role="alert">{error}</div>}</section>
    {version && <section className="panel admin-mrl-panel"><span className="eyebrow">PUBLISHED · VERSION {version.versionNo}</span><h2>Applicable version</h2><p>Effective {new Date(version.effectiveFrom).toLocaleString()} to {version.effectiveTo ? new Date(version.effectiveTo).toLocaleString() : 'open-ended'}</p><p><strong>Regulatory source:</strong> {version.sourceRef}</p><h3>Substance limits</h3>{limits.length ? <div className="table-scroll"><table><thead><tr><th>Active substance</th><th>Limit</th><th>Unit</th></tr></thead><tbody>{limits.map(item => <tr key={item.id}><td>{substances.find(s => s.id === item.substanceId)?.name ?? item.substanceId}</td><td>{item.limitValue}</td><td>{item.unit}</td></tr>)}</tbody></table></div> : <p className="empty">No limits in this version.</p>}<p className="muted small">This lookup shows the legal limits. It does not assess a sample or lot without laboratory results.</p></section>}
  </div>;
}
