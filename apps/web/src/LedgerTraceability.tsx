import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { api, errorMessage, type User } from './api';
import { Feedback, Pagination, Panel, emptyLookup } from './ledger-ui';
import { LedgerLotPicker } from './LedgerLotPicker';
import { TraceFlowchart } from './TraceFlowchart';
import { traceLotOrder } from './trace-flow';

type Contribution = { sourceLotId: string; quantityKg: string; denominatorKg: string; percentage: string };
type TraceLot = { id: string; code: string; stage: string; producedKg: string; restrictions: string[]; contributions: Contribution[] | null;
  positions: { locationId: string; quantityKg: string; availableKg: string }[] };
type Provenance = { sourceRecordId: string; procurementCode: string; farmerId: string; plotId: string; seasonId: string; plotSeasonId: string; riceVariety: string; expectedKg: string; originalValue: string; originalUnit: string };
export type LotTrace = { traceId: string; targetLotId: string; direction: 'forward' | 'backward'; view: 'current' | 'history'; asOf: string; complete: boolean; lotCount: number; edgeCount: number;
  sourceContributionsVisible: boolean; lots: TraceLot[];
  edges: { operationId: string; inputLotId: string; outputLotId: string; inputConsumedKg: string; outputProducedKg: string }[];
  operations: { id: string; kind: string; status: string; occurredAt: string; declaredLossKg: string; varianceKg: string }[];
  corrections: { id: string; originalOperationId: string; replacementOperationId: string | null; status: string }[];
  sources: { lotId: string; status: string; provenance: Provenance | null }[] };

