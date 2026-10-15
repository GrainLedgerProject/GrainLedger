export type Page<T> = { items: T[]; page: number; pageSize: number; hasMore: boolean };
export type FarmerOption = { id: string; code: string; name: string };
export type PlotOption = { id: string; farmerId: string; code: string };
export type PlotSeasonOption = { id: string; plotId: string; seasonId: string; riceVariety: string };
export type ReceivingOption = { collectionSiteId: string; collectionSiteCode: string; collectionSiteName: string; receivingLocationId: string; receivingLocationCode: string };
export type ProcurementReference = { receivingOptions: ReceivingOption[]; commodity: { id: string; code: string; name: string }; massUnits: { code: string; name: string; kgFactor: string }[]; page: number; pageSize: number; hasMore: boolean; maxLinesPerReceipt: number };
export type ProcurementLine = { id: string; lineNo: number; plotSeasonId: string; plotId: string; seasonId: string; riceVariety: string; measuredValue: string; measuredUnit: string; netMassKg: string; moisturePercent: string; sourceLotId: string; lotId: string; lotCode: string; stage: string; initialStock: { locationId: string; receivedKg: string } };
export type Procurement = { id: string; code: string; farmerId: string; collectionSiteId: string; receivingLocationId: string; commodityId: string; status: string; receivedAt: string; capturedAt: string; recordedAt: string; totalNetMassKg: string; items: ProcurementLine[] };
export type ProcurementSummary = { id: string; code: string; farmerId: string; farmerName: string; receivedAt: string; status: string; itemCount: number; totalNetMassKg: string };
export type SourceLot = { id: string; lotId: string; lotCode: string; procurementId: string; procurementCode: string; farmerId: string; plotId: string; plotSeasonId: string; riceVariety: string; receivedAt: string; initialNetMassKg: string; moisturePercent: string; receivingStatus: string; usableStockKg: string; currentStock: { locationId: string; onHandKg: string; availableKg: string }[]; stockAsOf: string };

export async function loadAllPages<T>(fetchPage: (page: number) => Promise<Page<T>>): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; page <= 100; page++) {
    const response = await fetchPage(page);
    items.push(...response.items);
    if (!response.hasMore) return items;
  }
  throw new Error('Too many records to load. Narrow the selection.');
}
