import { apiFetch } from './client';

export interface RecordOperationRequest {
  facilityId?: string;
  operationType: 'DRYING' | 'MILLING' | 'BLENDING' | 'BAGGING';
  inputs: Array<{ lotId: string; massKg: string }>;
  outputs: Array<{ productType: string; massKg: string; locationId?: string }>;
  declaredLossKg: string;
  declaredLossReason?: string;
  measuredVarianceKg?: string;
  varianceReason?: string;
}

export interface HoldRequest {
  lotId?: string;
  containerId?: string;
  reason: string;
  evidenceReference?: string;
}

export interface CorrectionRequest {
  operationId: string;
  reason: string;
  reconciliationNotes?: string;
}

export const ledgerEndpoints = {
  getSetup: () => apiFetch<unknown>('/api/ledger/setup'),
  getLookups: () => apiFetch<unknown>('/api/ledger/lookups'),
  getLots: () => apiFetch<unknown>('/api/ledger/lots'),
  getLot: (id: string) => apiFetch<unknown>(`/api/ledger/lots/${id}`),

  // Operations
  getOperations: () => apiFetch<unknown>('/api/ledger/operations'),
  recordOperation: (req: RecordOperationRequest | unknown) =>
    apiFetch<unknown>('/api/ledger/operations', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  getOperation: (id: string) => apiFetch<unknown>(`/api/ledger/operations/${id}`),
  confirmOperation: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/operations/${id}/confirm`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  commitOperation: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/operations/${id}/commit`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Quarantine Holds (BR-15)
  getHolds: () => apiFetch<unknown>('/api/ledger/holds'),
  issueHold: (req: HoldRequest | unknown) =>
    apiFetch<unknown>('/api/ledger/holds', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  respondHold: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/holds/${id}/respond`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Corrections & Reconciliations (BR-12)
  getCorrections: () => apiFetch<unknown>('/api/ledger/corrections'),
  reconcileCorrection: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/corrections/${id}/reconcile`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  commitCorrection: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/corrections/${id}/commit`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  cancelCorrection: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/corrections/${id}/cancel`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Notifications
  getNotifications: () => apiFetch<unknown>('/api/ledger/notifications'),
  readNotification: (id: string) =>
    apiFetch<unknown>(`/api/ledger/notifications/${id}/read`, {
      method: 'POST',
    }),

  // Immutable History
  getHistory: () => apiFetch<unknown>('/api/ledger/history'),

  // Receiving tasks & reviews
  getReceivingTasks: () => apiFetch<unknown>('/api/ledger/receiving-tasks'),
  getReceiving: (id: string) => apiFetch<unknown>(`/api/ledger/receiving/${id}`),
  postReceivingMeasurements: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/receiving/${id}/measurements`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  postReceivingReviews: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/receiving/${id}/reviews`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Unchanged lot transfer
  postTransfer: (req: unknown) =>
    apiFetch<unknown>('/api/ledger/transfers', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
};
