import { create } from 'zustand';
import { OfflineQueueItem, OfflineActionType, OfflineQueueStatus } from '../types/offline';
import { storage } from '../services/storage';

const OFFLINE_QUEUE_KEY = 'grainledger_offline_queue';

interface OfflineState {
  queue: OfflineQueueItem[];
  isSyncing: boolean;
  lastSyncedAt: string | null;

  initQueue: () => Promise<void>;
  enqueue: (item: {
    actionType: OfflineActionType;
    title: string;
    summary: string;
    payload: any;
  }) => Promise<OfflineQueueItem>;
  syncAll: () => Promise<{ successCount: number; failCount: number }>;
  retryItem: (id: string) => Promise<boolean>;
  clearCompleted: () => Promise<void>;
  getPendingCount: () => number;
}

export const useOfflineStore = create<OfflineState>((set, get) => ({
  queue: [
    {
      id: 'q-seed-01',
      actionType: 'CREATE_PROCUREMENT',
      title: 'Phiếu thu mua lúa ST25',
      summary: 'Nông hộ: Nguyễn Văn An - 14.500 kg',
      clientOperationId: 'op-proc-991',
      payload: { farmerId: 'farmer-01', measuredGrossKg: '14500' },
      createdAt: new Date(Date.now() - 3600000 * 2).toISOString(),
      status: 'PENDING',
      retryCount: 0,
    },
    {
      id: 'q-seed-02',
      actionType: 'RECORD_SPRAY',
      title: 'Khai báo phun Tricyclazole',
      summary: 'Thửa ruộng TR-01 - Liều lượng 0.6 l/ha',
      clientOperationId: 'op-spray-882',
      payload: { plotSeasonId: 'plot-01', substanceId: 'sub-tri-01' },
      createdAt: new Date(Date.now() - 3600000 * 4).toISOString(),
      status: 'SUCCESS',
      retryCount: 0,
      syncedAt: new Date(Date.now() - 3600000 * 3).toISOString(),
    },
  ],
  isSyncing: false,
  lastSyncedAt: new Date(Date.now() - 3600000 * 3).toLocaleTimeString('vi-VN', {
    hour: '2-digit',
    minute: '2-digit',
  }),

  initQueue: async () => {
    const saved = await storage.get<OfflineQueueItem[]>(OFFLINE_QUEUE_KEY);
    if (saved && Array.isArray(saved) && saved.length > 0) {
      set({ queue: saved });
    }
  },

  enqueue: async ({ actionType, title, summary, payload }) => {
    const newItem: OfflineQueueItem = {
      id: 'q-' + Date.now() + '-' + Math.random().toString(36).slice(2, 6),
      actionType,
      title,
      summary,
      clientOperationId: 'client-op-' + Date.now(),
      payload,
      createdAt: new Date().toISOString(),
      status: 'PENDING',
      retryCount: 0,
    };

    const updated = [newItem, ...get().queue];
    set({ queue: updated });
    await storage.set(OFFLINE_QUEUE_KEY, updated);
    return newItem;
  },

  syncAll: async () => {
    set({ isSyncing: true });
    const currentQueue = [...get().queue];
    let successCount = 0;
    let failCount = 0;

    // Simulate network sync with realistic delay
    await new Promise((res) => setTimeout(res, 1200));

    const updated = currentQueue.map((item) => {
      if (item.status === 'PENDING' || item.status === 'FAILED') {
        successCount++;
        return {
          ...item,
          status: 'SUCCESS' as OfflineQueueStatus,
          syncedAt: new Date().toISOString(),
        };
      }
      return item;
    });

    const nowStr = new Date().toLocaleTimeString('vi-VN', {
      hour: '2-digit',
      minute: '2-digit',
    });

    set({
      queue: updated,
      isSyncing: false,
      lastSyncedAt: nowStr,
    });

    await storage.set(OFFLINE_QUEUE_KEY, updated);
    return { successCount, failCount };
  },

  retryItem: async (id: string) => {
    set({ isSyncing: true });
    await new Promise((res) => setTimeout(res, 600));

    const updated = get().queue.map((item) => {
      if (item.id === id) {
        return {
          ...item,
          status: 'SUCCESS' as OfflineQueueStatus,
          syncedAt: new Date().toISOString(),
        };
      }
      return item;
    });

    set({ queue: updated, isSyncing: false });
    await storage.set(OFFLINE_QUEUE_KEY, updated);
    return true;
  },

  clearCompleted: async () => {
    const remaining = get().queue.filter((item) => item.status !== 'SUCCESS');
    set({ queue: remaining });
    await storage.set(OFFLINE_QUEUE_KEY, remaining);
  },

  getPendingCount: () => {
    return get().queue.filter((item) => item.status === 'PENDING' || item.status === 'FAILED').length;
  },
}));
