import { apiFetch } from './client';

export const catalogEndpoints = {
  // Active substances
  getActiveSubstances: () => apiFetch<unknown>('/api/active-substances'),
  createActiveSubstance: (req: { code: string; name: string }) =>
    apiFetch<unknown>('/api/active-substances', {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Markets
  getMarkets: () => apiFetch<unknown>('/api/markets'),
  createMarket: (req: { code: string; name: string }) =>
    apiFetch<unknown>('/api/markets', {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Commodities
  getCommodities: () => apiFetch<unknown>('/api/commodities'),
  createCommodity: (req: { code: string; name: string }) =>
    apiFetch<unknown>('/api/commodities', {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Quality attributes
  getQualityAttributes: () => apiFetch<unknown>('/api/quality-attributes'),
  createQualityAttribute: (req: { code: string; name: string; unit: string }) =>
    apiFetch<unknown>('/api/quality-attributes', {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Rice varieties
  getRiceVarieties: () => apiFetch<unknown>('/api/rice-varieties'),
  createRiceVariety: (req: { code: string; name: string }) =>
    apiFetch<unknown>('/api/rice-varieties', {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Measurement units
  getMeasurementUnits: () => apiFetch<unknown>('/api/measurement-units'),
  createMeasurementUnit: (req: { code: string; name: string; quantityKind: string }) =>
    apiFetch<unknown>('/api/measurement-units', {
      method: 'POST',
      body: JSON.stringify(req),
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
