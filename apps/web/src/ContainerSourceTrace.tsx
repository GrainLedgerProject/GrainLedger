import { useState } from 'react';
import type { TraceGraph } from './LedgerTraceability';
import { TraceFlowchart } from './TraceFlowchart';
import { traceRelationKey, traceRelationLabel } from './trace-flow';
import { Pagination } from './ledger-ui';
import { TraceLoadingTable } from './TraceLoadingTable';

export type ContainerTrace = { loadedKg: string; complete: boolean; findings?: string[]; graph?: TraceGraph;
  sourceContributions: { sourceLotId: string; quantityKg: string; percentage?: string; status?: string;
    provenance?: { plotId: string; procurementCode: string } | null }[] };

export function ContainerSourceTrace({ trace }: { trace: ContainerTrace }) {
  const [display, setDisplay] = useState<'table' | 'flowchart'>('table');
  const [selectedId, setSelectedId] = useState('');
  const [lotPage, setLotPage] = useState(1), [edgePage, setEdgePage] = useState(1);
  const graph = trace.graph;
  const lots = new Map(graph?.lots.map(lot => [lot.id, lot]));
  return <section className="container-source-trace"><h3>Actual container sources</h3>
    <p>Actual loaded: {trace.loadedKg} kg. Source kg below belong to this container's committed manifest, including earlier plans.</p>
    {!trace.complete && <p className="notice error">Container source trace is incomplete: {trace.findings?.join(', ') || 'source provenance unavailable'}. Review the recorded facts before drawing conclusions.</p>}
    <div className="table-scroll"><table aria-label="Actual container sources"><thead><tr><th>Source</th><th>Actual kg</th><th>Share (%)</th><th>Plot / provenance</th></tr></thead><tbody>{trace.sourceContributions.map(source => <tr key={source.sourceLotId}><td>{lots.get(source.sourceLotId)?.code || source.sourceLotId}</td><td>{source.quantityKg}</td><td>{source.percentage ?? '—'}</td><td>{source.provenance?.plotId ?? source.status ?? 'Unavailable'}</td></tr>)}</tbody></table></div>
    {graph ? <>
      <p>Snapshot: {new Date(graph.asOf).toLocaleString()}. Historical production kg and actual container loading kg are different quantities.</p>
      <div className="trace-display-options" role="group" aria-label="Container trace display"><button type="button" aria-pressed={display === 'table'} onClick={() => setDisplay('table')}>Table</button><button type="button" aria-pressed={display === 'flowchart'} onClick={() => setDisplay('flowchart')}>Flowchart</button></div>
      {display === 'flowchart' ? <TraceFlowchart trace={graph} selectedId={selectedId} onSelect={setSelectedId} onShowTable={() => setDisplay('table')} /> : <>
        <h4>Historical lots in container ancestry</h4><div className="table-scroll"><table aria-label="Container ancestry lots"><thead><tr><th>Lot</th><th>Stage</th><th>Historical created kg</th><th>Restrictions</th></tr></thead><tbody>{graph.lots.slice((lotPage - 1) * 25, lotPage * 25).map(lot => <tr key={lot.id}><td>{lot.code}<small>{lot.id}</small></td><td>{lot.stage}</td><td>{lot.producedKg}</td><td>{lot.restrictions.join(', ') || 'None'}</td></tr>)}</tbody></table></div>
        <Pagination label="container ancestry lots" page={lotPage} pageSize={25} totalCount={graph.lots.length} onChange={setLotPage} />
        <h4>Historical material relationships</h4><div className="table-scroll"><table aria-label="Container ancestry relationships"><thead><tr><th>Input lot</th><th>Operation / storage event</th><th>Output lot</th><th>Input / edge kg</th><th>Whole output created kg</th></tr></thead><tbody>{graph.edges.slice((edgePage - 1) * 25, edgePage * 25).map(edge => <tr key={`${traceRelationKey(edge)}/${edge.inputLotId}/${edge.outputLotId}`}><td>{lots.get(edge.inputLotId)?.code || edge.inputLotId}</td><td>{traceRelationLabel(graph, edge)}</td><td>{lots.get(edge.outputLotId)?.code || edge.outputLotId}</td><td>{edge.inputConsumedKg}</td><td>{edge.outputProducedKg}</td></tr>)}</tbody></table></div>
        <Pagination label="container ancestry relationships" page={edgePage} pageSize={25} totalCount={graph.edges.length} onChange={setEdgePage} />
        <TraceLoadingTable trace={graph} />
      </>}
    </> : <p>Flowchart is unavailable until the container tracing upgrade is installed.</p>}
  </section>;
}
