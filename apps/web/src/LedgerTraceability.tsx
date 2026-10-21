import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { api, errorMessage, type User } from './api';
import { Feedback, Pagination, Panel, emptyLookup } from './ledger-ui';
import { LedgerLotPicker } from './LedgerLotPicker';
import { TraceSourceEvidence } from './TraceSourceEvidence';
import { TraceFlowchart } from './TraceFlowchart';
import { traceLotOrder, traceRelationKey, traceRelationLabel } from './trace-flow';

type Contribution = { sourceLotId: string; quantityKg: string; denominatorKg: string; percentage: string };
type TraceLot = { directHold?: boolean; effectiveHold?: boolean; isCurrentPool?: boolean; poolRevision?: number | null; positivePathSourceIds?: string[] | null; id: string; code: string; stage: string; producedKg: string; restrictions: string[]; contributions: Contribution[] | null;
  positions: { locationId: string; quantityKg: string; availableKg: string }[] };
type Provenance = { sourceRecordId: string; procurementId: string; procurementItemId: string; procurementCode: string; farmerId: string; plotId: string; seasonId: string; plotSeasonId: string; riceVariety: string; expectedKg: string; originalValue: string; originalUnit: string; currentReferenceLabels?: { farmerCode: string | null; farmerName: string | null; plotCode: string | null; seasonName: string | null } };
export type TraceRelation = { relationType?: 'PROCESSING' | 'CUSTODY'; relationId?: string; eventId?: string | null; basis?: string;
  operationId: string | null; inputLotId: string; outputLotId: string; inputConsumedKg: string; outputProducedKg: string; inputEventKg?: string;
  sourceAllocations?: { sourceLotId: string; quantityKg: string }[] | null };
export type LotTrace = { schemaVersion?: number; graphBasis?: string; positivePathCoverage?: string;
  custodyEvents?: { id: string; action: 'INGRESS' | 'WITHDRAW'; locationId: string; revision: number; occurredAt: string; primaryOutputLotId: string }[]; traceId: string; targetLotId: string; direction: 'forward' | 'backward'; view: 'current' | 'history'; asOf: string; complete: boolean; lotCount: number; edgeCount: number;
  sourceContributionsVisible: boolean; lots: TraceLot[];
  edges: TraceRelation[];
  operations: { id: string; kind: string; status: string; occurredAt: string; declaredLossKg: string; varianceKg: string }[];
  corrections: { id: string; originalOperationId: string; replacementOperationId: string | null; status: string }[];
  sources: { lotId: string; status: string; provenance: Provenance | null }[] };

