import { useEffect, useState, type FormEvent } from 'react';
import { api, errorMessage } from './api';
import { type Page, type Procurement, type ProcurementSummary, type SourceLot } from './procurementTypes';
import { SourceSamples } from './Samples';
import { LabReports } from './LabReports';

export type IntakeListState = { page: number; receiptQuery: string; farmerId: string; from: string; to: string; sortBy: string; sortDirection: string };
const defaultIntakeState: IntakeListState = { page: 1, receiptQuery: '', farmerId: '', from: '', to: '', sortBy: 'receivedAt', sortDirection: 'desc' };
const arrivalStatusLabel = (status: string) => ({ RECEIVED_MATCHED: 'Received — exact match', RECEIVED_WITHIN_TOLERANCE: 'Received — within tolerance', RECEIVED_SHORTAGE_APPROVED: 'Received — shortage approved', AWAITING_MANAGER_REVIEW: 'Awaiting manager review', REMEASUREMENT_REQUESTED: 'Remeasurement requested', BLOCKED_UNRESOLVED: 'Blocked — unresolved', PENDING_ARRIVAL: 'Pending mill arrival' } as Record<string, string>)[status] || status;
export function Procurements({ canCreate, onCreate, onOpen, initialState = defaultIntakeState, onStateChange }: { canCreate: boolean; onCreate: () => void; onOpen: (id: string) => void; initialState?: IntakeListState; onStateChange?: (state: IntakeListState) => void }) {
  const [page, setPage] = useState(initialState.page); const [retry, setRetry] = useState(0);
  const [list, setList] = useState<Page<ProcurementSummary> | null>(null);
  const [error, setError] = useState(''); const [loading, setLoading] = useState(true);
  const [farmerQuery, setFarmerQuery] = useState('');
  const [farmers, setFarmers] = useState<{ id: string; code: string; name: string }[]>([]);
  const [farmerId, setFarmerId] = useState(initialState.farmerId);
  const [receiptQuery, setReceiptQuery] = useState(initialState.receiptQuery);
  const [from, setFrom] = useState(initialState.from); const [to, setTo] = useState(initialState.to);
  const [sortBy, setSortBy] = useState(initialState.sortBy); const [sortDirection, setSortDirection] = useState(initialState.sortDirection);
  const [applied, setApplied] = useState(initialState);
  const [filterError, setFilterError] = useState('');
  useEffect(() => {
    let active = true; const controller = new AbortController();
    const params = new URLSearchParams({ page: '1', pageSize: '100' });
    if (farmerQuery.trim()) params.set('q', farmerQuery.trim());
    void api<Page<{ id: string; code: string; name: string }>>(`/api/farmers?${params}`, { signal: controller.signal })
      .then(value => { if (active) setFarmers(value.items); }).catch(() => { if (active) setFarmers([]); });
    return () => { active = false; controller.abort(); };
  }, [farmerQuery]);
  useEffect(() => {
    let active = true; const controller = new AbortController(); setLoading(true); setList(null); setError('');
    const params = new URLSearchParams({ page: String(page), pageSize: '20', sortBy: applied.sortBy, sortDirection: applied.sortDirection });
    if (applied.receiptQuery) params.set('search', applied.receiptQuery);
    if (applied.farmerId) params.set('farmerId', applied.farmerId);
    if (applied.from) params.set('from', applied.from);
    if (applied.to) params.set('to', applied.to);
    void api<Page<ProcurementSummary>>(`/api/procurements?${params}`, { signal: controller.signal })
      .then(value => { if (active) setList(value); }).catch(cause => { if (active) setError(errorMessage(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [page, retry, applied]);
  function applyFilters(event: FormEvent) {
    event.preventDefault();
    if (from && to && from > to) { setFilterError('From date must be on or before To date.'); return; }
    setFilterError(''); setPage(1);
    const next = { page: 1, receiptQuery: receiptQuery.trim(), farmerId, from, to, sortBy, sortDirection };
    setApplied(next); onStateChange?.(next);
  }
  function clearFilters() {
    setReceiptQuery(''); setFarmerQuery(''); setFarmerId(''); setFrom(''); setTo(''); setSortBy('receivedAt'); setSortDirection('desc'); setFilterError(''); setPage(1);
    setApplied(defaultIntakeState); onStateChange?.(defaultIntakeState);
  }
  return <><div className="page-heading farmer-page-heading"><div><span className="eyebrow">PROCUREMENT</span><h1>Paddy intake</h1><p className="muted">Source registrations and their mill-arrival status. Pending arrivals are not usable stock.</p></div>{canCreate && <button onClick={onCreate}>+ Record intake</button>}</div>
    <section className="panel"><form onSubmit={applyFilters}><div className="admin-form-grid"><label>Receipt code<input value={receiptQuery} onChange={e => setReceiptQuery(e.target.value)} maxLength={100} placeholder="Search receipt code" /></label><label>Find farmer<input value={farmerQuery} onChange={e => { setFarmerQuery(e.target.value); setFarmerId(''); }} maxLength={100} placeholder="Name or code" /></label><label>Farmer<select value={farmerId} onChange={e => setFarmerId(e.target.value)}><option value="">All farmers</option>{farmers.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}{farmerId && !farmers.some(item => item.id === farmerId) && <option value={farmerId}>Selected farmer</option>}</select></label><label>From date<input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label><label>To date<input type="date" value={to} onChange={e => setTo(e.target.value)} /></label><label>Sort by<select value={sortBy} onChange={e => setSortBy(e.target.value)}><option value="receivedAt">Received time</option><option value="code">Receipt code</option><option value="status">Arrival status</option></select></label><label>Direction<select value={sortDirection} onChange={e => setSortDirection(e.target.value)}><option value="desc">Descending</option><option value="asc">Ascending</option></select></label></div>{filterError && <div className="notice error" role="alert">{filterError}</div>}<div className="admin-form-actions"><button type="submit">Apply filters</button><button className="secondary" type="button" onClick={clearFilters}>Clear</button></div></form>
      {loading && <p role="status">Loading procurements…</p>}{error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(n => n + 1)}>Try again</button></div>}
      {list && (list.items.length ? <div className="table-scroll"><table><thead><tr><th>Receipt</th><th>Farmer</th><th>Received</th><th>Lines</th><th>Expected mass</th><th>Arrival status</th></tr></thead><tbody>{list.items.map(item => <tr key={item.id}><td><button className="text-button farmer-plot-link" onClick={() => onOpen(item.id)}>{item.code} ↗</button></td><td>{item.farmerName}</td><td>{new Date(item.receivedAt).toLocaleString()}</td><td>{item.itemCount}</td><td>{item.totalNetMassKg} kg</td><td>{arrivalStatusLabel(item.status)}</td></tr>)}</tbody></table></div> : <p className="empty">{applied.receiptQuery || applied.farmerId || applied.from || applied.to ? 'No intake matches these filters.' : 'No procurement registrations found.'}</p>)}
      {list && <div className="farmer-pagination"><span>Page {list.page}</span><button className="secondary" disabled={page === 1 || loading} onClick={() => { const next = page - 1; setPage(next); onStateChange?.({ ...applied, page: next }); }}>Previous</button><button className="secondary" disabled={!list.hasMore || loading} onClick={() => { const next = page + 1; setPage(next); onStateChange?.({ ...applied, page: next }); }}>Next</button></div>}
    </section></>;
}

export function ProcurementDetail({ id, onBack, onOpenSourceLot, onOpenFarmer, onOpenPlot, onOpenPlotSeason }: { id: string; onBack: () => void; onOpenSourceLot: (id: string) => void; onOpenFarmer?: (id: string) => void; onOpenPlot?: (id: string) => void; onOpenPlotSeason?: (id: string) => void }) {
  const [record, setRecord] = useState<Procurement | null>(null); const [error, setError] = useState(''); const [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true; const controller = new AbortController(); setRecord(null); setError('');
    void api<Procurement>(`/api/procurements/${encodeURIComponent(id)}`, { signal: controller.signal })
      .then(value => { if (active) setRecord(value); }).catch(cause => { if (active) setError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, [id, retry]);
  return <><div className="page-heading"><span className="eyebrow">PROCUREMENT</span><h1>Receipt details</h1><p className="muted">Immutable source registration. Mill staff measure and confirm the arrival separately.</p></div><button className="text-button" onClick={onBack}>← Back to procurements</button>
    {!record && !error && <p role="status">Loading receipt…</p>}{error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(n => n + 1)}>Try again</button></div>}
    {record && <><section className="panel capture-detail"><span className="eyebrow">{arrivalStatusLabel(record.status)}</span><h2>{record.code}</h2><p>Farmer: {onOpenFarmer ? <button className="text-button" onClick={() => onOpenFarmer(record.farmerId)}>{record.farmerName ?? record.farmerCode ?? record.farmerId} ↗</button> : record.farmerName ?? record.farmerCode ?? record.farmerId}</p><div className="capture-stat-grid"><div><span>Recorded arrival time</span><strong>{new Date(record.receivedAt).toLocaleString()}</strong></div><div><span>Total expected mass</span><strong>{record.totalNetMassKg} kg</strong></div><div><span>Lines</span><strong>{record.items.length}</strong></div></div>{!['RECEIVED_MATCHED', 'RECEIVED_WITHIN_TOLERANCE', 'RECEIVED_SHORTAGE_APPROVED'].includes(record.status) && <p className="notice pending">The mill has not accepted this arrival. No usable stock is available from this registration.</p>}</section>
      <section className="panel capture-section"><h2>Source lots</h2><div className="source-lot-list">{record.items.map(item => <article className="source-lot-card" key={item.id}>
        <div className="source-lot-card-heading"><span className="eyebrow">LINE {item.lineNo}</span><button className="text-button farmer-plot-link" onClick={() => onOpenSourceLot(item.sourceLotId)}>{item.lotCode} ↗</button></div>
        <dl className="source-lot-card-facts">
          <div><dt>Plot</dt><dd>{onOpenPlot ? <button className="text-button" onClick={() => onOpenPlot(item.plotId)}>{item.plotCode ?? item.plotId} ↗</button> : item.plotCode ?? item.plotId}</dd></div>
          <div><dt>Season</dt><dd>{onOpenPlotSeason ? <button className="text-button" onClick={() => onOpenPlotSeason(item.plotSeasonId)}>{item.seasonName ?? item.plotSeasonId} ↗</button> : item.seasonName ?? item.plotSeasonId}</dd></div>
          <div><dt>Rice variety</dt><dd>{item.riceVariety}</dd></div>
          <div><dt>Declared quantity</dt><dd>{item.measuredValue} {item.measuredUnit}</dd></div>
          <div><dt>Expected net mass</dt><dd>{item.netMassKg} kg</dd></div>
          <div><dt>Moisture</dt><dd>{item.moisturePercent}%</dd></div>
        </dl>
      </article>)}</div></section></>}
  </>;
}

export function SourceLotDetail({ id, canWriteSamples, canReadLab = false, userId, companyId, onBack, onOpenSample, onOpenFarmer, onOpenPlot, onOpenPlotSeason }: { id: string; canWriteSamples: boolean; canReadLab?: boolean; userId: string; companyId: string; onBack: (procurementId: string | null) => void; onOpenSample: (id: string) => void; onOpenFarmer?: (id: string) => void; onOpenPlot?: (id: string) => void; onOpenPlotSeason?: (id: string) => void }) {
  const [lot, setLot] = useState<SourceLot | null>(null); const [error, setError] = useState(''); const [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true; const controller = new AbortController(); setLot(null); setError('');
    void api<SourceLot>(`/api/source-lots/${encodeURIComponent(id)}`, { signal: controller.signal })
      .then(value => { if (active) setLot(value); }).catch(cause => { if (active) setError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, [id, retry]);
  return <><div className="page-heading"><span className="eyebrow">SOURCE LOT</span><h1>Paddy source lot</h1><p className="muted">Origin, expected quantity and current Ledger stock are separate records.</p></div><button className="text-button" onClick={() => onBack(lot?.procurementId ?? null)}>← Back to receipt</button>
    {!lot && !error && <p role="status">Loading source lot…</p>}{error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(n => n + 1)}>Try again</button></div>}
    {lot && <><section className="panel capture-detail"><span className="eyebrow">{arrivalStatusLabel(lot.receivingStatus)}</span><h2>{lot.lotCode}</h2><div className="capture-stat-grid"><div><span>Expected net mass</span><strong>{lot.initialNetMassKg} kg</strong></div><div><span>Usable stock</span><strong>{lot.usableStockKg} kg</strong></div><div><span>Rice variety</span><strong>{lot.riceVariety}</strong></div></div><p>Receipt: <button className="text-button" onClick={() => onBack(lot.procurementId)}>{lot.procurementCode} ↗</button></p><p>Farmer: {onOpenFarmer ? <button className="text-button" onClick={() => onOpenFarmer(lot.farmerId)}>{lot.farmerName ?? lot.farmerCode ?? lot.farmerId} ↗</button> : lot.farmerName ?? lot.farmerCode ?? lot.farmerId}</p><p>Plot: {onOpenPlot ? <button className="text-button" onClick={() => onOpenPlot(lot.plotId)}>{lot.plotCode ?? lot.plotId} ↗</button> : lot.plotCode ?? lot.plotId}</p><p>Season: {onOpenPlotSeason ? <button className="text-button" onClick={() => onOpenPlotSeason(lot.plotSeasonId)}>{lot.seasonName ?? lot.plotSeasonId} ↗</button> : lot.seasonName ?? lot.plotSeasonId}</p><p className="muted small">Stock checked at {new Date(lot.stockAsOf).toLocaleString()}. Moisture: {lot.moisturePercent}%.</p>{lot.currentStock.length === 0 ? <p className="notice pending">No stock has been accepted for this source lot.</p> : <div className="table-scroll"><table><thead><tr><th>Location ID</th><th>On-hand</th><th>Available</th></tr></thead><tbody>{lot.currentStock.map(position => <tr key={position.locationId}><td>{position.locationId}</td><td>{position.onHandKg} kg</td><td>{position.availableKg} kg</td></tr>)}</tbody></table></div>}</section>
      <SourceSamples sourceLotId={lot.id} canCreate={canWriteSamples} userId={userId} companyId={companyId} onOpen={onOpenSample} />{canReadLab && <LabReports canWrite={false} sourceLotId={lot.id} onOpenSample={onOpenSample} />}</>}
  </>;
}
