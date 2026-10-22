import { create } from 'zustand';
import { Farmer, Plot, SprayDeclaration, ProcurementReceipt, FieldSample } from '../types/field';
import { storage } from '../services/storage';
import { useOfflineStore } from './offlineStore';

const FIELD_DATA_KEY = 'grainledger_field_data';

const SEED_FARMERS: Farmer[] = [
  {
    id: 'farmer-01',
    code: 'TG-CB-001',
    name: 'Nguyễn Văn An',
    contact: '0918 223 344',
    identityNumber: '082088001234',
    province: 'Tiền Giang',
    district: 'Huyện Cái Bè',
    commune: 'Xã Hậu Mỹ Bắc B',
    totalAreaHa: 3.5,
    registeredAt: '2026-09-15',
    syncStatus: 'SYNCED',
  },
  {
    id: 'farmer-02',
    code: 'TG-CB-002',
    name: 'Trần Thị Bé',
    contact: '0909 334 455',
    identityNumber: '082190005678',
    province: 'Tiền Giang',
    district: 'Huyện Cái Bè',
    commune: 'Xã Mỹ Hội',
    totalAreaHa: 2.2,
    registeredAt: '2026-09-20',
    syncStatus: 'SYNCED',
  },
  {
    id: 'farmer-03',
    code: 'DT-TL-003',
    name: 'Lê Văn Cường',
    contact: '0977 445 566',
    identityNumber: '087092003456',
    province: 'Đồng Tháp',
    district: 'Huyện Tháp Mười',
    commune: 'Xã Đốc Binh Kiều',
    totalAreaHa: 5.0,
    registeredAt: '2026-10-02',
    syncStatus: 'SYNCED',
  },
  {
    id: 'farmer-04',
    code: 'AG-TH-004',
    name: 'Phạm Hữu Đức',
    contact: '0988 556 677',
    identityNumber: '089085007890',
    province: 'An Giang',
    district: 'Huyện Thoại Sơn',
    commune: 'Xã Vọng Thê',
    totalAreaHa: 4.8,
    registeredAt: '2026-10-10',
    syncStatus: 'SYNCED',
  },
];

const SEED_PLOTS: Plot[] = [
  {
    id: 'plot-01',
    farmerId: 'farmer-01',
    farmerName: 'Nguyễn Văn An',
    code: 'TR-01-ST25',
    areaHa: 2.0,
    variety: 'ST25 (Lúa tôm đặc sản)',
    season: 'Đông Xuân 2026',
    latitude: 10.3751,
    longitude: 105.9521,
    registeredAt: '2026-09-18',
    syncStatus: 'SYNCED',
  },
  {
    id: 'plot-02',
    farmerId: 'farmer-01',
    farmerName: 'Nguyễn Văn An',
    code: 'TR-02-OM5451',
    areaHa: 1.5,
    variety: 'OM 5451',
    season: 'Đông Xuân 2026',
    latitude: 10.3765,
    longitude: 105.9538,
    registeredAt: '2026-09-18',
    syncStatus: 'SYNCED',
  },
  {
    id: 'plot-03',
    farmerId: 'farmer-02',
    farmerName: 'Trần Thị Bé',
    code: 'TR-03-JASMINE',
    areaHa: 2.2,
    variety: 'Jasmine 85',
    season: 'Đông Xuân 2026',
    latitude: 10.3812,
    longitude: 105.9602,
    registeredAt: '2026-09-22',
    syncStatus: 'SYNCED',
  },
  {
    id: 'plot-04',
    farmerId: 'farmer-03',
    farmerName: 'Lê Văn Cường',
    code: 'TR-04-DT8',
    areaHa: 5.0,
    variety: 'Đài Thơm 8',
    season: 'Đông Xuân 2026',
    latitude: 10.5123,
    longitude: 105.7891,
    registeredAt: '2026-10-05',
    syncStatus: 'SYNCED',
  },
];

const SEED_SPRAYS: SprayDeclaration[] = [
  {
    id: 'spray-01',
    plotSeasonId: 'plot-01',
    plotCode: 'TR-01-ST25',
    farmerName: 'Nguyễn Văn An',
    appliedOn: '2026-10-01',
    productName: 'Amistar Top 325SC',
    substanceId: 'Azoxystrobin + Difenoconazole',
    rate: 0.5,
    rateUnit: 'lít/ha',
    phiDays: 14,
    harvestAllowedDate: '2026-10-15',
    syncStatus: 'SYNCED',
  },
  {
    id: 'spray-02',
    plotSeasonId: 'plot-03',
    plotCode: 'TR-03-JASMINE',
    farmerName: 'Trần Thị Bé',
    appliedOn: '2026-10-05',
    productName: 'Beam 75WP',
    substanceId: 'Tricyclazole',
    rate: 0.3,
    rateUnit: 'kg/ha',
    phiDays: 21,
    harvestAllowedDate: '2026-10-26',
    syncStatus: 'SYNCED',
  },
];

