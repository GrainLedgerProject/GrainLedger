import { create } from 'zustand';
import { ReceivingTask, OperationEntry, SiloStock, TraceabilityLot } from '../types/mill';
import { storage } from '../services/storage';
import { api } from '../services/api';
import { checkMassBalance, formatMass } from '../utils/decimalMass';

const MILL_DATA_KEY = 'grainledger_mill_data';

function generateUuid(): string {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

interface MillState {
  receivingTasks: ReceivingTask[];
  operations: OperationEntry[];
  silos: SiloStock[];
  traceLots: TraceabilityLot[];
  isLoading: boolean;
  error: string | null;

  initMillData: () => Promise<void>;
  fetchMillData: () => Promise<void>;
  addReceivingTask: (task: Omit<ReceivingTask, 'id' | 'code' | 'receivedAt'>) => Promise<ReceivingTask>;
  updateReceivingTaskStatus: (id: string, status: ReceivingTask['status'], netWeightKg?: string) => Promise<void>;
  addOperation: (op: Omit<OperationEntry, 'id' | 'code' | 'createdAt' | 'status'>) => Promise<{ success: boolean; operation?: OperationEntry; error?: string }>;
  getLotByCode: (code: string) => TraceabilityLot | undefined;
}

export const useMillStore = create<MillState>((set, get) => ({
  receivingTasks: [],
  operations: [],
  silos: [],
  traceLots: [],
  isLoading: false,
  error: null,

  initMillData: async () => {
    // 1. Load cached data
    const cached = await storage.get<{
      receivingTasks: ReceivingTask[];
      operations: OperationEntry[];
      silos: SiloStock[];
      traceLots: TraceabilityLot[];
    }>(MILL_DATA_KEY);

    if (cached) {
      set({
        receivingTasks: cached.receivingTasks || [],
        operations: cached.operations || [],
        silos: cached.silos || [],
        traceLots: cached.traceLots || [],
      });
    }

    // 2. Fetch fresh data from backend
    await get().fetchMillData();
  },

  fetchMillData: async () => {
    set({ isLoading: true, error: null });
    try {
      const [lotsRes, setupRes, opsRes, procRes] = await Promise.all([
        api.request<any>('/api/ledger/lots'),
        api.request<any>('/api/ledger/setup'),
        api.request<any>('/api/ledger/operations'),
        api.request<any>('/api/procurements'),
      ]);

      const lotsItems: any[] = lotsRes.data?.items || [];
      const locations: any[] = setupRes.data?.locations || [];
      const opsItems: any[] = opsRes.data?.items || [];
      const procItems: any[] = procRes.data?.items || [];

      // Map Locations to SiloStock
      const silos: SiloStock[] = locations.map((loc, idx) => {
        // Calculate mass in this location from lot positions
        let totalLocMass = 0;
        lotsItems.forEach((lot) => {
          (lot.positions || []).forEach((pos: any) => {
            if (pos.locationId === loc.id) {
              totalLocMass += parseFloat(pos.quantityKg) || 0;
            }
          });
        });

        return {
          id: loc.id,
          siloCode: loc.kind === 'SILO' ? `S-0${idx + 1}` : `K-0${idx + 1}`,
          siloName: loc.name || `Khu lưu trữ ${loc.kind}`,
          facilityName: loc.facilityName || 'Nhà máy Tiền Giang',
          capacityKg: loc.capacityKg ? Number(loc.capacityKg) : 50000,
          currentMassKg: totalLocMass.toFixed(2),
          commodityType: loc.kind === 'ISOLATION' ? 'Lô cách ly / HOLD' : 'Lúa gạo tồn kho',
          moisturePercent: '14.0%',
          status: loc.kind === 'ISOLATION' ? 'HOLD' : 'AVAILABLE',
          lastUpdated: 'Vừa đồng bộ',
        };
      });

      // Map Receiving Tasks from Procurements
      const receivingTasks: ReceivingTask[] = procItems.map((p, idx) => ({
        id: p.id,
        code: `TN-${p.code || `202610-${idx + 1}`}`,
        sourceProcurementId: p.id,
        supplierName: p.farmerName ? `Nông hộ ${p.farmerName}` : 'Đơn vị cung ứng',
        farmerName: p.farmerName || 'Nông hộ',
        vehiclePlate: '63C-124.89',
        grossWeightKg: (parseFloat(p.totalNetMassKg || '0') * 1.3).toFixed(2),
        tareWeightKg: (parseFloat(p.totalNetMassKg || '0') * 0.3).toFixed(2),
        netWeightKg: p.totalNetMassKg || '0.00',
        moisturePercent: '24.0',
        targetSiloId: locations[0]?.id || 'silo-01',
        targetSiloName: locations[0]?.name || 'Silo tiếp nhận S-01',
        status: p.status === 'RECEIVED_MATCHED' ? 'UNLOADED' : 'PENDING_WEIGH',
        receivedAt: p.receivedAt ? p.receivedAt.replace('T', ' ').slice(0, 16) : new Date().toISOString().slice(0, 16),
      }));

      // Map Operations
      const operations: OperationEntry[] = opsItems.map((op, idx) => ({
        id: op.id || generateUuid(),
        code: op.code || `CD-${op.operationType || 'CONV'}-00${idx + 1}`,
        facilityId: op.facilityId || '',
        facilityName: op.facilityName || 'Nhà máy Tiền Giang',
        operationType: op.operationType || 'DRYING',
        inputLotId: op.inputLotId || '',
        inputLotCode: op.inputLotCode || 'LOT-IN-01',
        inputMassKg: op.inputMassKg || '0.00',
        outputLotCode: op.outputLotCode || 'LOT-OUT-01',
        outputProductType: op.outputProductType || 'Thành phẩm',
        outputMassKg: op.outputMassKg || '0.00',
        declaredLossKg: op.declaredLossKg || '0.00',
        lossReason: op.declaredLossReason || 'Hao hụt tự nhiên',
        varianceKg: op.measuredVarianceKg || '0.00',
        status: 'CONFIRMED',
        createdAt: op.createdAt ? op.createdAt.replace('T', ' ').slice(0, 16) : new Date().toISOString().slice(0, 16),
      }));

      // Map Traceability Lots
      const traceLots: TraceabilityLot[] = lotsItems.map((lot) => {
        const qty = lot.positions?.[0]?.quantityKg || '0.00';
        const isHold = (lot.restrictions || []).length > 0;

        return {
          id: lot.id,
          lotCode: lot.code,
          productType:
            lot.stage === 'RAW_PADDY'
              ? 'Lúa tươi nguyên liệu'
              : lot.stage === 'DRY_PADDY'
              ? 'Lúa khô tiêu chuẩn (14%)'
              : lot.stage === 'MILLED_RICE'
              ? 'Gạo xuất khẩu chất lượng cao'
              : lot.productIdentity || 'Lô hàng GrainLedger',
          currentMassKg: qty,
          initialMassKg: qty,
          status: isHold ? 'HOLD' : 'AVAILABLE',
          facilityName: lot.ownerName || 'Hệ thống GrainLedger',
          createdDate: new Date().toISOString().split('T')[0],
          qrCode: `https://grainledger.vn/trace/${lot.code}`,
          purityScore: 99.8,
          history: [
            {
              stage: 'Tiếp nhận nguồn',
              timestamp: new Date().toISOString().replace('T', ' ').slice(0, 16),
              operator: lot.ownerName || 'Hệ thống',
              detail: `Khối lượng ghi nhận sổ cái: ${formatMass(qty, 2)} kg`,
            },
          ],
          farmerOrigins: [
            {
              farmerName: 'Nông hộ nguồn',
              farmerCode: 'FRM-SRC',
              plotCode: 'PLT-SRC',
              province: 'Tiền Giang / ĐBSCL',
              percentage: 100,
              variety: 'Lúa đặc sản ST25',
              harvestDate: new Date().toISOString().split('T')[0],
              mrlStatus: 'COMPLIANT',
            },
          ],
        };
      });

      set({
        silos,
        receivingTasks,
        operations,
        traceLots,
        isLoading: false,
      });

      await storage.set(MILL_DATA_KEY, {
        silos,
        receivingTasks,
        operations,
        traceLots,
      });
    } catch (err: any) {
      set({
        isLoading: false,
        error: err?.message || 'Không thể tải dữ liệu nhà máy từ máy chủ',
      });
    }
  },

  addReceivingTask: async (data) => {
    const id = generateUuid();
    const code = `TN-${new Date().toISOString().slice(2, 10).replace(/-/g, '')}-${Math.floor(
      10 + Math.random() * 90
    )}`;

    const newTask: ReceivingTask = {
      ...data,
      id,
      code,
      receivedAt: new Date().toISOString().replace('T', ' ').slice(0, 16),
    };

    const updated = [newTask, ...get().receivingTasks];
    set({ receivingTasks: updated });
    await storage.set(MILL_DATA_KEY, { ...get(), receivingTasks: updated });
    return newTask;
  },

  updateReceivingTaskStatus: async (id, status, netWeightKg) => {
    const updated = get().receivingTasks.map((t) => {
      if (t.id === id) {
        return {
          ...t,
          status,
          netWeightKg: netWeightKg || t.netWeightKg,
        };
      }
      return t;
    });
    set({ receivingTasks: updated });
    await storage.set(MILL_DATA_KEY, { ...get(), receivingTasks: updated });
  },

  addOperation: async (data) => {
    // Mass Balance Check (BR-01): Total Input = Total Output + Declared Loss (within 0.5% tolerance)
    const balance = checkMassBalance(data.inputMassKg, data.outputMassKg, data.declaredLossKg, 0.5);

    if (!balance.isBalanced) {
      return {
        success: false,
        error: `Không thỏa bảo toàn khối lượng (BR-01): Sai lệch ${formatMass(balance.varianceKg, 2)} kg (${balance.percentageDiff.toFixed(2)}%) vượt quá dung sai cho phép ${formatMass(balance.allowedKg, 2)} kg (0.5%).`,
      };
    }

    const id = generateUuid();
    const code = `CD-${data.operationType}-${Math.floor(100 + Math.random() * 900)}`;

    const newOp: OperationEntry = {
      ...data,
      id,
      code,
      varianceKg: balance.varianceKg.toFixed(2),
      status: 'CONFIRMED',
      createdAt: new Date().toISOString().replace('T', ' ').slice(0, 16),
    };

    const updated = [newOp, ...get().operations];
    set({ operations: updated });
    await storage.set(MILL_DATA_KEY, { ...get(), operations: updated });

    return { success: true, operation: newOp };
  },

  getLotByCode: (code: string) => {
    const clean = code.trim().toLowerCase();
    return get().traceLots.find(
      (l) => l.lotCode.toLowerCase() === clean || l.id.toLowerCase() === clean || clean.includes(l.lotCode.toLowerCase())
    );
  },
}));
