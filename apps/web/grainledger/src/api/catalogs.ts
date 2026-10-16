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

export const catalogEndpoints = {
  // Active substances
  getActiveSubstances: (_params?: unknown) => apiFetch<unknown>('/api/active-substances'),
  createActiveSubstance: (req: { code: string; name: string }) =>
    apiFetch<unknown>('/api/active-substances', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateActiveSubstance: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/active-substances/${id}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteActiveSubstance: (id: string) =>
    apiFetch<unknown>(`/api/active-substances/${id}`, {
      method: 'DELETE',
    }),

  // Markets
  getMarkets: (_params?: unknown) => apiFetch<unknown>('/api/markets'),
  createMarket: (req: { code: string; name: string }) =>
    apiFetch<unknown>('/api/markets', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateMarket: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/markets/${id}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteMarket: (id: string) =>
    apiFetch<unknown>(`/api/markets/${id}`, {
      method: 'DELETE',
    }),

  // Commodities
  getCommodities: (_params?: unknown) => apiFetch<unknown>('/api/commodities'),
  createCommodity: (req: { code: string; name: string }) =>
    apiFetch<unknown>('/api/commodities', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateCommodity: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/commodities/${id}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteCommodity: (id: string) =>
    apiFetch<unknown>(`/api/commodities/${id}`, {
      method: 'DELETE',
    }),

  // Quality attributes
  getQualityAttributes: (_params?: unknown) => apiFetch<unknown>('/api/quality-attributes'),
  createQualityAttribute: (req: { code: string; name: string; unit: string }) =>
    apiFetch<unknown>('/api/quality-attributes', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateQualityAttribute: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/quality-attributes/${id}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteQualityAttribute: (id: string) =>
    apiFetch<unknown>(`/api/quality-attributes/${id}`, {
      method: 'DELETE',
    }),

  // Rice varieties
  getRiceVarieties: (_params?: unknown) => apiFetch<unknown>('/api/rice-varieties'),
  createRiceVariety: (req: { code: string; name: string }) =>
    apiFetch<unknown>('/api/rice-varieties', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateRiceVariety: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/rice-varieties/${id}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteRiceVariety: (id: string) =>
    apiFetch<unknown>(`/api/rice-varieties/${id}`, {
      method: 'DELETE',
    }),

  // Measurement units
  getMeasurementUnits: (_params?: unknown) => apiFetch<unknown>('/api/measurement-units'),
  createMeasurementUnit: (req: { code: string; name: string; quantityKind: string }) =>
    apiFetch<unknown>('/api/measurement-units', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateMeasurementUnit: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/measurement-units/${id}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteMeasurementUnit: (id: string) =>
    apiFetch<unknown>(`/api/measurement-units/${id}`, {
      method: 'DELETE',
    }),

  // MRL table versions
  getMrlVersions: () => apiFetch<unknown>('/api/mrl-versions'),
  createMrlVersion: (req: unknown) =>
    apiFetch<unknown>('/api/mrl-versions', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  getMrlLimits: (versionId: string) =>
    apiFetch<unknown>(`/api/mrl-versions/${versionId}/limits`),
  upsertMrlLimit: (versionId: string, substanceId: string, req: { limitValue: number; unit: string }) =>
    apiFetch<unknown>(`/api/mrl-versions/${versionId}/limits/${substanceId}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  publishMrlVersion: (versionId: string) =>
    apiFetch<unknown>(`/api/mrl-versions/${versionId}/publish`, {
      method: 'POST',
    }),
  resolveMrlVersion: () => apiFetch<unknown>('/api/mrl-versions/resolve'),
};

export const catalogsEndpoints = catalogEndpoints;