const SEED_RECEIPTS: ProcurementReceipt[] = [
  {
    id: 'rcpt-01',
    receiptNumber: 'TM-20261020-001',
    farmerId: 'farmer-01',
    farmerName: 'Nguyễn Văn An',
    farmerPhone: '0918 223 344',
    plotSeasonId: 'plot-01',
    plotCode: 'TR-01-ST25',
    commodityName: 'Lúa tươi ST25',
    measuredGrossKg: '14500.00',
    moisturePercent: '24.5',
    pricePerKg: '9800',
    totalAmount: '142100000',
    vehiclePlate: '63C-124.89',
    driverName: 'Trần Văn Bình',
    receivingLocationId: 'loc-silo-a1',
    collectionSite: 'Cầu Cái Bè Km12',
    capturedAt: '2026-10-20 09:30',
    status: 'COMMITTED',
    syncStatus: 'SYNCED',
  },
  {
    id: 'rcpt-02',
    receiptNumber: 'TM-20261021-002',
    farmerId: 'farmer-03',
    farmerName: 'Lê Văn Cường',
    farmerPhone: '0977 445 566',
    plotSeasonId: 'plot-04',
    plotCode: 'TR-04-DT8',
    commodityName: 'Lúa tươi Đài Thơm 8',
    measuredGrossKg: '22800.00',
    moisturePercent: '25.2',
    pricePerKg: '8600',
    totalAmount: '196080000',
    vehiclePlate: '66B-089.45',
    driverName: 'Võ Quốc Tuấn',
    receivingLocationId: 'loc-silo-b2',
    collectionSite: 'Bến đò Tháp Mười',
    capturedAt: '2026-10-21 14:15',
    status: 'COMMITTED',
    syncStatus: 'SYNCED',
  },
];

interface FieldState {
  farmers: Farmer[];
  plots: Plot[];
  sprays: SprayDeclaration[];
  receipts: ProcurementReceipt[];
  samples: FieldSample[];

  initFieldData: () => Promise<void>;
  addFarmer: (farmerData: Omit<Farmer, 'id' | 'code' | 'registeredAt' | 'syncStatus'>) => Promise<Farmer>;
  addPlot: (plotData: Omit<Plot, 'id' | 'code' | 'registeredAt' | 'syncStatus'>) => Promise<Plot>;
  addSpray: (sprayData: Omit<SprayDeclaration, 'id' | 'harvestAllowedDate' | 'syncStatus'>) => Promise<SprayDeclaration>;
  addReceipt: (receiptData: Omit<ProcurementReceipt, 'id' | 'receiptNumber' | 'capturedAt' | 'status' | 'syncStatus'>) => Promise<ProcurementReceipt>;
  addSample: (sampleData: Omit<FieldSample, 'id' | 'collectedAt' | 'syncStatus'>) => Promise<FieldSample>;
}