export function LedgerTraceability({ user, active = true, initialLotId }: { user: User; active?: boolean; initialLotId?: string }) {
  const [lotId, setLotId] = useState(initialLotId || ''), [direction, setDirection] = useState<'forward' | 'backward'>('forward'), [view, setView] = useState<'current' | 'history'>('current');
  const [result, setResult] = useState<LotTrace | null>(null), [selectedId, setSelectedId] = useState(''), [page, setPage] = useState(1), [edgePage, setEdgePage] = useState(1);
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  const [evidenceSource, setEvidenceSource] = useState<string | null>(null);
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
  function invalidate() { request.current++; controller.current?.abort(); setResult(null); setEvidenceSource(null); setError(''); setBusy(false); }
  useEffect(() => { invalidate(); return () => { request.current++; controller.current?.abort(); }; }, [active, user.id, user.role, user.organizationId, user.permissions.join(',')]);
  async function trace(event: FormEvent) {
    event.preventDefault(); invalidate();
    const current = ++request.current, abort = new AbortController(); controller.current = abort; setBusy(true);
    try {
      const next = await api<LotTrace>('/api/ledger/trace', { method: 'POST', body: JSON.stringify({ lotId, direction, view }), signal: abort.signal });
      if (current !== request.current) return;
      if (next.schemaVersion !== 2) throw new Error('The trace service requires an upgrade before this result can be displayed.');
      if (!next.complete) throw new Error('Trace did not complete. No partial result is shown.');
      setResult(next); setSelectedId(next.targetLotId); setPage(1); setEdgePage(1);
    } catch (e) { if (current === request.current) setError(errorMessage(e)); }
    finally { if (current === request.current) setBusy(false); }
  }
  if (!canTrace) return null;
  const selected = result?.lots.find(lot => lot.id === selectedId);
  const lots = new Map(result?.lots.map(lot => [lot.id, lot]) ?? []);
  const sources = new Map(result?.sources.map(source => [source.lotId, source]) ?? []);
  return <>
    <div className="ledger-task-intro"><h2>Traceability</h2><p>Follow recorded lots through storage and processing. Source shares describe accounting allocation; they do not identify individual grains.</p></div>
    <Feedback error={error} />
    <Panel title="Trace a lot">
      <form onSubmit={event => void trace(event)}>
        {!global && <LedgerLotPicker label="Starting lot" availableOnly={false} required={false} allowHistorical value={lotId} onChange={value => { invalidate(); setLotId(value); }} look={emptyLookup} disabled={!active} />}
        <label>Ledger lot ID<input required value={lotId} placeholder="Accepted Ledger lot UUID" onChange={event => { invalidate(); setLotId(event.target.value); }} /></label>
        <div className="ledger-form-grid">
          <label>Trace direction<select value={direction} onChange={event => { invalidate(); setDirection(event.target.value as 'forward' | 'backward'); }}><option value="forward">Forward — recorded descendants</option>{canSources && <option value="backward">Backward — contributing sources</option>}</select></label>
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
        <p>Storage generations and processing stages overlap. Do not add every row as a total mass. Recorded relationships do not establish contamination or compliance.</p>
        <div className="trace-display-options" role="group" aria-label="Trace result display"><button type="button" aria-pressed={display === 'table'} onClick={() => setDisplay('table')}>Table</button><button type="button" aria-pressed={display === 'flowchart'} onClick={() => setDisplay('flowchart')}>Flowchart</button></div>
        {display === 'flowchart' ? <TraceFlowchart key={result.traceId} trace={result} selectedId={selectedId} onSelect={id => { setSelectedId(id); setEvidenceSource(null); }} onShowTable={() => setDisplay('table')} /> : <>
        <div className="table-scroll"><table aria-label="Traced lots"><thead><tr><th>Lot</th><th>Stage</th><th>Quantity when created (kg)</th><th>Current stock / available (kg)</th><th>Restrictions</th></tr></thead><tbody>{orderedLots.slice((page - 1) * 25, page * 25).map(lot => <tr key={lot.id}>
          <td><button type="button" aria-pressed={selectedId === lot.id} onClick={() => { setSelectedId(lot.id); setEvidenceSource(null); }}>{lot.code}</button>{lot.id === result.targetLotId && <span className="trace-start-badge">Starting lot</span>}<small>{lot.id}</small></td><td>{lot.stage}{lot.poolRevision != null && <small>{lot.isCurrentPool ? 'Current pool' : 'Custody generation'} · revision {lot.poolRevision}</small>}</td><td>{lot.producedKg}</td>
          <td>{lot.positions.map(position => <p key={position.locationId}>{position.quantityKg} / {position.availableKg} · {position.locationId}</p>)}</td><td>{lot.restrictions.join(', ') || 'None'}{lot.effectiveHold && <small>{lot.directHold ? 'HOLD placed on this lot' : 'HOLD inherited through recorded source ancestry'}</small>}</td>
        </tr>)}</tbody></table></div>
        <Pagination label="traced lots" page={page} pageSize={25} totalCount={result.lots.length} onChange={setPage} />
        </>}
      </Panel>
      {display === 'table' && <Panel title="Storage and processing relationships">
        <p>Processing quantities belong to the operation. Custody input kg is the mass on that edge; output kg is the whole lot created. Source allocations are separate. Ordinary transfers preserve lot identity.</p>
        {!result.edges.length ? <p>No material relationships in this direction and view.</p> : <div className="table-scroll"><table aria-label="Storage and processing relationships"><thead><tr><th>Input lot</th><th>Storage event / operation</th><th>Output lot</th><th>Input / custody edge (kg)</th><th>Output lot created (kg)</th></tr></thead><tbody>{result.edges.slice((edgePage - 1) * 25, edgePage * 25).map(edge => <tr key={`${traceRelationKey(edge)}/${edge.inputLotId}/${edge.outputLotId}`}>
          <td>{lots.get(edge.inputLotId)?.code || edge.inputLotId}</td><td>{traceRelationLabel(result, edge)}<small>{edge.relationId || edge.operationId}</small></td><td>{lots.get(edge.outputLotId)?.code || edge.outputLotId}</td><td>{edge.inputConsumedKg}</td><td>{edge.outputProducedKg}</td>
        </tr>)}</tbody></table></div>}
        <Pagination label="trace relationships" page={edgePage} pageSize={25} totalCount={result.edges.length} onChange={setEdgePage} />
      </Panel>}
      {result.sourceContributionsVisible && selected && <Panel title={`Source composition — ${selected.code}`}>
        {result.schemaVersion === 2 && <p>Positive source paths from the starting lot: {selected.positivePathSourceIds?.length || 0} source(s) established by persisted allocations. {result.positivePathCoverage === 'PARTIAL_LEGACY' ? 'Legacy edges have no persisted allocation detail; missing paths are not proof of absence.' : 'An empty set means no positive source path from the starting lot is recorded for this node.'} This is not a quality verdict.</p>}
        <p>Allocated output kg / original output kg; percentages are rounded for display. A zero allocation after rounding does not erase a recorded source relationship.</p>
        <div className="table-scroll"><table aria-label="Traced source contributions"><thead><tr><th>Source Ledger lot</th><th>Allocated kg / output kg</th><th>Share</th><th>Recorded origin</th></tr></thead><tbody>{selected.contributions?.map(contribution => {
          const source = sources.get(contribution.sourceLotId), origin = source?.provenance;
          const labels = origin?.currentReferenceLabels;
          return <tr key={contribution.sourceLotId}><td>{lots.get(contribution.sourceLotId)?.code || contribution.sourceLotId}<small>{lots.has(contribution.sourceLotId) ? contribution.sourceLotId : ''}</small></td><td>{contribution.quantityKg} / {contribution.denominatorKg}</td><td>{contribution.percentage}%</td><td>{origin ? <>
            <p>Procurement: {origin.procurementCode}</p>
            {user.role === 'CustomerManager' && user.permissions.some(permission => permission === 'lab.read' || permission === 'lab.write') && <button type="button" onClick={() => setEvidenceSource(origin.sourceRecordId)}>View samples and laboratory evidence</button>}
            <p>Plot: {labels?.plotCode || origin.plotId} · Season: {labels?.seasonName || origin.seasonId}</p>
            <details className="trace-origin"><summary>View field / season origin — {origin.procurementCode}</summary>
              <p>Origin of the selected lot: allocated {contribution.quantityKg} / {contribution.denominatorKg} kg ({contribution.percentage}%).</p>
              <ol className="trace-origin-chain" aria-label={`Recorded origin for ${contribution.sourceLotId}`}>
                <li><strong>Farmer</strong><p>{labels?.farmerName || 'Name unavailable'}{labels?.farmerCode ? ` · ${labels.farmerCode}` : ''}</p><small>{origin.farmerId}</small></li>
                <li><strong>Plot / crop season</strong><p>Plot: {labels?.plotCode || 'Code unavailable'} · Season: {labels?.seasonName || 'Name unavailable'}</p><p>Recorded variety: {origin.riceVariety}</p><small>Plot: {origin.plotId}<br />Season: {origin.seasonId}<br />Plot-season: {origin.plotSeasonId}</small></li>
                <li><strong>Procurement</strong><p>{origin.procurementCode}</p><p>Registered source expectation: {origin.originalValue} {origin.originalUnit} ({origin.expectedKg} kg). This is not the arrival measurement or this lot's allocated mass.</p><small>Procurement: {origin.procurementId}<br />Item: {origin.procurementItemId}<br />Source record: {origin.sourceRecordId}</small></li>
                <li><strong>Source Ledger lot</strong><p>{lots.get(contribution.sourceLotId)?.code || contribution.sourceLotId}</p><small>{contribution.sourceLotId}</small></li>
              </ol>
              <p>Origin IDs, variety and source expectation come from the committed procurement record. Names and codes from reference records are current labels, including retained archived records; they are not historical registration snapshots.</p>
              <p>This chain identifies the recorded source. Registration audit and changes to field records are not included.</p>
            </details>
          </> : 'Legacy source details unavailable; no origin inferred.'}</td></tr>;
        })}</tbody></table></div>
      </Panel>}
      {evidenceSource && result.sourceContributionsVisible && user.role === 'CustomerManager' && user.permissions.some(permission => permission === 'lab.read' || permission === 'lab.write') && <TraceSourceEvidence key={`${user.id}:${user.organizationId}:${evidenceSource}`} sourceRecordId={evidenceSource} onClose={() => setEvidenceSource(null)} />}
      {!!result.corrections.length && <Panel title="Correction references"><p>Accounting replacements preserve history. These references are not reverse physical movements.</p>{result.corrections.map(correction => <p key={correction.id}>{correction.status}: {correction.originalOperationId} → {correction.replacementOperationId || 'Awaiting replacement'}</p>)}</Panel>}
    </>}
  </>;
}
