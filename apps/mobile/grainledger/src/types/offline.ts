export type OfflineActionType =
  | 'REGISTER_FARMER'
  | 'REGISTER_PLOT'
  | 'RECORD_SPRAY'
  | 'CREATE_PROCUREMENT'
  | 'FIELD_SAMPLE'
  | 'MILL_RECEIVING'
  | 'MILL_OPERATION';

export type OfflineQueueStatus = 'PENDING' | 'SYNCING' | 'SUCCESS' | 'FAILED';

export interface OfflineQueueItem {
  id: string;
  actionType: OfflineActionType;
  title: string;
  summary: string;
  clientOperationId: string;
  payload: any;
  createdAt: string;
  status: OfflineQueueStatus;
  retryCount: number;
  errorMessage?: string;
  syncedAt?: string;
}
