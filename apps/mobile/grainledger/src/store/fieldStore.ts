import { create } from 'zustand';
import { Farmer, Plot, SprayDeclaration, ProcurementReceipt, FieldSample } from '../types/field';
import { storage } from '../services/storage';
import { api } from '../services/api';
import { useOfflineStore } from './offlineStore';

const FIELD_DATA_KEY = 'grainledger_field_data';

function generateUuid(): string {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

interface FarmerApiResponse {
  items: {
    id: string;
    code: string;
    name: string;
    contact?: string | null;
    createdAt?: string;
  }[];
}

interface PlotApiResponse {
  items: {
    id: string;
    farmerId: string;
    code: string;
    areaHa: number | string;
    createdAt?: string;
  }[];
}

interface ProcurementApiResponse {
  items: {
    id: string;
    code: string;
    farmerId: string;
    farmerName: string;
    receivedAt?: string;
    status: string;
    totalNetMassKg: string;
  }[];
}

interface FieldState {
  farmers: Farmer[];
  plots: Plot[];
  sprays: SprayDeclaration[];
  receipts: ProcurementReceipt[];
  samples: FieldSample[];
  isLoading: boolean;
  error: string | null;

  initFieldData: () => Promise<void>;
  fetchFieldData: () => Promise<void>;
  addFarmer: (farmerData: Omit<Farmer, 'id' | 'code' | 'registeredAt' | 'syncStatus'>) => Promise<Farmer>;
  addPlot: (plotData: Omit<Plot, 'id' | 'code' | 'registeredAt' | 'syncStatus'>) => Promise<Plot>;
  addSpray: (sprayData: Omit<SprayDeclaration, 'id' | 'harvestAllowedDate' | 'syncStatus'>) => Promise<SprayDeclaration>;
  addReceipt: (receiptData: Omit<ProcurementReceipt, 'id' | 'receiptNumber' | 'capturedAt' | 'status' | 'syncStatus'>) => Promise<ProcurementReceipt>;
  addSample: (sampleData: Omit<FieldSample, 'id' | 'collectedAt' | 'syncStatus'>) => Promise<FieldSample>;
}

export const useFieldStore = create<FieldState>((set, get) => ({
  farmers: [],
  plots: [],
  sprays: [],
  receipts: [],
  samples: [],
  isLoading: false,
  error: null,

  initFieldData: async () => {
    // 1. First, load cached data from storage
    const cached = await storage.get<{
      farmers: Farmer[];
      plots: Plot[];
      sprays: SprayDeclaration[];
      receipts: ProcurementReceipt[];
      samples: FieldSample[];
    }>(FIELD_DATA_KEY);

    if (cached) {
      set({
        farmers: cached.farmers || [],
        plots: cached.plots || [],
        sprays: cached.sprays || [],
        receipts: cached.receipts || [],
        samples: cached.samples || [],
      });
    }

    // 2. Fetch fresh data from backend APIs
    await get().fetchFieldData();
  },

  fetchFieldData: async () => {
    set({ isLoading: true, error: null });
    try {
      const [farmersRes, plotsRes, procRes, spraysRes] = await Promise.all([
        api.request<FarmerApiResponse>('/api/farmers'),
        api.request<PlotApiResponse>('/api/plots'),
        api.request<ProcurementApiResponse>('/api/procurements'),
        api.request<any[]>('/api/spray-declarations'),
      ]);

      const farmers: Farmer[] = (farmersRes.data?.items || []).map((item) => ({
        id: item.id,
        code: item.code,
        name: item.name,
        contact: item.contact || '',
        registeredAt: item.createdAt ? item.createdAt.split('T')[0] : new Date().toISOString().split('T')[0],
        syncStatus: 'SYNCED',
      }));

      const plots: Plot[] = (plotsRes.data?.items || []).map((item) => {
        const farmer = farmers.find((f) => f.id === item.farmerId);
        return {
          id: item.id,
          farmerId: item.farmerId,
          farmerName: farmer?.name || 'Nông hộ liên kết',
          code: item.code,
          areaHa: Number(item.areaHa) || 0,
          variety: 'Lúa chất lượng cao',
          season: 'Vụ mùa 2026',
          registeredAt: new Date().toISOString().split('T')[0],
          syncStatus: 'SYNCED',
        };
      });

      const receipts: ProcurementReceipt[] = (procRes.data?.items || []).map((item) => ({
        id: item.id,
        receiptNumber: item.code,
        farmerId: item.farmerId,
        farmerName: item.farmerName || 'N/A',
        farmerPhone: '',
        plotSeasonId: '',
        plotCode: 'Thửa ruộng thu mua',
        commodityName: 'Lúa tươi thu hoạch',
        measuredGrossKg: item.totalNetMassKg || '0',
        moisturePercent: '24.0',
        collectionSite: 'Trạm thu mua Cái Bè',
        capturedAt: item.receivedAt ? item.receivedAt.replace('T', ' ').slice(0, 16) : new Date().toISOString().slice(0, 16),
        status: (item.status as any) || 'COMMITTED',
        syncStatus: 'SYNCED',
      }));

      const sprays: SprayDeclaration[] = Array.isArray(spraysRes.data)
        ? spraysRes.data.map((item: any) => ({
            id: item.id || generateUuid(),
            plotSeasonId: item.plotSeasonId || '',
            plotCode: item.plotCode || 'TR-01',
            farmerName: item.farmerName || '',
            appliedOn: item.appliedOn || new Date().toISOString().split('T')[0],
            productName: item.productName || item.substanceId || 'Thuốc BVTV',
            substanceId: item.substanceId || '',
            rate: item.rate || 0,
            rateUnit: item.rateUnit || 'lít/ha',
            phiDays: item.phiDays || 14,
            harvestAllowedDate: item.harvestAllowedDate || '',
            syncStatus: 'SYNCED',
          }))
        : get().sprays;

      set({
        farmers,
        plots,
        receipts,
        sprays,
        isLoading: false,
      });

      await storage.set(FIELD_DATA_KEY, {
        farmers,
        plots,
        receipts,
        sprays,
        samples: get().samples,
      });
    } catch (err: any) {
      set({
        isLoading: false,
        error: err?.message || 'Không thể tải dữ liệu thực địa từ máy chủ',
      });
    }
  },

  addFarmer: async (data) => {
    const entityId = generateUuid();
    const clientOpId = generateUuid();
    const code = `FRM-${Math.floor(1000 + Math.random() * 9000)}`;

    const newFarmer: Farmer = {
      ...data,
      id: entityId,
      code,
      registeredAt: new Date().toISOString().split('T')[0],
      syncStatus: 'PENDING',
    };

    // Optimistically update state
    const updated = [newFarmer, ...get().farmers];
    set({ farmers: updated });
    await storage.set(FIELD_DATA_KEY, { ...get(), farmers: updated });

    // Attempt real API call
    try {
      const res = await api.request('/api/farmers', {
        method: 'POST',
        body: JSON.stringify({
          clientOperationId: clientOpId,
          capturedAt: new Date().toISOString(),
          payloadSchemaVersion: 1,
          payload: {
            entityId,
            code,
            name: data.name,
            contact: data.contact || '',
          },
        }),
      });

      if (res.data && !res.isMock) {
        newFarmer.syncStatus = 'SYNCED';
        set({ farmers: [newFarmer, ...get().farmers.filter((f) => f.id !== entityId)] });
        await storage.set(FIELD_DATA_KEY, { ...get(), farmers: get().farmers });
        return newFarmer;
      }
    } catch {
      // Fall through to offline queue
    }

    // Queue offline if failed or offline
    await useOfflineStore.getState().enqueue({
      actionType: 'REGISTER_FARMER',
      title: `Đăng ký nông hộ: ${newFarmer.name}`,
      summary: `Mã ${newFarmer.code} - ĐT: ${newFarmer.contact}`,
      payload: newFarmer,
    });

    return newFarmer;
  },

  addPlot: async (data) => {
    const entityId = generateUuid();
    const clientOpId = generateUuid();
    const farmer = get().farmers.find((f) => f.id === data.farmerId);
    const code = `PLT-${Math.floor(1000 + Math.random() * 9000)}-01`;

    const newPlot: Plot = {
      ...data,
      id: entityId,
      code,
      farmerName: farmer?.name || data.farmerName || 'N/A',
      registeredAt: new Date().toISOString().split('T')[0],
      syncStatus: 'PENDING',
    };

    const updated = [newPlot, ...get().plots];
    set({ plots: updated });
    await storage.set(FIELD_DATA_KEY, { ...get(), plots: updated });

    try {
      const res = await api.request('/api/plots', {
        method: 'POST',
        body: JSON.stringify({
          clientOperationId: clientOpId,
          capturedAt: new Date().toISOString(),
          payloadSchemaVersion: 1,
          payload: {
            entityId,
            farmerId: data.farmerId,
            code,
            geometry: {
              type: 'Point',
              coordinates: [data.longitude || 105.9, data.latitude || 10.3],
            },
            areaHa: data.areaHa,
          },
        }),
      });

      if (res.data && !res.isMock) {
        newPlot.syncStatus = 'SYNCED';
        set({ plots: [newPlot, ...get().plots.filter((p) => p.id !== entityId)] });
        await storage.set(FIELD_DATA_KEY, { ...get(), plots: get().plots });
        return newPlot;
      }
    } catch {
      // Fall through
    }

    await useOfflineStore.getState().enqueue({
      actionType: 'REGISTER_PLOT',
      title: `Thêm thửa ruộng ${newPlot.code}`,
      summary: `${newPlot.areaHa} ha - ${newPlot.variety} (${newPlot.farmerName})`,
      payload: newPlot,
    });

    return newPlot;
  },

  addSpray: async (data) => {
    const id = generateUuid();
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
    const id = generateUuid();
    const receiptNumber = `PRC-${new Date().toISOString().slice(2, 10).replace(/-/g, '')}-${Math.floor(
      1000 + Math.random() * 9000
    )}`;

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
    const id = generateUuid();
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
