import { apiFetch } from './client';

export interface FarmerInput {
  code: string;
  name: string;
  contact: string;
}

export interface RegisterFarmerRequest {
  deviceId?: string | null;
  clientOperationId: string;
  capturedAt: string;
  payloadSchemaVersion: number;
  payload: {
    entityId: string;
    code: string;
    name: string;
    contact: string;
  };
}

export interface RegisterPlotRequest {
  deviceId?: string | null;
  clientOperationId: string;
  capturedAt: string;
  payloadSchemaVersion: number;
  payload: {
    entityId: string;
    farmerId: string;
    code: string;
    geometry: unknown;
    areaHa: number;
  };
}

export interface ProcurementInput {
  farmerId: string;
  plotSeasonId: string;
  commodityId: string;
  receivingLocationId: string;
  measuredGrossKg: string;
  moisturePercent: string;
}

export interface RegisterProcurementRequest {
  deviceId?: string | null;
  clientOperationId: string;
  capturedAt: string;
  payloadSchemaVersion: number;
  payload: {
    entityId: string;
    farmerId: string;
    collectionSiteId: string;
    receivingLocationId: string;
    commodityId: string;
    receivedAt: string;
    items: Array<{
      itemId: string;
      lineNo: number;
      plotSeasonId: string;
      measuredValue: string;
      measuredUnit: string;
      moisturePercent: string;
      lotId: string;
      sourceLotId: string;
    }>;
  };
}

export interface RecordSprayDeclarationRequest {
  deviceId?: string | null;
  clientOperationId: string;
  capturedAt: string;
  payloadSchemaVersion: number;
  payload: {
    entityId: string;
    plotSeasonId: string;
    appliedOn: string;
    informationSource?: string | null;
    evidenceRef?: string | null;
    items: Array<{
      productName?: string | null;
      substanceId: string;
      rate: number;
      rateUnit?: string | null;
    }>;
  };
}

export const procurementEndpoints = {
  getReferenceData: () => apiFetch<unknown>('/api/procurement-reference-data'),
  getProcurementReferenceData: () => apiFetch<unknown>('/api/procurement-reference-data'),
  getSourceLot: (id: string) => apiFetch<unknown>(`/api/source-lots/${id}`),

  // Farmers
  getFarmers: (params?: Record<string, string | number>) => {
    const qs = params ? '?' + new URLSearchParams(params as Record<string, string>).toString() : '';
    return apiFetch<unknown>(`/api/farmers${qs}`);
  },
  createFarmer: (req: RegisterFarmerRequest) =>
    apiFetch<unknown>('/api/farmers', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updateFarmer: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/farmers/${id}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deleteFarmer: (id: string, req?: unknown) =>
    apiFetch<unknown>(`/api/farmers/${id}`, {
      method: 'DELETE',
      body: req ? JSON.stringify(req) : undefined,
    }),
  getFarmer: (id: string) => apiFetch<unknown>(`/api/farmers/${id}`),

  // Plots
  getPlots: (params?: Record<string, string | number>) => {
    const qs = params ? '?' + new URLSearchParams(params as Record<string, string>).toString() : '';
    return apiFetch<unknown>(`/api/plots${qs}`);
  },
  createPlot: (req: RegisterPlotRequest) =>
    apiFetch<unknown>('/api/plots', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  updatePlot: (id: string, req: unknown) =>
    apiFetch<unknown>(`/api/plots/${id}`, {
      method: 'PUT',
      body: JSON.stringify(req),
    }),
  deletePlot: (id: string, req?: unknown) =>
    apiFetch<unknown>(`/api/plots/${id}`, {
      method: 'DELETE',
      body: req ? JSON.stringify(req) : undefined,
    }),
  getPlot: (id: string) => apiFetch<unknown>(`/api/plots/${id}`),

  // Crop Seasons
  getCropSeasons: (params?: Record<string, string | number>) => {
    const qs = params ? '?' + new URLSearchParams(params as Record<string, string>).toString() : '';
    return apiFetch<unknown>(`/api/crop-seasons${qs}`);
  },
  createCropSeason: (req: unknown) =>
    apiFetch<unknown>('/api/crop-seasons', {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Plot Seasons
  getPlotSeasons: (params?: Record<string, string | number>) => {
    const qs = params ? '?' + new URLSearchParams(params as Record<string, string>).toString() : '';
    return apiFetch<unknown>(`/api/plot-seasons${qs}`);
  },
  createPlotSeason: (req: unknown) =>
    apiFetch<unknown>('/api/plot-seasons', {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Spray Declarations
  getSprayDeclarations: () => apiFetch<unknown>('/api/spray-declarations'),
  recordSprayDeclaration: (req: RecordSprayDeclarationRequest) =>
    apiFetch<unknown>('/api/spray-declarations', {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  // Procurements
  getProcurements: () => apiFetch<unknown>('/api/procurements'),
  registerProcurement: (req: RegisterProcurementRequest) =>
    apiFetch<unknown>('/api/procurements', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  getProcurement: (id: string) => apiFetch<unknown>(`/api/procurements/${id}`),
};
