import { apiFetch } from './client';

export interface SampleInput {
  entityId: string;
  sourceLotId: string;
  code: string | null;
  sealCode: string | null;
  collectedAt: string;
  materialDescription?: string | null;
}

export interface CaptureSampleRequest {
  deviceId?: string | null;
  clientOperationId: string;
  capturedAt: string;
  payloadSchemaVersion: number;
  payload: SampleInput | null;
}

export interface SampleHandoverInput {
  entityId: string;
  fromParty: string | null;
  toParty: string | null;
  handedOverAt: string;
  sealCondition: string | null;
  evidenceRef: string | null;
  handoverKind?: string | null;
  laboratoryName?: string | null;
  laboratoryReference?: string | null;
  laboratorySampleReference?: string | null;
}

export interface RecordSampleHandoverRequest {
  deviceId?: string | null;
  clientOperationId: string;
  capturedAt: string;
  payloadSchemaVersion: number;
  payload: SampleHandoverInput | null;
}

export interface LabResultInput {
  testLabel: string | null;
  substanceId: string | null;
  originalResultText: string | null;
  resultKind: string | null;
  numericValue?: string | null;
  unit?: string | null;
  lod?: string | null;
  loq?: string | null;
  method?: string | null;
  uncertainty?: string | null;
  reference?: string | null;
}

export interface LabResultSetInput {
  entityId: string;
  sampleId: string | null;
  laboratorySampleReference: string | null;
  statedSampleCode: string | null;
  statedSealCode: string | null;
  sectionReference: string | null;
  results: LabResultInput[] | null;
}

export interface LabReportInput {
  entityId: string;
  laboratoryName: string | null;
  reportNumber: string | null;
  issuedOn: string;
  receivedAt: string;
  reportKind: string | null;
  relatedReportId?: string | null;
  relationshipReason?: string | null;
  fileName: string | null;
  contentType: string | null;
  contentBase64: string | null;
  companyOnlyConfirmed: boolean;
  laboratoryConclusion: string | null;
  statedStandard: string | null;
  resultSets: LabResultSetInput[] | null;
}

export interface CaptureLabReportRequest {
  deviceId?: string | null;
  clientOperationId: string;
  capturedAt: string;
  payloadSchemaVersion: number;
  payload: LabReportInput | null;
}

export interface LabReportMatchInput {
  resultSetId: string;
  sampleId: string;
  identityBasis: string | null;
  evidenceRef?: string | null;
}

export interface ConfirmLabReportInput {
  identityChecked: boolean;
  transcriptionChecked: boolean;
  matches: LabReportMatchInput[] | null;
}

export interface ConfirmLabReportRequest {
  deviceId?: string | null;
  clientOperationId: string;
  capturedAt: string;
  payloadSchemaVersion: number;
  payload: ConfirmLabReportInput | null;
}

export const sampleEndpoints = {
  getSamples: (params?: Record<string, string | number>) => {
    const qs = params ? '?' + new URLSearchParams(params as Record<string, string>).toString() : '';
    return apiFetch<unknown>(`/api/samples${qs}`);
  },
  getSample: (id: string) => apiFetch<unknown>(`/api/samples/${id}`),
  createSample: (req: CaptureSampleRequest) =>
    apiFetch<unknown>('/api/samples', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  captureSample: (req: CaptureSampleRequest) =>
    apiFetch<unknown>('/api/samples', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  getSampleHandovers: (id: string) => apiFetch<unknown>(`/api/samples/${id}/handovers`),
  recordSampleHandover: (id: string, req: RecordSampleHandoverRequest) =>
    apiFetch<unknown>(`/api/samples/${id}/handovers`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),
};

export const labReportEndpoints = {
  getLabReports: (params?: Record<string, string | number>) => {
    const qs = params ? '?' + new URLSearchParams(params as Record<string, string>).toString() : '';
    return apiFetch<unknown>(`/api/lab-reports${qs}`);
  },
  getLabReport: (id: string) => apiFetch<unknown>(`/api/lab-reports/${id}`),
  createLabReport: (req: CaptureLabReportRequest) =>
    apiFetch<unknown>('/api/lab-reports', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  captureLabReport: (req: CaptureLabReportRequest) =>
    apiFetch<unknown>('/api/lab-reports', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  getLabReportFile: (id: string) => apiFetch<unknown>(`/api/lab-reports/${id}/file`),
  confirmLabReport: (id: string, req: ConfirmLabReportRequest) =>
    apiFetch<unknown>(`/api/lab-reports/${id}/confirm`, {
      method: 'POST',
      body: JSON.stringify(req),
    }),
};

export const complianceEndpoints = {
  ...sampleEndpoints,
  ...labReportEndpoints,
};
