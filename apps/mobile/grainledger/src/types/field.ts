export type SyncStatus = 'SYNCED' | 'PENDING' | 'FAILED';

export interface Farmer {
  id: string;
  code: string;
  name: string;
  contact: string;
  identityNumber?: string;
  province?: string;
  district?: string;
  commune?: string;
  totalAreaHa?: number;
  registeredAt: string;
  syncStatus: SyncStatus;
}

export interface Plot {
  id: string;
  farmerId: string;
  farmerName?: string;
  code: string;
  areaHa: number;
  variety: string;
  season: string;
  latitude?: number;
  longitude?: number;
  registeredAt: string;
  syncStatus: SyncStatus;
}

export interface SprayDeclaration {
  id: string;
  plotSeasonId: string;
  plotCode: string;
  farmerName?: string;
  appliedOn: string;
  productName: string;
  substanceId: string;
  rate: number;
  rateUnit: string;
  phiDays: number;
  harvestAllowedDate: string;
  syncStatus: SyncStatus;
}

export interface ProcurementReceipt {
  id: string;
  receiptNumber: string;
  farmerId: string;
  farmerName: string;
  farmerPhone?: string;
  plotSeasonId: string;
  plotCode: string;
  commodityName: string;
  measuredGrossKg: string;
  moisturePercent: string;
  pricePerKg?: string;
  totalAmount?: string;
  vehiclePlate?: string;
  driverName?: string;
  receivingLocationId?: string;
  collectionSite: string;
  capturedAt: string;
  status: 'COMMITTED' | 'PENDING' | 'SYNCED';
  syncStatus: SyncStatus;
}

export interface FieldSample {
  id: string;
  code: string;
  sealCode: string;
  sourceLotId: string;
  plotCode: string;
  collectedAt: string;
  materialDescription?: string;
  syncStatus: SyncStatus;
}
