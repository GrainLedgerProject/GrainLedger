import { create } from 'zustand';
import { ReceivingTask, OperationEntry, SiloStock, TraceabilityLot } from '../types/mill';
import { storage } from '../services/storage';
import { checkMassBalance, formatMass } from '../utils/decimalMass';

const MILL_DATA_KEY = 'grainledger_mill_data';

const SEED_RECEIVING_TASKS: ReceivingTask[] = [
  {
    id: 'rec-01',
    code: 'TN-20261022-01',
    sourceProcurementId: 'rcpt-01',
    supplierName: 'Hợp tác xã Nông nghiệp Cái Bè',
    farmerName: 'Nguyễn Văn An',
    vehiclePlate: '63C-124.89',
    grossWeightKg: '14500.00',
    tareWeightKg: '4200.00',
    netWeightKg: '10300.00',
    moisturePercent: '24.2',
    targetSiloId: 'silo-01',
    targetSiloName: 'Silo tiếp nhận S-01 (Lúa tươi)',
    status: 'WEIGHED',
    receivedAt: '2026-10-22 08:30',
  },
  {
    id: 'rec-02',
    code: 'TN-20261022-02',
    sourceProcurementId: 'rcpt-02',
    supplierName: 'Đội xe vận chuyển Mekong 03',
    farmerName: 'Lê Văn Cường',
    vehiclePlate: '66B-089.45',
    grossWeightKg: '22800.00',
    tareWeightKg: '5800.00',
    netWeightKg: '17000.00',
    moisturePercent: '25.0',
    targetSiloId: 'silo-02',
    targetSiloName: 'Silo tiếp nhận S-02 (Lúa tươi)',
    status: 'PENDING_WEIGH',
    receivedAt: '2026-10-22 09:45',
  },
  {
    id: 'rec-03',
    code: 'TN-20261021-09',
    sourceProcurementId: 'rcpt-prev',
    supplierName: 'HTX Nông nghiệp Tân Kiều',
    farmerName: 'Trần Thị Bé',
    vehiclePlate: '66C-112.33',
    grossWeightKg: '18400.00',
    tareWeightKg: '4900.00',
    netWeightKg: '13500.00',
    moisturePercent: '23.8',
    targetSiloId: 'silo-01',
    targetSiloName: 'Silo tiếp nhận S-01 (Lúa tươi)',
    status: 'UNLOADED',
    receivedAt: '2026-10-21 16:20',
  },
];

const SEED_OPERATIONS: OperationEntry[] = [
  {
    id: 'op-01',
    code: 'CD-SAY-001',
    facilityId: 'fac-01',
    facilityName: 'Nhà máy Tiền Giang - Lò sấy vỉ ngang 01',
    operationType: 'DRYING',
    inputLotId: 'lot-raw-01',
    inputLotCode: 'LOT-RAW-ST25-01',
    inputMassKg: '10300.00',
    outputLotCode: 'LOT-DRY-ST25-01',
    outputProductType: 'Lúa khô ST25 (14% độ ẩm)',
    outputMassKg: '9150.00',
    declaredLossKg: '1150.00',
    lossReason: 'Hao hụt bay hơi ẩm tự nhiên từ 24.2% xuống 14.0%',
    varianceKg: '0.00',
    status: 'CONFIRMED',
    createdAt: '2026-10-22 11:00',
  },
  {
    id: 'op-02',
    code: 'CD-XAY-002',
    facilityId: 'fac-01',
    facilityName: 'Dây chuyền xay xát Satake 10T/h',
    operationType: 'MILLING',
    inputLotId: 'lot-dry-01',
    inputLotCode: 'LOT-DRY-ST25-01',
    inputMassKg: '9150.00',
    outputLotCode: 'LOT-BROWN-ST25-01',
    outputProductType: 'Gạo lứt ST25 xuất khẩu',
    outputMassKg: '6850.00',
    declaredLossKg: '2300.00',
    lossReason: 'Tách vỏ trấu và tạp chất thô',
    varianceKg: '0.00',
    status: 'CONFIRMED',
    createdAt: '2026-10-22 14:30',
  },
];

const SEED_SILOS: SiloStock[] = [
  {
    id: 'silo-01',
    siloCode: 'S-01',
    siloName: 'Silo tiếp nhận lúa tươi S-01',
    facilityName: 'Cụm Silo Cái Bè',
    capacityKg: 50000,
    currentMassKg: '23800.00',
    commodityType: 'Lúa tươi ST25',
    moisturePercent: '24.1%',
    status: 'AVAILABLE',
    lastUpdated: '10 phút trước',
  },
  {
    id: 'silo-02',
    siloCode: 'S-02',
    siloName: 'Silo trữ lúa khô S-02 (Sau sấy)',
    facilityName: 'Cụm Silo Cái Bè',
    capacityKg: 100000,
    currentMassKg: '72450.00',
    commodityType: 'Lúa khô ST25 (14%)',
    moisturePercent: '14.0%',
    status: 'AVAILABLE',
    lastUpdated: '25 phút trước',
  },
  {
    id: 'silo-03',
    siloCode: 'K-01',
    siloName: 'Kho thành phẩm TP-01',
    facilityName: 'Kho đóng gói trung tâm',
    capacityKg: 150000,
    currentMassKg: '95200.00',
    commodityType: 'Gạo ST25 đóng bao 5kg (Đạt chuẩn xuất khẩu)',
    moisturePercent: '13.8%',
    status: 'AVAILABLE',
    lastUpdated: '1 giờ trước',
  },
  {
    id: 'silo-04',
    siloCode: 'S-04',
    siloName: 'Silo cách ly kiểm định K-04',
    facilityName: 'Cụm Silo Cái Bè',
    capacityKg: 40000,
    currentMassKg: '18500.00',
    commodityType: 'Lô chờ kết quả test dư lượng MRL',
    moisturePercent: '14.2%',
    status: 'HOLD',
    lastUpdated: 'Hôm qua',
  },
];

