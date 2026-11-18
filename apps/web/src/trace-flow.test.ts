import { expect, it } from 'vitest';
import { buildTraceFlow, flowLimit, traceLotOrder } from './trace-flow';
import type { LotTrace } from './LedgerTraceability';

const lot = (id: string) => ({ id, code: id.toUpperCase(), stage: 'DRIED_PADDY', producedKg: '0.000001', positions: [], restrictions: [], contributions: null });
const edge = (operationId: string, inputLotId: string, outputLotId: string, inputConsumedKg: string, outputProducedKg: string) => ({ operationId, inputLotId, outputLotId, inputConsumedKg, outputProducedKg });
const trace: LotTrace = { schemaVersion: 2, traceId: 'fixture', targetLotId: 'a', direction: 'forward', view: 'current', complete: true, asOf: '2026-10-17T00:00:00Z', lotCount: 4, edgeCount: 4, sourceContributionsVisible: false,
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

it('draws custody ingress and withdrawal/remainder as separate storage events without double counting split input', () => {
  const custody: LotTrace = { ...trace, lots: ['a', 'b', 'c', 'd'].map(lot), operations: [],
    custodyEvents: [{ id: 'ingress', action: 'INGRESS', locationId: 'store', revision: 1, occurredAt: trace.asOf, primaryOutputLotId: 'b' },
      { id: 'withdraw', action: 'WITHDRAW', locationId: 'store', revision: 2, occurredAt: trace.asOf, primaryOutputLotId: 'c' }],
    edges: [
      { ...edge('unused', 'a', 'b', '99999999999999.999999', '99999999999999.999999'), operationId: null, relationType: 'CUSTODY', relationId: 'ingress', inputEventKg: '99999999999999.999999' },
      { ...edge('unused', 'b', 'c', '0.000001', '0.000001'), operationId: null, relationType: 'CUSTODY', relationId: 'withdraw', inputEventKg: '99999999999999.999999' },
      { ...edge('unused', 'b', 'd', '99999999999999.999998', '99999999999999.999998'), operationId: null, relationType: 'CUSTODY', relationId: 'withdraw', inputEventKg: '99999999999999.999999' },
    ] };
  const flow = buildTraceFlow(custody);
  expect(flow.error).toBeUndefined();
  expect(flow.nodes.filter(node => node.kind === 'custody')).toHaveLength(2);
  expect(flow.nodes.some(node => node.kind === 'operation')).toBe(false);
  expect(flow.edges.filter(edge => edge.from === 'lot:b')).toHaveLength(1);
  expect(flow.edges.find(edge => edge.from === 'lot:b')?.quantityKg).toBe('99999999999999.999999');
  expect(flow.edges.filter(edge => edge.from === 'custody:withdraw')).toHaveLength(2);
  const nearLimit = { ...custody, lots: [...custody.lots, ...Array.from({ length: flowLimit.nodes - 5 }, (_, i) => lot(`extra-${i}`))] };
  expect(buildTraceFlow(nearLimit).error).toMatch(/complete result remains available in Table/);
});

const targets = [{ containerId: 'c1', code: 'CTN-1', status: 'CLOSED', linkedLoadedKg: '10.000000', basis: 'ACTUAL_LOADING', held: false }];
const loadingEdges = [{ containerId: 'c1', lotId: 'd', quantityKg: '10.000000', basis: 'ACTUAL_LOADING' as const,
  lines: [ { lineId: 'l1', loadId: 'load1', locationId: 'store1', proposalId: 'p1', loadedAt: trace.asOf, quantityKg: '4.000001' },
    { lineId: 'l2', loadId: 'load2', locationId: 'store2', proposalId: 'p2', loadedAt: trace.asOf, quantityKg: '5.999999' } ] }];

it('adds one actual container node and exact loading edge across partial loads, locations and plan versions', () => {
  const flow = buildTraceFlow({ ...trace, actualContainers: targets, loadingEdges });
  expect(flow.error).toBeUndefined();
  expect(flow.nodes.filter(n => n.kind === 'container')).toEqual([expect.objectContaining({ id: 'container:c1' })]);
  expect(flow.edges.filter(e => e.kind === 'loading')).toEqual([{ id: 'lot:d/container:c1', from: 'lot:d', to: 'container:c1', quantityKg: '10.000000', kind: 'loading' }]);
  expect(flow.nodes.find(n => n.id === 'container:c1')!.x).toBeGreaterThan(flow.nodes.find(n => n.id === 'lot:d')!.x);
  expect(flow.edges.some(e => e.from === 'lot:a' && e.to === 'container:c1')).toBe(false);
  expect(loadingEdges[0].lines.map(l => l.quantityKg)).toEqual(['4.000001', '5.999999']);
});

it('keeps lot backward upstream, supports a container root and an unloaded standalone container', () => {
  const backward = { ...trace, direction: 'backward' as const, targetLotId: 'd', actualContainers: targets, loadingEdges };
  expect(buildTraceFlow(backward).nodes.some(n => n.kind === 'container')).toBe(false);
  const fromContainer = { ...backward, targetLotId: undefined, root: { type: 'container' as const, id: 'c1' } };
  expect(traceLotOrder(fromContainer)).toEqual(['d', 'b', 'c', 'a']);
  expect(buildTraceFlow(fromContainer).nodes.some(n => n.kind === 'container')).toBe(true);
  const empty = buildTraceFlow({ ...fromContainer, lots: [], edges: [], loadingEdges: [] });
  expect(empty.error).toBeUndefined(); expect(empty.nodes).toHaveLength(1); expect(empty.edges).toEqual([]);
});

it('keeps multiple container targets distinct and counts container/loading connections in graph limits', () => {
  const two = { ...trace, actualContainers: [...targets, { ...targets[0], containerId: 'c2' }], loadingEdges: [...loadingEdges, { ...loadingEdges[0], containerId: 'c2' }] };
  expect(buildTraceFlow(two).nodes.filter(n => n.kind === 'container')).toHaveLength(2);
  expect(buildTraceFlow(two).edges.filter(e => e.kind === 'loading')).toHaveLength(2);
  expect(buildTraceFlow({ ...two, lots: [...trace.lots, ...Array.from({ length: flowLimit.nodes - 7 }, (_, i) => lot(`extra${i}`))] }).error).toMatch(/complete result remains available/);
  expect(buildTraceFlow({ ...two, loadingEdges: undefined }).error).toMatch(/loading relationships are unavailable/);
  expect(buildTraceFlow({ ...two, loadingEdges: [{ ...loadingEdges[0], lotId: 'missing' }] }).error).toMatch(/loading endpoints are missing/);
  expect(buildTraceFlow({ ...two, loadingEdges: [loadingEdges[0], loadingEdges[0]] }).error).toMatch(/Duplicate loading/);
});
