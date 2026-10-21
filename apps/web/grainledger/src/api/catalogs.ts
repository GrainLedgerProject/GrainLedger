import { apiFetch } from './client';

export interface ActiveSubstanceDto {
  id: string;
  code: string;
  name: string;
  category?: string;
  safetyIntervalDays?: number;
}

export interface MrlLimitDto {
  substanceId: string;
  substanceName?: string;
  substanceCode?: string;
  limitValue: number;
  unit: string;
  effectiveThreshold: number;
}

export interface MrlVersionDto {
  id: string;
  marketId: string;
  marketName?: string;
  commodityId: string;
  commodityName?: string;
  versionNumber: number;
  effectiveFrom: string;
  status: 'DRAFT' | 'PUBLISHED' | 'ARCHIVED';
  limits?: MrlLimitDto[];
  publishedAt?: string;
}

export interface ImportMrlLimitRequest {
  substanceCode: string | null;
  limitValue: string | null;
  unit: string | null;
}

export interface ImportMrlVersionRequest {
  marketId: string;
  commodityId: string;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  sourceRef: string | null;
  limits: ImportMrlLimitRequest[] | null;
}

export interface CreateMrlVersionRequest {
  marketId: string;
  commodityId: string;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  sourceRef: string | null;
}

export interface UpsertMrlLimitRequest {
  limitValue: number | string | null;
  unit: string | null;
}

export const catalogEndpoints = {
  // Active substances
  getActiveSubstances: (_params?: unknown) => apiFetch<unknown>('/api/active-substances'),
  getActiveSubstance: (id: string) => apiFetch<unknown>(`/api/active-substances/${id}`),
  createActiveSubstance: (req: { code: string; name: string }) =>
    apiFetch<unknown>('/api/active-substances', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateActiveSubstance: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/active-substances/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  patchActiveSubstance: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/active-substances/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  deleteActiveSubstance: (id: string) =>
    apiFetch<unknown>(`/api/active-substances/${id}`, {
      method: 'DELETE',
    }),

  // Markets
  getMarkets: (_params?: unknown) => apiFetch<unknown>('/api/markets'),
  getMarket: (id: string) => apiFetch<unknown>(`/api/markets/${id}`),
  createMarket: (req: { code: string; name: string }) =>
    apiFetch<unknown>('/api/markets', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateMarket: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/markets/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  patchMarket: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/markets/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  deleteMarket: (id: string) =>
    apiFetch<unknown>(`/api/markets/${id}`, {
      method: 'DELETE',
    }),

  // Commodities
  getCommodities: (_params?: unknown) => apiFetch<unknown>('/api/commodities'),
  getCommodity: (id: string) => apiFetch<unknown>(`/api/commodities/${id}`),
  createCommodity: (req: { code: string; name: string }) =>
    apiFetch<unknown>('/api/commodities', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateCommodity: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/commodities/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  patchCommodity: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/commodities/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  deleteCommodity: (id: string) =>
    apiFetch<unknown>(`/api/commodities/${id}`, {
      method: 'DELETE',
    }),

  // Quality attributes
  getQualityAttributes: (_params?: unknown) => apiFetch<unknown>('/api/quality-attributes'),
  getQualityAttribute: (id: string) => apiFetch<unknown>(`/api/quality-attributes/${id}`),
  createQualityAttribute: (req: { code: string; name: string; unit: string }) =>
    apiFetch<unknown>('/api/quality-attributes', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateQualityAttribute: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/quality-attributes/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  patchQualityAttribute: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/quality-attributes/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  deleteQualityAttribute: (id: string) =>
    apiFetch<unknown>(`/api/quality-attributes/${id}`, {
      method: 'DELETE',
    }),

  // Rice varieties
  getRiceVarieties: (_params?: unknown) => apiFetch<unknown>('/api/rice-varieties'),
  getRiceVariety: (id: string) => apiFetch<unknown>(`/api/rice-varieties/${id}`),
  createRiceVariety: (req: { code: string; name: string }) =>
    apiFetch<unknown>('/api/rice-varieties', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateRiceVariety: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/rice-varieties/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  patchRiceVariety: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/rice-varieties/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  deleteRiceVariety: (id: string) =>
    apiFetch<unknown>(`/api/rice-varieties/${id}`, {
      method: 'DELETE',
    }),

  // Measurement units
  getMeasurementUnits: (_params?: unknown) => apiFetch<unknown>('/api/measurement-units'),
  getMeasurementUnit: (id: string) => apiFetch<unknown>(`/api/measurement-units/${id}`),
  createMeasurementUnit: (req: { code: string; name: string; quantityKind: string }) =>
    apiFetch<unknown>('/api/measurement-units', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateMeasurementUnit: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/measurement-units/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  patchMeasurementUnit: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/measurement-units/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  deleteMeasurementUnit: (id: string) =>
    apiFetch<unknown>(`/api/measurement-units/${id}`, {
      method: 'DELETE',
    }),

  // MRL table versions
  getMrlVersions: () => apiFetch<unknown>('/api/mrl-versions'),
  getMrlVersion: (id: string) => apiFetch<unknown>(`/api/mrl-versions/${id}`),
  createMrlVersion: (req: CreateMrlVersionRequest | unknown) =>
    apiFetch<unknown>('/api/mrl-versions', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateMrlVersion: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/mrl-versions/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(req),
    }),
  importMrlVersion: (req: ImportMrlVersionRequest | unknown) =>
    apiFetch<unknown>('/api/mrl-versions/import', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  getMrlLimits: (versionId: string) =>
    apiFetch<unknown>(`/api/mrl-versions/${versionId}/limits`),
  upsertMrlLimit: (versionId: string, substanceId: string, req: UpsertMrlLimitRequest | { limitValue: number; unit: string }) =>
    apiFetch<unknown>(`/api/mrl-versions/${versionId}/limits/${substanceId}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteMrlLimit: (versionId: string, substanceId: string) =>
    apiFetch<unknown>(`/api/mrl-versions/${versionId}/limits/${substanceId}`, {
      method: 'DELETE',
    }),
  publishMrlVersion: (versionId: string) =>
    apiFetch<unknown>(`/api/mrl-versions/${versionId}/publish`, {
      method: 'POST',
    }),
  resolveMrlVersion: () => apiFetch<unknown>('/api/mrl-versions/resolve'),
};

export const catalogsEndpoints = catalogEndpoints;
