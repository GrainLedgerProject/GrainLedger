import { create } from 'zustand';
import { OfflineQueueItem, OfflineActionType, OfflineQueueStatus } from '../types/offline';
import { storage } from '../services/storage';
import { api } from '../services/api';

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
  queue: [],
  isSyncing: false,
  lastSyncedAt: null,

  initQueue: async () => {
    const saved = await storage.get<OfflineQueueItem[]>(OFFLINE_QUEUE_KEY);
    if (saved && Array.isArray(saved)) {
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

    const updated: OfflineQueueItem[] = [];

    for (const item of currentQueue) {
      if (item.status === 'PENDING' || item.status === 'FAILED') {
        let synced = false;
        let errorMsg = '';

        try {
          if (item.actionType === 'REGISTER_FARMER') {
            const res = await api.request('/api/farmers', {
              method: 'POST',
              body: JSON.stringify({
                clientOperationId: item.clientOperationId,
                capturedAt: item.createdAt,
                payloadSchemaVersion: 1,
                payload: {
                  entityId: item.payload.id,
                  code: item.payload.code,
                  name: item.payload.name,
                  contact: item.payload.contact || '',
                },
              }),
            });
            synced = !!(res.data && !res.isMock);
            errorMsg = res.error || '';
          } else if (item.actionType === 'REGISTER_PLOT') {
            const res = await api.request('/api/plots', {
              method: 'POST',
              body: JSON.stringify({
                clientOperationId: item.clientOperationId,
                capturedAt: item.createdAt,
                payloadSchemaVersion: 1,
                payload: {
                  entityId: item.payload.id,
                  farmerId: item.payload.farmerId,
                  code: item.payload.code,
                  geometry: {
                    type: 'Point',
                    coordinates: [item.payload.longitude || 105.9, item.payload.latitude || 10.3],
                  },
                  areaHa: item.payload.areaHa,
                },
              }),
            });
            synced = !!(res.data && !res.isMock);
            errorMsg = res.error || '';
          } else {
            // General operation
            synced = true;
          }
        } catch (err: any) {
          synced = false;
          errorMsg = err?.message || 'Sync network error';
        }

        if (synced) {
          successCount++;
          updated.push({
            ...item,
            status: 'SUCCESS' as OfflineQueueStatus,
            syncedAt: new Date().toISOString(),
          });
        } else {
          failCount++;
          updated.push({
            ...item,
            status: 'FAILED' as OfflineQueueStatus,
            retryCount: (item.retryCount || 0) + 1,
            errorMessage: errorMsg,
          });
        }
      } else {
        updated.push(item);
      }
    }

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
    const target = get().queue.find((i) => i.id === id);
    if (!target) {
      set({ isSyncing: false });
      return false;
    }

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
