import { useEffect, useState } from 'react';
import { api, errorMessage } from './api';
import { type Page, type Procurement, type ProcurementSummary, type SourceLot } from './procurementTypes';
import { SourceSamples } from './Samples';

export function Procurements({ canCreate, onCreate, onOpen }: { canCreate: boolean; onCreate: () => void; onOpen: (id: string) => void }) {
  const [page, setPage] = useState(1); const [retry, setRetry] = useState(0);
  const [list, setList] = useState<Page<ProcurementSummary> | null>(null);
  const [error, setError] = useState(''); const [loading, setLoading] = useState(true);
  useEffect(() => {
    let active = true; const controller = new AbortController(); setLoading(true); setList(null); setError('');
    void api<Page<ProcurementSummary>>(`/api/procurements?page=${page}&pageSize=20`, { signal: controller.signal })
      .then(value => { if (active) setList(value); }).catch(cause => { if (active) setError(errorMessage(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [page, retry]);
  return <><div className="page-heading farmer-page-heading"><div><span className="eyebrow">PROCUREMENT</span><h1>Paddy intake</h1><p className="muted">Committed receipts from farmers and their source lots.</p></div>{canCreate && <button onClick={onCreate}>+ Record intake</button>}</div>
    <section className="panel">{loading && <p role="status">Loading procurements…</p>}{error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(n => n + 1)}>Try again</button></div>}
      {list && (list.items.length ? <div className="table-scroll"><table><thead><tr><th>Receipt</th><th>Farmer</th><th>Received</th><th>Lines</th><th>Net mass</th><th>Status</th></tr></thead><tbody>{list.items.map(item => <tr key={item.id}><td><button className="text-button farmer-plot-link" onClick={() => onOpen(item.id)}>{item.code} ↗</button></td><td>{item.farmerName}</td><td>{new Date(item.receivedAt).toLocaleString()}</td><td>{item.itemCount}</td><td>{item.totalNetMassKg} kg</td><td>{item.status}</td></tr>)}</tbody></table></div> : <p className="empty">No committed procurements found.</p>)}
      {list && <div className="farmer-pagination"><span>Page {list.page}</span><button className="secondary" disabled={page === 1 || loading} onClick={() => setPage(p => p - 1)}>Previous</button><button className="secondary" disabled={!list.hasMore || loading} onClick={() => setPage(p => p + 1)}>Next</button></div>}
    </section></>;
}

export function ProcurementDetail({ id, onBack, onOpenSourceLot }: { id: string; onBack: () => void; onOpenSourceLot: (id: string) => void }) {
  const [record, setRecord] = useState<Procurement | null>(null); const [error, setError] = useState(''); const [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true; const controller = new AbortController(); setRecord(null); setError('');
    void api<Procurement>(`/api/procurements/${encodeURIComponent(id)}`, { signal: controller.signal })
      .then(value => { if (active) setRecord(value); }).catch(cause => { if (active) setError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, [id, retry]);
  return <><div className="page-heading"><span className="eyebrow">PROCUREMENT</span><h1>Receipt details</h1><p className="muted">Immutable record of the accepted farmer delivery.</p></div><button className="text-button" onClick={onBack}>← Back to procurements</button>
    {!record && !error && <p role="status">Loading receipt…</p>}{error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(n => n + 1)}>Try again</button></div>}
    {record && <><section className="panel capture-detail"><span className="eyebrow">{record.status}</span><h2>{record.code}</h2><div className="capture-stat-grid"><div><span>Received</span><strong>{new Date(record.receivedAt).toLocaleString()}</strong></div><div><span>Total net mass</span><strong>{record.totalNetMassKg} kg</strong></div><div><span>Lines</span><strong>{record.items.length}</strong></div></div></section>
      <section className="panel capture-section"><h2>Source lots</h2><div className="table-scroll"><table><thead><tr><th>Line</th><th>Lot</th><th>Rice variety</th><th>Measured</th><th>Net mass</th><th>Moisture</th></tr></thead><tbody>{record.items.map(item => <tr key={item.id}><td>{item.lineNo}</td><td><button className="text-button farmer-plot-link" onClick={() => onOpenSourceLot(item.sourceLotId)}>{item.lotCode} ↗</button></td><td>{item.riceVariety}</td><td>{item.measuredValue} {item.measuredUnit}</td><td>{item.netMassKg} kg</td><td>{item.moisturePercent}%</td></tr>)}</tbody></table></div></section></>}
  </>;
}

export function SourceLotDetail({ id, canWriteSamples, userId, companyId, onBack, onOpenSample }: { id: string; canWriteSamples: boolean; userId: string; companyId: string; onBack: (procurementId: string | null) => void; onOpenSample: (id: string) => void }) {
  const [lot, setLot] = useState<SourceLot | null>(null); const [error, setError] = useState(''); const [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true; const controller = new AbortController(); setLot(null); setError('');
    void api<SourceLot>(`/api/source-lots/${encodeURIComponent(id)}`, { signal: controller.signal })
      .then(value => { if (active) setLot(value); }).catch(cause => { if (active) setError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, [id, retry]);
  return <><div className="page-heading"><span className="eyebrow">SOURCE LOT</span><h1>Paddy source lot</h1><p className="muted">Origin, initial receipt and current Ledger stock are separate records.</p></div><button className="text-button" onClick={() => onBack(lot?.procurementId ?? null)}>← Back to receipt</button>
    {!lot && !error && <p role="status">Loading source lot…</p>}{error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(n => n + 1)}>Try again</button></div>}
    {lot && <><section className="panel capture-detail"><h2>{lot.lotCode}</h2><div className="capture-stat-grid"><div><span>Initial net mass</span><strong>{lot.initialNetMassKg} kg</strong></div><div><span>Moisture</span><strong>{lot.moisturePercent}%</strong></div><div><span>Rice variety</span><strong>{lot.riceVariety}</strong></div></div><p className="muted small">Current stock as of {new Date(lot.stockAsOf).toLocaleString()}</p><div className="table-scroll"><table><thead><tr><th>Location ID</th><th>On-hand</th></tr></thead><tbody>{lot.currentStock.map(position => <tr key={position.locationId}><td>{position.locationId}</td><td>{position.onHandKg} kg</td></tr>)}</tbody></table></div></section>
      <SourceSamples sourceLotId={lot.id} canCreate={canWriteSamples} userId={userId} companyId={companyId} onOpen={onOpenSample} /></>}
  </>;
}
