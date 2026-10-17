import type { LotTrace } from './LedgerTraceability';

export type FlowNode = { id: string; kind: 'lot' | 'operation'; recordId: string; x: number; y: number };
export type FlowEdge = { id: string; from: string; to: string; quantityKg: string };
export type TraceFlow = { nodes: FlowNode[]; edges: FlowEdge[]; width: number; height: number; error?: string };
export const flowCard = { width: 236, height: 148, columnGap: 96, rowGap: 32, padding: 28 };
export const flowLimit = { nodes: 250, relationships: 500 };

// Only graph geometry uses JS numbers. Mass strings are carried unchanged from the API.
export function traceLotOrder(trace: LotTrace): string[] {
  const byId = new Map(trace.lots.map(lot => [lot.id, lot]));
  const next = new Map<string, Set<string>>();
  for (const edge of trace.edges) {
    const from = trace.direction === 'forward' ? edge.inputLotId : edge.outputLotId;
    const to = trace.direction === 'forward' ? edge.outputLotId : edge.inputLotId;
    if (!next.has(from)) next.set(from, new Set());
    next.get(from)!.add(to);
  }
  const compare = (a: string, b: string) => (byId.get(a)?.code || a).localeCompare(byId.get(b)?.code || b) || a.localeCompare(b);
  const result = [trace.targetLotId], seen = new Set(result);
  for (let cursor = 0; cursor < result.length; cursor++) {
    for (const id of [...(next.get(result[cursor]) || [])].sort(compare)) {
      if (byId.has(id) && !seen.has(id)) { seen.add(id); result.push(id); }
    }
  }
  return [...result.filter(id => byId.has(id)), ...trace.lots.map(lot => lot.id).filter(id => !seen.has(id)).sort(compare)];
}

export function buildTraceFlow(trace: LotTrace): TraceFlow {
  const unavailable = (error: string): TraceFlow => ({ nodes: [], edges: [], width: 0, height: 0, error });
  const operationIds = new Set(trace.edges.map(edge => edge.operationId));
  if (trace.lots.length + operationIds.size > flowLimit.nodes || trace.edges.length > flowLimit.relationships)
    return unavailable(`Flowchart supports up to ${flowLimit.nodes} lot/operation nodes and ${flowLimit.relationships} relationships. The complete result remains available in Table view.`);
  const lotIds = new Set(trace.lots.map(lot => lot.id));
  const nodes: FlowNode[] = traceLotOrder(trace).map(id => ({ id: `lot:${id}`, kind: 'lot', recordId: id, x: 0, y: 0 }));
  for (const id of [...operationIds].sort()) nodes.push({ id: `operation:${id}`, kind: 'operation', recordId: id, x: 0, y: 0 });
  const connections = new Map<string, FlowEdge>();
  for (const edge of trace.edges) {
    if (!lotIds.has(edge.inputLotId) || !lotIds.has(edge.outputLotId))
      return unavailable('Some relationship endpoints are missing. Use Table view to inspect the returned result.');
    const input = `lot:${edge.inputLotId}`, operation = `operation:${edge.operationId}`, output = `lot:${edge.outputLotId}`;
    // An operation's input/output appears once even when the API contains every input/output pair.
    connections.set(`${input}/${operation}`, { id: `${input}/${operation}`, from: input, to: operation, quantityKg: edge.inputConsumedKg });
    connections.set(`${operation}/${output}`, { id: `${operation}/${output}`, from: operation, to: output, quantityKg: edge.outputProducedKg });
  }
  const edges = [...connections.values()];
  const outgoing = new Map(nodes.map(node => [node.id, [] as string[]]));
  const incoming = new Map(nodes.map(node => [node.id, 0]));
  const levels = new Map(nodes.map(node => [node.id, 0]));
  for (const edge of edges) {
    outgoing.get(edge.from)!.push(edge.to);
    incoming.set(edge.to, incoming.get(edge.to)! + 1);
  }
  const queue = nodes.filter(node => incoming.get(node.id) === 0).map(node => node.id);
  for (let cursor = 0; cursor < queue.length; cursor++) {
    const id = queue[cursor];
    for (const target of outgoing.get(id)!) {
      levels.set(target, Math.max(levels.get(target)!, levels.get(id)! + 1));
      incoming.set(target, incoming.get(target)! - 1);
      if (incoming.get(target) === 0) queue.push(target);
    }
  }
  if (queue.length !== nodes.length) return unavailable('Flowchart cannot display cyclic relationships. Use Table view to inspect the returned result.');
  const columns = new Map<number, FlowNode[]>();
  for (const node of nodes) {
    const level = levels.get(node.id)!;
    if (!columns.has(level)) columns.set(level, []);
    columns.get(level)!.push(node);
  }
  const maxRows = Math.max(1, ...[...columns.values()].map(column => column.length));
  const { width, height, columnGap, rowGap, padding } = flowCard;
  const chartHeight = padding * 2 + maxRows * (height + rowGap) - rowGap;
  for (const [level, column] of columns) {
    const offset = (chartHeight - (column.length * (height + rowGap) - rowGap)) / 2;
    column.forEach((node, index) => { node.x = padding + level * (width + columnGap); node.y = offset + index * (height + rowGap); });
  }
  return { nodes, edges, width: padding * 2 + (Math.max(0, ...columns.keys()) + 1) * (width + columnGap) - columnGap, height: chartHeight };
}
