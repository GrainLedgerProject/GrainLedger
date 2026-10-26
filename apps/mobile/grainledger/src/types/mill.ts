export type ReceivingStatus = 'PENDING_WEIGH' | 'WEIGHED' | 'UNLOADED' | 'REJECTED';
export type OperationType = 'DRYING' | 'MILLING' | 'BLENDING' | 'BAGGING';
export type LotStatus = 'AVAILABLE' | 'HOLD' | 'IN_PROCESS' | 'DEPLETED' | 'QUARANTINED';

export interface ReceivingTask {
  id: string;
  code: string;
  sourceProcurementId?: string;
  supplierName: string;
  farmerName?: string;
  vehiclePlate: string;
  grossWeightKg: string;
  tareWeightKg: string;
  netWeightKg: string;
  moisturePercent: string;
  targetSiloId?: string;
  targetSiloName: string;
  status: ReceivingStatus;
  receivedAt: string;
}

export interface OperationEntry {
  id: string;
  code: string;
  facilityId: string;
  facilityName: string;
  operationType: OperationType;
  inputLotId: string;
  inputLotCode: string;
  inputMassKg: string;
  outputLotCode: string;
  outputProductType: string;
  outputMassKg: string;
  declaredLossKg: string;
  lossReason?: string;
  varianceKg?: string;
  status: 'CONFIRMED' | 'PENDING_APPROVAL' | 'RECONCILED';
  createdAt: string;
}

export interface SiloStock {
  id: string;
  siloCode: string;
  siloName: string;
  facilityName: string;
  capacityKg: number;
  currentMassKg: string;
  commodityType: string;
  moisturePercent: string;
  status: LotStatus;
  lastUpdated: string;
}

export interface FarmerOriginTrace {
  farmerName: string;
  farmerCode: string;
  plotCode: string;
  province: string;
  percentage: number;
  variety: string;
  harvestDate: string;
  mrlStatus: 'COMPLIANT' | 'WARNING' | 'BREACH';
}

export interface TraceabilityLot {
  id: string;
  lotCode: string;
  productType: string;
  currentMassKg: string;
  initialMassKg: string;
  status: LotStatus;
  facilityName: string;
  createdDate: string;
  qrCode: string;
  purityScore: number;
  history: {
    stage: string;
    timestamp: string;
    operator: string;
    detail: string;
  }[];
  farmerOrigins: FarmerOriginTrace[];
}