const SEED_TRACE_LOTS: TraceabilityLot[] = [
  {
    id: 'lot-st25-export-01',
    lotCode: 'GL-ST25-202610-VN',
    productType: 'Gạo đặc sản ST25 hút chân không 5kg',
    currentMassKg: '15000.00',
    initialMassKg: '15000.00',
    status: 'AVAILABLE',
    facilityName: 'Nhà máy chế biến gạo Tiền Giang',
    createdDate: '2026-10-22',
    qrCode: 'https://grainledger.vn/trace/GL-ST25-202610-VN',
    purityScore: 99.4,
    history: [
      { stage: 'Thu mua tại ruộng', timestamp: '2026-10-20 09:30', operator: 'Võ Minh Thu Mua', detail: 'Thu mua 14.500 kg từ nông hộ Nguyễn Văn An' },
      { stage: 'Tiếp nhận & Cân xe', timestamp: '2026-10-20 11:15', operator: 'Nguyễn Tấn Vận Hành', detail: 'Nhập silo tiếp nhận S-01, net 10.300 kg' },
      { stage: 'Sấy lúa khô', timestamp: '2026-10-21 08:00', operator: 'Nguyễn Tấn Vận Hành', detail: 'Hạ độ ẩm từ 24.2% về 14.0% qua lò sấy vỉ ngang' },
      { stage: 'Xay xát & Đánh bóng', timestamp: '2026-10-21 14:00', operator: 'Trần Quốc Quản Lý', detail: 'Bóc vỏ trấu, đánh bóng hạt qua dàn máy Satake' },
      { stage: 'Kiểm định MRL', timestamp: '2026-10-22 09:00', operator: 'Phòng Lab Quatest 3', detail: 'Đạt chuẩn xuất khẩu EU & US (0 hoạt chất vượt ngưỡng)' },
      { stage: 'Đóng bao & Niêm chì', timestamp: '2026-10-22 15:30', operator: 'Phạm Thanh Xuất Khẩu', detail: 'Đóng 3.000 bao 5kg kèm tem QR truy xuất chống giả' },
    ],
    farmerOrigins: [
      {
        farmerName: 'Nguyễn Văn An',
        farmerCode: 'TG-CB-001',
        plotCode: 'TR-01-ST25',
        province: 'Tiền Giang',
        percentage: 68.5,
        variety: 'ST25 Lúa Tôm',
        harvestDate: '2026-10-20',
        mrlStatus: 'COMPLIANT',
      },
      {
        farmerName: 'Trần Thị Bé',
        farmerCode: 'TG-CB-002',
        plotCode: 'TR-03-JASMINE',
        province: 'Tiền Giang',
        percentage: 31.5,
        variety: 'ST25 (Đồng giống)',
        harvestDate: '2026-10-20',
        mrlStatus: 'COMPLIANT',
      },
    ],
  },
];

interface MillState {
  receivingTasks: ReceivingTask[];
  operations: OperationEntry[];
  silos: SiloStock[];
  traceLots: TraceabilityLot[];

  initMillData: () => Promise<void>;
  addReceivingTask: (task: Omit<ReceivingTask, 'id' | 'code' | 'receivedAt'>) => Promise<ReceivingTask>;
  updateReceivingTaskStatus: (id: string, status: ReceivingTask['status'], netWeightKg?: string) => Promise<void>;
  addOperation: (op: Omit<OperationEntry, 'id' | 'code' | 'createdAt' | 'status'>) => Promise<{ success: boolean; operation?: OperationEntry; error?: string }>;
  getLotByCode: (code: string) => TraceabilityLot | undefined;
}

export const useMillStore = create<MillState>((set, get) => ({
  receivingTasks: SEED_RECEIVING_TASKS,
  operations: SEED_OPERATIONS,
  silos: SEED_SILOS,
  traceLots: SEED_TRACE_LOTS,

  initMillData: async () => {
    const saved = await storage.get<{
      receivingTasks: ReceivingTask[];
      operations: OperationEntry[];
      silos: SiloStock[];
      traceLots: TraceabilityLot[];
    }>(MILL_DATA_KEY);

    if (saved) {
      set({
        receivingTasks: saved.receivingTasks || SEED_RECEIVING_TASKS,
        operations: saved.operations || SEED_OPERATIONS,
        silos: saved.silos || SEED_SILOS,
        traceLots: saved.traceLots || SEED_TRACE_LOTS,
      });
    }
  },

  addReceivingTask: async (data) => {
    const id = 'rec-' + Date.now();
    const code = `TN-${new Date().toISOString().slice(0, 10).replace(/-/g, '')}-${String(
      get().receivingTasks.length + 1
    ).padStart(2, '0')}`;

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

    const id = 'op-' + Date.now();
    const code = `CD-${data.operationType}-${String(get().operations.length + 1).padStart(3, '0')}`;

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
