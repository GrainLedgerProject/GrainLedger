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

export interface EvidenceRequest {
  id: string;
  ownerId: string;
  facilityId: string;
  fileName: string;
  contentType: string;
  contentBase64: string;
}

export const ledgerEndpoints = {
  getSetup: () => apiFetch<unknown>('/api/ledger/setup'),
  getLookups: () => apiFetch<unknown>('/api/ledger/lookups'),
  getLots: (params?: Record<string, string | number | undefined>) => {
    if (!params) return apiFetch<unknown>('/api/ledger/lots');
    const filtered = Object.fromEntries(
      Object.entries(params).filter(([_, v]) => v !== undefined && v !== '')
    ) as Record<string, string>;
    const qs = new URLSearchParams(filtered).toString();
    return apiFetch<unknown>(`/api/ledger/lots${qs ? '?' + qs : ''}`);
  },
  getLot: (id: string) => apiFetch<unknown>(`/api/ledger/lots/${id}`),

  // Facilities, Locations, Equipment
  getFacilities: () => apiFetch<unknown>('/api/ledger/facilities'),
  createFacility: (req: unknown) =>
    apiFetch<unknown>('/api/ledger/facilities', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateFacility: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/facilities/${id}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteFacility: (id: string, req?: unknown) =>
    apiFetch<unknown>(`/api/ledger/facilities/${id}`, {
      method: 'DELETE',
      body: req ? JSON.stringify(req) : undefined,
    }),

  createLocation: (req: unknown) =>
    apiFetch<unknown>('/api/ledger/locations', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateLocation: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/locations/${id}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteLocation: (id: string, req?: unknown) =>
    apiFetch<unknown>(`/api/ledger/locations/${id}`, {
      method: 'DELETE',
      body: req ? JSON.stringify(req) : undefined,
    }),
  clearLocation: (id: string, req?: unknown) =>
    apiFetch<unknown>(`/api/ledger/locations/${id}/clearance`, {
      method: 'POST',
      body: req ? JSON.stringify(req) : undefined,
    }),

  createEquipment: (req: unknown) =>
    apiFetch<unknown>('/api/ledger/equipment', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateEquipment: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/equipment/${id}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteEquipment: (id: string, req?: unknown) =>
    apiFetch<unknown>(`/api/ledger/equipment/${id}`, {
      method: 'DELETE',
      body: req ? JSON.stringify(req) : undefined,
    }),

  // User Assignments to Facilities
  createAssignment: (req: unknown) =>
    apiFetch<unknown>('/api/ledger/assignments', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateAssignment: (userId: string, facilityId: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/assignments/${userId}/${facilityId}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteAssignment: (userId: string, facilityId: string, req?: unknown) =>
    apiFetch<unknown>(`/api/ledger/assignments/${userId}/${facilityId}`, {
      method: 'DELETE',
      body: req ? JSON.stringify(req) : undefined,
    }),

  // Operations
  getOperations: () => apiFetch<unknown>('/api/ledger/operations'),
  recordOperation: (req: RecordOperationRequest | unknown) =>
    apiFetch<unknown>('/api/ledger/operations', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  getOperation: (id: string) => apiFetch<unknown>(`/api/ledger/operations/${id}`),
  updateOperation: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/operations/${id}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteOperation: (id: string, req?: unknown) =>
    apiFetch<unknown>(`/api/ledger/operations/${id}`, {
      method: 'DELETE',
      body: req ? JSON.stringify(req) : undefined,
    }),
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
  createHold: (req: HoldRequest | unknown) =>
    apiFetch<unknown>('/api/ledger/holds', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  respondHold: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/holds/${id}/respond`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  respondToHold: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/holds/${id}/respond`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Corrections & Reconciliations (BR-12)
  getCorrections: () => apiFetch<unknown>('/api/ledger/corrections'),
  createCorrection: (req: unknown) =>
    apiFetch<unknown>('/api/ledger/corrections', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
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
  recordReceivingMeasurement: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/receiving/${id}/measurements`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  postReceivingReviews: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/receiving/${id}/reviews`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  reviewReceiving: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/receiving/${id}/reviews`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Policies & Compatibilities
  getPolicies: () => apiFetch<unknown>('/api/ledger/policies'),
  createPolicy: (req: unknown) =>
    apiFetch<unknown>('/api/ledger/policies', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  getCompatibilities: () => apiFetch<unknown>('/api/ledger/compatibilities'),
  createCompatibility: (req: unknown) =>
    apiFetch<unknown>('/api/ledger/compatibilities', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  createMixedBulkPolicy: (req: unknown) =>
    apiFetch<unknown>('/api/ledger/mixed-bulk-policies', {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Unchanged lot transfer
  postTransfer: (req: unknown) =>
    apiFetch<unknown>('/api/ledger/transfers', {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Evidence
  uploadEvidence: (req: EvidenceRequest) =>
    apiFetch<unknown>('/api/ledger/evidence', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  getEvidence: (id: string) => apiFetch<unknown>(`/api/ledger/evidence/${id}`),

  // Receiving Thresholds (BR-07)
  getReceivingThresholds: () => apiFetch<unknown>('/api/ledger/receiving-thresholds'),
  getReceivingThresholdCompanies: () => apiFetch<unknown>('/api/ledger/receiving-thresholds/companies'),
  getReceivingThresholdHistory: () => apiFetch<unknown>('/api/ledger/receiving-thresholds/history'),
  getOwnCompanyReceivingThreshold: () => apiFetch<unknown>('/api/ledger/receiving-thresholds/own-company'),
  updateDefaultReceivingThreshold: (req: unknown) =>
    apiFetch<unknown>('/api/ledger/receiving-thresholds/default', {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  createCompanyReceivingThreshold: (companyId: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/receiving-thresholds/companies/${companyId}`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateCompanyReceivingThreshold: (companyId: string, req: unknown) =>
    apiFetch<unknown>(`/api/ledger/receiving-thresholds/companies/${companyId}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteCompanyReceivingThreshold: (companyId: string, req?: unknown) =>
    apiFetch<unknown>(`/api/ledger/receiving-thresholds/companies/${companyId}`, {
      method: 'DELETE',
      body: req ? JSON.stringify(req) : undefined,
    }),
};
