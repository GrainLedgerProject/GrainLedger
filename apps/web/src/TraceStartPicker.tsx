import { useEffect, useState } from 'react';
import { api, errorMessage } from './api';
import { Pagination } from './ledger-ui';
import { traceKindLabel } from './trace-kind';

type StartLot = { id: string; code: string; kind: string; stage: string; quantityKg: string; poolRevision: number | null;
  positions: { locationName: string; facilityName: string }[] };
type StartPage = { items: StartLot[]; page: number; pageSize: number; totalCount: number;
  locations?: { id: string; name: string; facilityId: string }[]; facilities?: { id: string; name: string }[] };
const empty: StartPage = { items: [], page: 1, pageSize: 25, totalCount: 0 };

export function TraceStartPicker({ value, onChange, active }: { value: string; onChange: (id: string) => void; active: boolean }) {
  const [mode, setMode] = useState('pools'), [search, setSearch] = useState(''), [facility, setFacility] = useState(''), [location, setLocation] = useState('');
  const [page, setPage] = useState(1), [data, setData] = useState<StartPage>(empty), [error, setError] = useState(''), [loading, setLoading] = useState(false), [retry, setRetry] = useState(0);
  useEffect(() => {
    if (!active) return;
    let current = true; const controller = new AbortController(); setLoading(true); setError(''); setData(empty);
    const query = new URLSearchParams({ mode, page: String(page), pageSize: '25' });
    if (search) query.set('search', search);
    if (facility) query.set('facilityId', facility);
    if (location) query.set('locationId', location);
    void api<StartPage>(`/api/ledger/trace/starts?${query}`, { signal: controller.signal }).then(next => {
      if (!Array.isArray(next.items) || !Number.isInteger(next.totalCount)) throw new Error('Trace starting-lot response is unavailable.');
      if (current) setData(next);
    }).catch(cause => { if (current) setError(errorMessage(cause)); }).finally(() => { if (current) setLoading(false); });
    return () => { current = false; controller.abort(); };
  }, [active, mode, search, facility, location, page, retry]);
  function reset(change: () => void) { change(); setPage(1); onChange(''); }
  return <fieldset disabled={!active}>
    <legend>Find a tracing starting point</legend>
    <div className="ledger-form-grid">
      <label>Starting point type<select value={mode} onChange={e => reset(() => setMode(e.target.value))}>
        <option value="pools">Current silo / warehouse pools</option><option value="sources">Procurement sources</option><option value="all">All recorded lots, including history</option>
      </select></label>
      <label>Trace facility<select value={facility} onChange={e => reset(() => { setFacility(e.target.value); setLocation(''); })}><option value="">All permitted facilities</option>{data.facilities?.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}</select></label>
      <label>Trace silo / location<select value={location} onChange={e => reset(() => setLocation(e.target.value))}><option value="">All permitted locations</option>{data.locations?.filter(l => !facility || l.facilityId === facility).map(l => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
      <label>Search Starting lot<input type="search" onKeyDown={e => { if (e.key === 'Enter') e.preventDefault(); }} value={search} onChange={e => reset(() => setSearch(e.target.value))} /></label>
    </div>
    {error && <div role="alert" className="notice error">{error} <button type="button" onClick={() => setRetry(n => n + 1)}>Retry starting lots</button></div>}
    <label>Starting lot<select value={value} onChange={e => onChange(e.target.value)} disabled={loading}><option value="">Select…</option>
      {value && !data.items.some(l => l.id === value) && <option value={value}>Entered Ledger lot · {value}</option>}
      {data.items.map(l => <option key={l.id} value={l.id}>{l.code} · {traceKindLabel(l)} · {l.stage} · {l.quantityKg} kg{l.poolRevision != null ? ` · r${l.poolRevision}` : ''}{l.positions.map(p => ` · ${p.locationName}`).join('')}</option>)}
    </select></label>
    {loading && <p role="status">Loading trace starting lots…</p>}
    {!loading && !error && !data.items.length && <p>No starting lots match these filters. Try procurement sources or recorded history, or enter a Ledger lot ID below.</p>}
    <Pagination label="Starting lot" page={page} pageSize={25} totalCount={data.totalCount} onChange={setPage} busy={loading} />
  </fieldset>;
}
