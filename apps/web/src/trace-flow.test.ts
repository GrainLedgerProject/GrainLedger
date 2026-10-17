import { expect, it } from 'vitest';
import { buildTraceFlow, flowLimit, traceLotOrder } from './trace-flow';
import type { LotTrace } from './LedgerTraceability';

const lot = (id: string) => ({ id, code: id.toUpperCase(), stage: 'DRIED_PADDY', producedKg: '0.000001', positions: [], restrictions: [], contributions: null });
const edge = (operationId: string, inputLotId: string, outputLotId: string, inputConsumedKg: string, outputProducedKg: string) => ({ operationId, inputLotId, outputLotId, inputConsumedKg, outputProducedKg });
const trace: LotTrace = { traceId: 'fixture', targetLotId: 'a', direction: 'forward', view: 'current', complete: true, asOf: '2026-10-17T00:00:00Z', lotCount: 4, edgeCount: 4, sourceContributionsVisible: false,
  lots: ['d', 'b', 'a', 'c'].map(lot), operations: [], corrections: [], sources: [],
  edges: [edge('split', 'a', 'b', '10.000001', '6.000001'), edge('split', 'a', 'c', '10.000001', '4.000000'), edge('join', 'b', 'd', '6.000001', '10.000001'), edge('join', 'c', 'd', '4.000000', '10.000001')] };

it('keeps split and rejoin relationships with one shared operation node and one connection per actual input/output', () => {
  const flow = buildTraceFlow(trace);
  expect(flow.error).toBeUndefined(); expect(flow.nodes).toHaveLength(6); expect(flow.edges).toHaveLength(6);
  expect(flow.edges.filter(e => e.from === 'lot:a' && e.to === 'operation:split')).toHaveLength(1);
  expect(flow.edges.filter(e => e.from === 'operation:join' && e.to === 'lot:d')).toHaveLength(1);
  expect(flow.edges.find(e => e.from === 'lot:a')?.quantityKg).toBe('10.000001');
  const nodes = new Map(flow.nodes.map(node => [node.id, node]));
  for (const connection of flow.edges) expect(nodes.get(connection.from)!.x).toBeLessThan(nodes.get(connection.to)!.x);
  expect(nodes.get('lot:b')!.y).not.toBe(nodes.get('lot:c')!.y);
});

it('orders the selected lot first in both directions while keeping physical arrows input to output', () => {
  expect(traceLotOrder(trace)).toEqual(['a', 'b', 'c', 'd']);
  const backward = { ...trace, targetLotId: 'd', direction: 'backward' as const };
  expect(traceLotOrder(backward)).toEqual(['d', 'b', 'c', 'a']);
  const flow = buildTraceFlow(backward);
  expect(flow.nodes.find(n => n.recordId === 'd')!.x).toBeGreaterThan(flow.nodes.find(n => n.recordId === 'a')!.x);
  expect(flow.edges.some(e => e.from === 'lot:d')).toBe(false);
});

it('keeps zero-attributed lots and does not create correction or transfer edges', () => {
  const withCorrection = { ...trace, lots: trace.lots.map(l => ({ ...l, contributions: [{ sourceLotId: 'zero-source', quantityKg: '0.000000', denominatorKg: '0.000001', percentage: '0.000000' }] })),
    corrections: [{ id: 'correction', originalOperationId: 'split', replacementOperationId: 'replacement', status: 'COMMITTED' }] };
  expect(buildTraceFlow(withCorrection).nodes.filter(n => n.kind === 'lot')).toHaveLength(4);
  expect(buildTraceFlow(withCorrection).nodes.some(n => n.recordId === 'replacement')).toBe(false);
  expect(buildTraceFlow({ ...trace, lots: [lot('a')], edges: [] }).nodes).toHaveLength(1);
});

it('rejects oversized or malformed diagrams explicitly instead of returning a partial graph', () => {
  expect(buildTraceFlow({ ...trace, lots: Array.from({ length: flowLimit.nodes + 1 }, (_, i) => lot(String(i))), edges: [] }).error).toMatch(/complete result remains available in Table/);
  expect(buildTraceFlow({ ...trace, edges: [edge('op', 'missing', 'a', '1', '1')] }).error).toMatch(/endpoints are missing/);
  expect(buildTraceFlow({ ...trace, edges: [edge('cycle', 'a', 'a', '1', '1')] }).error).toMatch(/cyclic/);
});