export function LedgerTraceability({ user, active = true }: { user: User; active?: boolean }) {
  const [lotId, setLotId] = useState(''), [direction, setDirection] = useState<'forward' | 'backward'>('forward'), [view, setView] = useState<'current' | 'history'>('current');
  const [result, setResult] = useState<LotTrace | null>(null), [selectedId, setSelectedId] = useState(''), [page, setPage] = useState(1), [edgePage, setEdgePage] = useState(1);
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  const [display, setDisplay] = useState<'table' | 'flowchart'>('table');
  const orderedLots = useMemo(() => {
    if (!result) return [];
    const byId = new Map(result.lots.map(lot => [lot.id, lot]));
    return traceLotOrder(result).map(id => byId.get(id)!);
  }, [result]);
  const request = useRef(0), controller = useRef<AbortController | null>(null);
  const global = user.permissions.includes('trace.read.all');
  const canTrace = global || user.permissions.includes('trace.read.tenant');
  const canSources = global || user.role === 'CustomerManager';
  function invalidate() { request.current++; controller.current?.abort(); setResult(null); setError(''); setBusy(false); }
  useEffect(() => { if (!active) invalidate(); return () => { request.current++; controller.current?.abort(); }; }, [active, user.id, user.role, user.organizationId]);
  async function trace(event: FormEvent) {
    event.preventDefault(); invalidate();
    const current = ++request.current, abort = new AbortController(); controller.current = abort; setBusy(true);
    try {
      const next = await api<LotTrace>('/api/ledger/trace', { method: 'POST', body: JSON.stringify({ lotId, direction, view }), signal: abort.signal });
      if (current !== request.current) return;
      if (!next.complete) throw new Error('Trace did not complete. No partial result is shown.');
      setResult(next); setSelectedId(next.targetLotId); setPage(1); setEdgePage(1);
    } catch (e) { if (current === request.current) setError(errorMessage(e)); }
    finally { if (current === request.current) setBusy(false); }
  }
  if (!canTrace) return null;
  const selected = result?.lots.find(lot => lot.id === selectedId);
  const lots = new Map(result?.lots.map(lot => [lot.id, lot]) ?? []);
  const operations = new Map(result?.operations.map(operation => [operation.id, operation]) ?? []);
  const sources = new Map(result?.sources.map(source => [source.lotId, source]) ?? []);
  return <>
    <div className="ledger-task-intro"><h2>Traceability</h2><p>Follow recorded lots through processing. Source shares describe accounting allocation; they do not identify individual grains.</p></div>
    <Feedback error={error} />
    <Panel title="Trace a lot">
      <form onSubmit={event => void trace(event)}>
        {!global && <LedgerLotPicker label="Starting lot" value={lotId} onChange={value => { invalidate(); setLotId(value); }} look={emptyLookup} disabled={!active} />}
        <label>Ledger lot ID<input required value={lotId} placeholder="Accepted Ledger lot UUID" onChange={event => { invalidate(); setLotId(event.target.value); }} /></label>
        <div className="ledger-form-grid">
          <label>Trace direction<select value={direction} onChange={event => { invalidate(); setDirection(event.target.value as 'forward' | 'backward'); }}><option value="forward">Forward — affected lots</option>{canSources && <option value="backward">Backward — contributing sources</option>}</select></label>
          <label>Trace view<select value={view} onChange={event => { invalidate(); setView(event.target.value as 'current' | 'history'); }}><option value="current">Current — effective operations</option><option value="history">History — includes superseded operations</option></select></label>
        </div>
        <p>Use a received lot. Procurement awaiting arrival has no Ledger stock graph yet. Refresh the trace to see newer stock or corrections.</p>
        <button disabled={busy || !active || !lotId}>{busy ? 'Tracing…' : 'Run trace'}</button>
      </form>
    </Panel>
    {result && <>
      <Panel title="Trace result">
        <p role="status">Complete {result.direction} trace: {result.lotCount} lots, {result.edgeCount} relationships. {result.view === 'history' ? 'Historical operations include superseded records.' : 'Superseded operations excluded.'}</p>
        <p>Snapshot: {new Date(result.asOf).toLocaleString()} · Reference: {result.traceId}</p>
        <p>Quantities at different processing stages overlap. Do not add every row as a total affected mass.</p>
        <div className="trace-display-options" role="group" aria-label="Trace result display"><button type="button" aria-pressed={display === 'table'} onClick={() => setDisplay('table')}>Table</button><button type="button" aria-pressed={display === 'flowchart'} onClick={() => setDisplay('flowchart')}>Flowchart</button></div>
        {display === 'flowchart' ? <TraceFlowchart key={result.traceId} trace={result} selectedId={selectedId} onSelect={setSelectedId} onShowTable={() => setDisplay('table')} /> : <>
        <div className="table-scroll"><table aria-label="Traced lots"><thead><tr><th>Lot</th><th>Stage</th><th>Original output (kg)</th><th>Current stock / available (kg)</th><th>Restrictions</th></tr></thead><tbody>{orderedLots.slice((page - 1) * 25, page * 25).map(lot => <tr key={lot.id}>
          <td><button type="button" aria-pressed={selectedId === lot.id} onClick={() => setSelectedId(lot.id)}>{lot.code}</button>{lot.id === result.targetLotId && <span className="trace-start-badge">Starting lot</span>}<small>{lot.id}</small></td><td>{lot.stage}</td><td>{lot.producedKg}</td>
          <td>{lot.positions.map(position => <p key={position.locationId}>{position.quantityKg} / {position.availableKg} · {position.locationId}</p>)}</td><td>{lot.restrictions.join(', ') || 'None'}</td>
        </tr>)}</tbody></table></div>
        <Pagination label="traced lots" page={page} pageSize={25} totalCount={result.lots.length} onChange={setPage} />
        </>}
      </Panel>
      {display === 'table' && <Panel title="Processing relationships">
        <p>Input consumed and output produced belong to the operation. They are not source-attributed kilograms for each edge. Transfers preserve lot identity.</p>
        {!result.edges.length ? <p>No processing relationships in this direction and view.</p> : <div className="table-scroll"><table aria-label="Processing relationships"><thead><tr><th>Input lot</th><th>Operation</th><th>Output lot</th><th>Input consumed (kg)</th><th>Output produced (kg)</th></tr></thead><tbody>{result.edges.slice((edgePage - 1) * 25, edgePage * 25).map(edge => <tr key={`${edge.operationId}/${edge.inputLotId}/${edge.outputLotId}`}>
          <td>{lots.get(edge.inputLotId)?.code || edge.inputLotId}</td><td>{operations.get(edge.operationId)?.kind} · {operations.get(edge.operationId)?.status}<small>{edge.operationId}</small></td><td>{lots.get(edge.outputLotId)?.code || edge.outputLotId}</td><td>{edge.inputConsumedKg}</td><td>{edge.outputProducedKg}</td>
        </tr>)}</tbody></table></div>}
        <Pagination label="trace relationships" page={edgePage} pageSize={25} totalCount={result.edges.length} onChange={setEdgePage} />
      </Panel>}
      {result.sourceContributionsVisible && selected && <Panel title={`Source composition — ${selected.code}`}>
        <p>Allocated output kg / original output kg; percentages are rounded for display. A zero allocation after rounding does not erase a recorded source relationship.</p>
        <div className="table-scroll"><table aria-label="Traced source contributions"><thead><tr><th>Source Ledger lot</th><th>Allocated kg / output kg</th><th>Share</th><th>Recorded origin</th></tr></thead><tbody>{selected.contributions?.map(contribution => {
          const source = sources.get(contribution.sourceLotId), origin = source?.provenance;
          return <tr key={contribution.sourceLotId}><td>{contribution.sourceLotId}</td><td>{contribution.quantityKg} / {contribution.denominatorKg}</td><td>{contribution.percentage}%</td><td>{origin ? <>
            <p>Procurement: {origin.procurementCode}</p><p>Farmer: {origin.farmerId}</p><p>Plot: {origin.plotId}</p><p>Season: {origin.seasonId}</p><p>Plot-season: {origin.plotSeasonId} · {origin.riceVariety}</p><p>Source expectation: {origin.originalValue} {origin.originalUnit} ({origin.expectedKg} kg)</p>
          </> : 'Legacy source details unavailable; no origin inferred.'}</td></tr>;
        })}</tbody></table></div>
      </Panel>}
      {!!result.corrections.length && <Panel title="Correction references"><p>Accounting replacements preserve history. These references are not reverse physical movements.</p>{result.corrections.map(correction => <p key={correction.id}>{correction.status}: {correction.originalOperationId} → {correction.replacementOperationId || 'Awaiting replacement'}</p>)}</Panel>}
    </>}
  </>;
}
