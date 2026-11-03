export type TraceLotKind = 'SOURCE' | 'CURRENT_POOL' | 'POOL_GENERATION' | 'ALIQUOT' | 'REMAINDER' | 'PROCESSING_OUTPUT' | 'LOT';
const labels: Record<TraceLotKind, string> = { SOURCE: 'Procurement source', CURRENT_POOL: 'Current pool', POOL_GENERATION: 'Historical pool', ALIQUOT: 'Withdrawn portion', REMAINDER: 'Pool remainder', PROCESSING_OUTPUT: 'Processing output', LOT: 'Recorded lot' };
export function traceKindLabel(lot: { kind?: string; isCurrentPool?: boolean }): string {
  return labels[(lot.kind || (lot.isCurrentPool ? 'CURRENT_POOL' : 'LOT')) as TraceLotKind] || 'Recorded lot';
}