export const useFieldStore = create<FieldState>((set, get) => ({
  farmers: SEED_FARMERS,
  plots: SEED_PLOTS,
  sprays: SEED_SPRAYS,
  receipts: SEED_RECEIPTS,
  samples: [],

  initFieldData: async () => {
    const saved = await storage.get<{
      farmers: Farmer[];
      plots: Plot[];
      sprays: SprayDeclaration[];
      receipts: ProcurementReceipt[];
      samples: FieldSample[];
    }>(FIELD_DATA_KEY);

    if (saved) {
      set({
        farmers: saved.farmers || SEED_FARMERS,
        plots: saved.plots || SEED_PLOTS,
        sprays: saved.sprays || SEED_SPRAYS,
        receipts: saved.receipts || SEED_RECEIPTS,
        samples: saved.samples || [],
      });
    }
  },

  addFarmer: async (data) => {
    const id = 'farmer-' + Date.now();
    const code = `TG-CB-${String(get().farmers.length + 1).padStart(3, '0')}`;
    const newFarmer: Farmer = {
      ...data,
      id,
      code,
      registeredAt: new Date().toISOString().split('T')[0],
      syncStatus: 'PENDING',
    };

    const updated = [newFarmer, ...get().farmers];
    set({ farmers: updated });
    await storage.set(FIELD_DATA_KEY, { ...get(), farmers: updated });

    // Enqueue in offline queue
    await useOfflineStore.getState().enqueue({
      actionType: 'REGISTER_FARMER',
      title: `Đăng ký nông hộ: ${newFarmer.name}`,
      summary: `Mã ${newFarmer.code} - ĐT: ${newFarmer.contact}`,
      payload: newFarmer,
    });

    return newFarmer;
  },

  addPlot: async (data) => {
    const id = 'plot-' + Date.now();
    const farmer = get().farmers.find((f) => f.id === data.farmerId);
    const code = `TR-${String(get().plots.length + 1).padStart(2, '0')}-${data.variety.split(' ')[0]}`;
    const newPlot: Plot = {
      ...data,
      id,
      code,
      farmerName: farmer?.name || data.farmerName || 'N/A',
      registeredAt: new Date().toISOString().split('T')[0],
      syncStatus: 'PENDING',
    };

    const updated = [newPlot, ...get().plots];
    set({ plots: updated });
    await storage.set(FIELD_DATA_KEY, { ...get(), plots: updated });

    await useOfflineStore.getState().enqueue({
      actionType: 'REGISTER_PLOT',
      title: `Thêm thửa ruộng ${newPlot.code}`,
      summary: `${newPlot.areaHa} ha - ${newPlot.variety} (${newPlot.farmerName})`,
      payload: newPlot,
    });

    return newPlot;
  },

  addSpray: async (data) => {
    const id = 'spray-' + Date.now();
    const appDate = new Date(data.appliedOn);
    appDate.setDate(appDate.getDate() + (data.phiDays || 14));
    const harvestAllowedDate = appDate.toISOString().split('T')[0];

    const newSpray: SprayDeclaration = {
      ...data,
      id,
      harvestAllowedDate,
      syncStatus: 'PENDING',
    };

    const updated = [newSpray, ...get().sprays];
    set({ sprays: updated });
    await storage.set(FIELD_DATA_KEY, { ...get(), sprays: updated });

    await useOfflineStore.getState().enqueue({
      actionType: 'RECORD_SPRAY',
      title: `Khai báo thuốc BVTV: ${newSpray.productName}`,
      summary: `Thửa ${newSpray.plotCode} - Cách ly đến ${harvestAllowedDate}`,
      payload: newSpray,
    });

    return newSpray;
  },

  addReceipt: async (data) => {
    const id = 'rcpt-' + Date.now();
    const receiptNumber = `TM-${new Date().toISOString().slice(0, 10).replace(/-/g, '')}-${String(
      get().receipts.length + 1
    ).padStart(3, '0')}`;

    const newReceipt: ProcurementReceipt = {
      ...data,
      id,
      receiptNumber,
      capturedAt: new Date().toISOString().replace('T', ' ').slice(0, 16),
      status: 'COMMITTED',
      syncStatus: 'PENDING',
    };

    const updated = [newReceipt, ...get().receipts];
    set({ receipts: updated });
    await storage.set(FIELD_DATA_KEY, { ...get(), receipts: updated });

    await useOfflineStore.getState().enqueue({
      actionType: 'CREATE_PROCUREMENT',
      title: `Lập phiếu thu mua ${newReceipt.receiptNumber}`,
      summary: `${newReceipt.farmerName} - ${newReceipt.measuredGrossKg} kg ${newReceipt.commodityName}`,
      payload: newReceipt,
    });

    return newReceipt;
  },

  addSample: async (data) => {
    const id = 'sample-' + Date.now();
    const newSample: FieldSample = {
      ...data,
      id,
      collectedAt: new Date().toISOString().replace('T', ' ').slice(0, 16),
      syncStatus: 'PENDING',
    };

    const updated = [newSample, ...get().samples];
    set({ samples: updated });
    await storage.set(FIELD_DATA_KEY, { ...get(), samples: updated });

    await useOfflineStore.getState().enqueue({
      actionType: 'FIELD_SAMPLE',
      title: `Đăng ký mẫu ruộng ${newSample.code}`,
      summary: `Seal: ${newSample.sealCode} - Thửa ${newSample.plotCode}`,
      payload: newSample,
    });

    return newSample;
  },
}));
