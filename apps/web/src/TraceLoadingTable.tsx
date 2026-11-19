import { useState } from 'react';
import type { TraceGraph } from './LedgerTraceability';
import { Pagination } from './ledger-ui';

export function TraceLoadingTable({ trace }: { trace: TraceGraph }) {
  const [page, setPage] = useState(1);
  const edges = trace.loadingEdges || [];
  const lots = new Map(trace.lots.map(lot => [lot.id, lot.code]));
  const containers = new Map(trace.actualContainers?.map(container => [container.containerId, container.code]));
  return <section><h4>Actual loading relationships</h4>
    <p>Actual loading kg belong to these lot → container links. They are separate from historical produced kg and individual source contributions.</p>
    {edges.length ? <><div className="table-scroll"><table aria-label="Trace actual loading"><thead><tr><th>Lot → container</th><th>Actual kg</th><th>Committed load / location / proposal</th></tr></thead><tbody>{edges.slice((page - 1) * 25, page * 25).map(edge => <tr key={`${edge.lotId}/${edge.containerId}`}><td>{lots.get(edge.lotId) || edge.lotId} → {containers.get(edge.containerId) || edge.containerId}</td><td>{edge.quantityKg}</td><td>{edge.lines.map(line => <details key={line.lineId}><summary>{line.quantityKg} kg · load {line.loadId}</summary><p>Line {line.lineId} · location {line.locationId} · proposal {line.proposalId} · {new Date(line.loadedAt).toLocaleString()}</p>{line.sourceContributions?.map(source => <p key={source.sourceLotId}>Source {source.sourceLotId}: {source.quantityKg} kg actual contribution</p>)}</details>)}</td></tr>)}</tbody></table></div><Pagination label="actual loading relationships" page={page} pageSize={25} totalCount={edges.length} onChange={setPage} /></> : <p>No actual loading relationships recorded in this trace.</p>}
  </section>;
}
