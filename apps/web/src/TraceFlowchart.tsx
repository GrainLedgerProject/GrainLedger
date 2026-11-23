import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { traceKindLabel } from './trace-kind';
import type { TraceGraph } from './LedgerTraceability';
import { buildTraceFlow, flowCard, traceRelationKey, traceRelationLabel } from './trace-flow';

export function TraceFlowchart({ trace, selectedId, onSelect, onShowTable, canOpenContainer = true }: {
  trace: TraceGraph; selectedId: string; onSelect: (id: string) => void; onShowTable: () => void; canOpenContainer?: boolean;
}) {
  const flow = useMemo(() => buildTraceFlow(trace), [trace]);
  const lots = useMemo(() => new Map(trace.lots.map(lot => [lot.id, lot])), [trace]);
  const operations = useMemo(() => new Map(trace.operations.map(operation => [operation.id, operation])), [trace]);
  const containers = useMemo(() => new Map(trace.actualContainers?.map(container => [container.containerId, container])), [trace]);
  const containerRoot = trace.root?.type === 'container';
  const rootId = containerRoot ? `container:${trace.root!.id}` : `lot:${trace.targetLotId}`;
  const positions = useMemo(() => new Map(flow.nodes.map(node => [node.id, node])), [flow]);
  const [zoom, setZoom] = useState(1);
  const viewport = useRef<HTMLDivElement>(null), startingLot = useRef<HTMLButtonElement | HTMLAnchorElement>(null);
  const marker = useId().replaceAll(':', ''), helpId = `${marker}-help`;
  useEffect(() => {
    const node = positions.get(rootId), area = viewport.current;
    if (node && area) {
      area.scrollLeft = Math.max(0, node.x + flowCard.width / 2 - area.clientWidth / 2);
      area.scrollTop = Math.max(0, node.y + flowCard.height / 2 - area.clientHeight / 2);
    }
  }, [positions, rootId]);
  if (flow.error) return <div className="notice" role="status"><p>{flow.error}</p><button type="button" onClick={onShowTable}>Show table</button></div>;
  function focusStart() {
    const node = positions.get(rootId), area = viewport.current;
    if (!node || !area) return;
    startingLot.current?.focus({ preventScroll: true });
    area.scrollLeft = Math.max(0, (node.x + flowCard.width / 2) * zoom - area.clientWidth / 2);
    area.scrollTop = Math.max(0, (node.y + flowCard.height / 2) * zoom - area.clientHeight / 2);
  }
  const selected = lots.get(selectedId);
  return <div className="trace-flow">
    <p id={helpId}>Material flows left to right: lot → storage event or processing operation → lot → actual loading → container. {containerRoot ? 'Backward tracing starts at this container and shows the ancestry of its actual loaded lots to the left.' : trace.direction === 'backward' ? 'Backward tracing shows earlier inputs to the left of your starting lot.' : 'Forward tracing shows later outputs and actual container targets to the right of your starting lot.'} Only lots in this trace are shown; an operation may also have other inputs or outputs. Select a lot to inspect it below.{canOpenContainer && ' Open a container to view its details.'}</p>
    {containerRoot && <p className="notice">Lot and processing quantities describe historical production. Loading edges describe actual container kg. Source contributions to this container are shown separately in Actual container sources; do not add the historical lot quantities as loaded mass.</p>}
    <div className="trace-flow-toolbar" role="group" aria-label="Flowchart controls">
      <button type="button" aria-label="Zoom out" disabled={zoom <= 0.4} onClick={() => setZoom(value => Math.max(0.4, value - 0.2))}>−</button>
      <output aria-label="Graph zoom">{Math.round(zoom * 100)}%</output>
      <button type="button" aria-label="Zoom in" disabled={zoom >= 1.6} onClick={() => setZoom(value => Math.min(1.6, value + 0.2))}>+</button>
      <button type="button" onClick={() => {
        const area = viewport.current;
        if (!area) return;
        const style = getComputedStyle(area);
        const verticalBorders = style.boxSizing === 'border-box'
          ? (parseFloat(style.borderTopWidth) || 0) + (parseFloat(style.borderBottomWidth) || 0) : 0;
        const maximumHeight = parseFloat(style.maxHeight) || area.clientHeight || flow.height;
        setZoom(Math.min(1, (area.clientWidth || flow.width) / flow.width, (maximumHeight - verticalBorders) / flow.height));
        area.scrollLeft = 0;
        area.scrollTop = 0;
      }}>Fit graph</button>
      <button type="button" onClick={() => { setZoom(1); }}>Reset zoom</button>
      <button type="button" onClick={focusStart}>Focus starting {containerRoot ? 'container' : 'lot'}</button>
    </div>
    <div className="trace-flow-legend"><span className="trace-flow-lot-key">Lot</span><span className="trace-flow-operation-key">Processing / custody</span><span className="trace-flow-container-key">Container · actual loading</span><span className="trace-flow-start-key">Starting {containerRoot ? 'container' : 'lot'}</span><span>Badges show recorded restrictions, not a quality verdict.</span></div>
    <div className="trace-flow-viewport" ref={viewport} role="region" aria-label="Trace flowchart" aria-describedby={helpId} tabIndex={0}>
      <div style={{ width: flow.width * zoom, height: flow.height * zoom, overflow: 'clip' }}>
        <div className="trace-flow-canvas" style={{ width: flow.width, height: flow.height, transform: `scale(${zoom})` }}>
          <svg width={flow.width} height={flow.height} aria-hidden="true" className="trace-flow-lines">
            <defs><marker id={marker} markerWidth="9" markerHeight="9" refX="8" refY="4.5" orient="auto"><path d="M0,0 L9,4.5 L0,9 Z" fill="currentColor" /></marker></defs>
            {flow.edges.map((edge, index) => {
              const from = positions.get(edge.from)!, to = positions.get(edge.to)!;
              const x1 = from.x + flowCard.width, y1 = from.y + flowCard.height / 2, x2 = to.x - 5, y2 = to.y + flowCard.height / 2;
              const bend = Math.max(36, (x2 - x1) / 2);
              // Skip-level inputs travel above the cards rather than through an unrelated
              // intermediate operation (e.g. remaining source stock rejoining a later batch).
              const lane = 10 + (index % 3) * 6;
              const path = to.x - from.x > flowCard.width + flowCard.columnGap + 1
                ? `M${x1},${y1} C${x1 + 24},${y1} ${x1 + 24},${lane} ${x1 + 48},${lane} H${x2 - 48} C${x2 - 24},${lane} ${x2 - 24},${y2} ${x2},${y2}`
                : `M${x1},${y1} C${x1 + bend},${y1} ${x2 - bend},${y2} ${x2},${y2}`;
              return <g key={edge.id}><path className={`trace-flow-edge${edge.kind === 'loading' ? ' trace-flow-loading-edge' : ''}`} d={path} markerEnd={`url(#${marker})`}><title>{edge.quantityKg} kg — {edge.kind === 'loading' ? 'actual loading, not source attribution' : 'event quantity, not source attribution'}</title></path>{edge.kind === 'loading' && <text x={(x1 + x2) / 2} y={to.x - from.x > flowCard.width + flowCard.columnGap + 1 ? lane - 2 : y2 - 12} textAnchor="middle" className="trace-flow-edge-label">{edge.quantityKg} kg loaded</text>}</g>;
            })}
          </svg>
          {flow.nodes.map(node => {
            const style = { left: node.x, top: node.y, width: flowCard.width, height: flowCard.height };
            if (node.kind === 'container') {
              const container = containers.get(node.recordId)!, start = containerRoot && trace.root!.id === node.recordId;
              const className = `trace-flow-node trace-flow-container${start ? ' trace-flow-start' : ''}`;
              const restrictions = [container.held && 'Container HOLD', container.restricted && 'Loaded lot restriction'].filter(Boolean).join(' · ');
              const content = <>
                {start && <span className="trace-flow-kicker">STARTING CONTAINER</span>}<strong>{container.code}</strong>
                <span>{container.status} · {container.linkedLoadedKg} kg linked actual loading</span>
                <span className={restrictions ? 'trace-flow-restriction' : 'trace-flow-clear'}>{restrictions || 'No recorded restrictions'}</span>
              </>;
              return canOpenContainer
                ? <a key={node.id} ref={element => { if (start) startingLot.current = element; }} href={`/export/containers/${container.containerId}`} style={style}
                    className={className} aria-label={`Open container ${container.code}${start ? ' — starting container' : ''}`}>{content}</a>
                : <div key={node.id} style={style} className={className}>{content}</div>;
            }
            if (node.kind === 'custody') {
              const event = trace.custodyEvents?.find(event => event.id === node.recordId);
              return <div key={node.id} className="trace-flow-node trace-flow-operation trace-flow-custody" style={style} role="group" aria-label={`${event?.action || 'Custody'} storage event ${node.recordId}`}>
                <span className="trace-flow-kicker">STORAGE</span><strong>{event?.action === 'INGRESS' ? 'Pool ingress' : 'Withdrawal / remainder'}</strong>
                <span>Revision {event?.revision} · {event ? new Date(event.occurredAt).toLocaleString() : 'Recorded'}</span>
                <small title={event?.locationId}>Location: {event?.locationId}</small>
              </div>;
            }
            if (node.kind === 'operation') {
              const operation = operations.get(node.recordId);
              return <div key={node.id} className="trace-flow-node trace-flow-operation" style={style} role="group" aria-label={`${operation?.kind || 'Processing'} operation ${node.recordId}`}>
                <span className="trace-flow-kicker">PROCESSING</span><strong>{operation?.kind || 'Processing'}</strong><span>{operation?.status || 'Recorded'}</span>
                <small title={node.recordId}>{node.recordId}</small>
              </div>;
            }
            const lot = lots.get(node.recordId)!, start = lot.id === trace.targetLotId;
            return <button type="button" key={node.id} ref={element => { if (start) startingLot.current = element; }} style={style}
              className={`trace-flow-node trace-flow-lot${start ? ' trace-flow-start' : ''}`}
              aria-label={`Select lot ${lot.code}${start ? ' — starting lot' : ''}`} aria-pressed={lot.id === selectedId} onClick={() => onSelect(lot.id)} title={`${lot.code} · ${lot.id}`}>
              <span className="trace-flow-kicker">{start ? 'STARTING LOT · ' : ''}{traceKindLabel(lot)}</span><strong>{lot.code}</strong><span>{lot.stage} · {lot.producedKg} kg created{lot.poolRevision != null ? ` · ${lot.isCurrentPool ? 'Current pool' : 'Generation'} r${lot.poolRevision}` : ''}</span>
              <span className={lot.restrictions.length ? 'trace-flow-restriction' : 'trace-flow-clear'}>{lot.restrictions.join(' · ') || 'No recorded restrictions'}</span>
              <small title={lot.id}>{lot.positions.map(p => p.locationName || p.locationId).join(' · ') || lot.id}</small>
            </button>;
          })}
        </div>
      </div>
    </div>
    {!trace.edges.length && !trace.loadingEdges?.length && <p>{containerRoot ? 'No actual loading recorded. The starting container is shown on its own.' : 'No material relationships in this direction and view. The starting lot is shown on its own.'}</p>}
    {selected && <section className="trace-flow-selection" aria-label="Selected graph lot" aria-live="polite">
      <strong>{selected.code}</strong>{selected.id === trace.targetLotId && <span className="trace-start-badge">Starting lot</span>}
      <p>{selected.stage} · Quantity when created: {selected.producedKg} kg · {selected.restrictions.join(', ') || 'No recorded restrictions'}</p>
      <small>{selected.id}</small>
      {selected.positions.map(position => <p key={position.locationId}>Current stock: {position.quantityKg} kg · Available: {position.availableKg} kg · {position.locationName || position.locationId}</p>)}
    </section>}
    <details className="trace-flow-quantities"><summary>Event quantities and exact relationships</summary><p>Processing amounts belong to each operation input/output; custody input is edge mass and output is whole created lot; do not sum repeated output amounts across input–output pairs.</p>
      <ul>{trace.edges.map(edge => <li key={`${traceRelationKey(edge)}/${edge.inputLotId}/${edge.outputLotId}`}>
        {lots.get(edge.inputLotId)?.code} → {traceRelationLabel(trace, edge)} → {lots.get(edge.outputLotId)?.code}: input {edge.inputConsumedKg} kg; output {edge.outputProducedKg} kg. <small>{edge.relationId || edge.operationId}</small>
      </li>)}</ul>
      {!!trace.loadingEdges?.length && <><p>Actual loading is separate from historical lot quantities and individual source contributions. Linked loading may cover only part of a container manifest.</p><ul>{trace.loadingEdges.map(edge => <li key={`${edge.lotId}/${edge.containerId}`}>
        {lots.get(edge.lotId)?.code} → {containers.get(edge.containerId)?.code}: {edge.quantityKg} kg actual loading.
        <ul>{edge.lines.map(line => <li key={line.lineId}>{line.quantityKg} kg · load {line.loadId} · location {line.locationId} · proposal {line.proposalId}{line.sourceContributions?.map(source => <span key={source.sourceLotId}> · source {source.sourceLotId}: {source.quantityKg} kg</span>)}</li>)}</ul>
      </li>)}</ul></>}
    </details>
  </div>;
}
