import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { LabReports, parseReportedResult } from './LabReports';
import { clearLocalSession } from './api';

const fetchMock = vi.fn<typeof fetch>();

it.each(['CONFIRMED', 'PENDING_REVIEW'])('offers result tracing only for confirmed sample associations: %s', async status => {
  const report = { id: 'trace-report', reportNumber: 'R-TRACE', laboratoryName: 'Lab', issuedOn: '2026-10-20', receivedAt: '2026-10-20T10:00:00Z', reportKind: 'FINAL', status,
    fileName: 'report.pdf', contentType: 'application/pdf', fileSize: 1, sha256: 'hash', recordedBy: 'actor', capturedAt: '2026-10-20T10:00:00Z', recordedAt: '2026-10-20T10:00:00Z', confirmedBy: null, confirmedAt: null, suggestedMarketCode: 'VN', assessmentStatus: 'NOT_ASSESSED', assessmentReasons: [],
    resultSets: [{ id: 'panel', sampleId: 'proposed-sample', confirmedSampleId: status === 'CONFIRMED' ? 'confirmed-sample' : null, sourceLotId: 'source', laboratorySampleReference: null, statedSampleCode: null, statedSealCode: null, sectionReference: null,
      results: [{ id: 'selected-row', testLabel: 'Cadmium', originalResultText: '0.15', resultKind: 'NUMERIC', numericValue: '0.15', unit: 'mg/kg' }] }] };
  fetchMock.mockImplementation(async input => new Response(JSON.stringify(String(input) === '/api/lab-reports/trace-report' ? report
    : { items: String(input).startsWith('/api/lab-reports?') ? [report] : [], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } }));
  const onTraceResult = vi.fn();
  render(<LabReports canWrite={false} onTraceResult={onTraceResult} />);
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'R-TRACE ↗' }));
  await screen.findByRole('heading', { name: 'R-TRACE' });
  if (status === 'CONFIRMED') {
    await user.click(screen.getByRole('button', { name: 'Trace forward from result' }));
    expect(onTraceResult).toHaveBeenCalledWith({ reportId: 'trace-report', resultId: 'selected-row' });
  } else expect(screen.queryByRole('button', { name: 'Trace forward from result' })).not.toBeInTheDocument();
  expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
});

beforeEach(() => {
  clearLocalSession();
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  fetchMock.mockImplementation(async () => new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, hasMore: false }), {
    headers: { 'Content-Type': 'application/json' },
  }));
});
it.each([
  ['0.15', { resultKind: 'NUMERIC', numericValue: '0.15' }],
  ['<0.01', { resultKind: 'BELOW_LIMIT', numericValue: '0.01' }],
  ['ND', { resultKind: 'NOT_DETECTED', numericValue: undefined }],
  ['1e-3', null],
  ['<LOD', null],
])('parses only supported reported-result notation: %s', (text, expected) => {
  expect(parseReportedResult(text)).toEqual(expected);
});

it.each(['Test parameter', 'Reported result'] as const)('validates a cleared required %s before uploading report evidence', async field => {
  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: '+ Receive report' }));
  await user.type(screen.getByRole('textbox', { name: /^Laboratory$/ }), 'External laboratory');
  await user.type(screen.getByLabelText('Report number'), 'R-001');
  await user.type(screen.getByLabelText('Issue date'), '2026-10-20');
  await user.upload(screen.getByLabelText('Original report file'), new File(['Original evidence'], 'report.txt', { type: 'text/plain' }));
  await user.click(screen.getByRole('checkbox', { name: /this original file contains this company/ }));
  await user.type(screen.getByLabelText('Test parameter section 1 row 1'), 'Reported test');
  await user.type(screen.getByLabelText('Reported result section 1 row 1'), 'ND');
  await user.clear(screen.getByLabelText(`${field} section 1 row 1`));
  // jsdom misreports uploaded file validity; exercise the application's submit validation.
  fireEvent.submit(screen.getByRole('button', { name: 'Save report evidence' }).closest('form')!);

  expect(await screen.findByRole('alert')).toHaveTextContent(/result rows with test labels and original text/);
  expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
});
it('requires related report choice before file encoding for linked report kind', async () => {
  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: '+ Receive report' }));
  await user.selectOptions(screen.getByLabelText('Report kind'), 'SUPPLEMENTARY');
  fireEvent.submit(screen.getByRole('button', { name: 'Save report evidence' }).closest('form')!);
  expect(await screen.findByRole('alert')).toHaveTextContent('Choose a related report');
  expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
});

it('edits unconfirmed report using prefilled multi-section facts and retains original file', async () => {
  const row = { id: 'result-a', testLabel: 'Cadmium', originalResultText: '0.15', resultKind: 'NUMERIC', numericValue: '0.15', unit: 'mg/kg', lod: '0.01', loq: '0.02', method: 'ICP-MS', uncertainty: '5%', reference: 'Page 2' };
  const report = { id: 'report-a', reportNumber: 'R-001', laboratoryName: 'North Lab', issuedOn: '2026-10-19', receivedAt: '2026-10-20T10:00:00Z', reportKind: 'FINAL', status: 'UNRESOLVED', fileName: 'original.pdf', contentType: 'application/pdf', fileSize: 12, sha256: 'hash', recordedBy: 'operator', capturedAt: '2026-10-20T10:00:00Z', recordedAt: '2026-10-20T10:00:00Z', confirmedBy: null, confirmedAt: null, suggestedMarketCode: 'VN', assessmentStatus: 'NOT_ASSESSED', assessmentReasons: [], resultSets: [{ id: 'set-a', sampleId: null, confirmedSampleId: null, sourceLotId: null, laboratorySampleReference: 'LAB-01', statedSampleCode: 'S-01', statedSealCode: 'SEAL-01', sectionReference: 'Page 2', results: [row] }, { id: 'set-b', sampleId: null, confirmedSampleId: null, sourceLotId: null, laboratorySampleReference: 'LAB-02', statedSampleCode: 'S-02', statedSealCode: 'SEAL-02', sectionReference: 'Page 3', results: [{ ...row, id: 'result-b', testLabel: 'Lead' }] }] };
  let submitted: { payload: { entityId: string; reportNumber: string; contentBase64: string | null; resultSets: Array<{ results: Array<{ testLabel: string; unit?: string; method?: string }> }> } } | undefined;
  const requestBodies: string[] = [];
  fetchMock.mockImplementation(async (input, init) => {
    const path = String(input);
    if (path === '/api/auth/csrf') return new Response(JSON.stringify({ token: 'csrf' }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports?page=1&pageSize=20') return new Response(JSON.stringify({ items: [report], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports/report-a' && init?.method !== 'PUT') return new Response(JSON.stringify(report), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/active-substances?page=1&pageSize=100') return new Response(JSON.stringify({ items: [], page: 1, pageSize: 100, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports/report-a' && init?.method === 'PUT') { requestBodies.push(String(init.body)); submitted = JSON.parse(String(init.body)); return requestBodies.length === 1 ? new Response(JSON.stringify({ title: 'Temporary failure' }), { status: 503, headers: { 'Content-Type': 'application/json' } }) : new Response(JSON.stringify({ ...report, id: 'replacement-a', reportNumber: 'R-002' }), { headers: { 'Content-Type': 'application/json' } }); }
    if (path === '/api/lab-reports/replacement-a') return new Response(JSON.stringify({ ...report, id: 'replacement-a', reportNumber: 'R-002' }), { headers: { 'Content-Type': 'application/json' } });
    return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
  });
  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'R-001 ↗' }));
  await user.click(await screen.findByRole('button', { name: 'Edit draft' }));
  expect(screen.getByLabelText('Test parameter section 1 row 1')).toHaveValue('Cadmium');
  expect(screen.getByLabelText('Test parameter section 2 row 1')).toHaveValue('Lead');
  expect(screen.getByLabelText('method section 1 row 1')).toHaveValue('ICP-MS');
  await user.clear(screen.getByLabelText('Report number'));
  await user.type(screen.getByLabelText('Report number'), 'R-002');
  fireEvent.submit(screen.getByRole('button', { name: 'Save draft changes' }).closest('form')!);
  expect(await screen.findByRole('alert')).toHaveTextContent('Temporary failure');
  expect(screen.getByLabelText('Report number')).toHaveValue('R-002');
  expect(screen.getByRole('button', { name: 'Retry exact submission' })).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Retry exact submission' }));
  await waitFor(() => expect(requestBodies).toHaveLength(2));
  expect(requestBodies[1]).toBe(requestBodies[0]);
  expect(submitted?.payload.reportNumber).toBe('R-002');
  expect(submitted?.payload.entityId).toBe('report-a');
  expect(submitted?.payload.contentBase64).toBeNull();
  expect(submitted?.payload.resultSets).toHaveLength(2);
  expect(submitted?.payload.resultSets[0].results[0]).toMatchObject({ testLabel: 'Cadmium', unit: 'mg/kg', method: 'ICP-MS' });
  expect(await screen.findByRole('heading', { name: 'R-002' })).toBeInTheDocument();
});
it('edits null optional result fields safely and preserves untouched result facts and exact received instant', async () => {
  const report = { id: 'report-null-fields', reportNumber: 'R-NULL', laboratoryName: 'North Lab', issuedOn: '2026-10-19', receivedAt: '2026-10-20T10:00:42.789Z', reportKind: 'FINAL', status: 'UNRESOLVED', fileName: 'original.pdf', contentType: 'application/pdf', fileSize: 12, sha256: 'hash', recordedBy: 'operator', capturedAt: '2026-10-20T10:00:00Z', recordedAt: '2026-10-20T10:00:00Z', confirmedBy: null, confirmedAt: null, suggestedMarketCode: 'VN', assessmentStatus: 'NOT_ASSESSED', assessmentReasons: [], resultSets: [{ id: 'set-null-fields', sampleId: null, confirmedSampleId: null, sourceLotId: null, laboratorySampleReference: null, statedSampleCode: null, statedSealCode: null, sectionReference: null, results: [
    { id: 'nd-row', testLabel: 'Aflatoxin', originalResultText: 'ND', resultKind: 'NOT_DETECTED', numericValue: null, substanceId: null, unit: null, lod: null, loq: null, method: null, uncertainty: null, reference: null },
    { id: 'numeric-row', testLabel: 'Cadmium', originalResultText: '0.05', resultKind: 'NUMERIC', numericValue: '0.05', substanceId: null, unit: 'mg/kg', lod: null, loq: null, method: null, uncertainty: null, reference: null },
  ] }] };
  let submitted: { payload: { reportNumber: string; receivedAt: string; resultSets: Array<{ results: Array<{ testLabel: string; originalResultText: string; resultKind: string; numericValue?: string; unit?: string; lod?: string; loq?: string; method?: string; uncertainty?: string; reference?: string }> }> } } | undefined;
  fetchMock.mockImplementation(async (input, init) => {
    const path = String(input);
    if (path === '/api/lab-reports?page=1&pageSize=20') return new Response(JSON.stringify({ items: [report], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports/report-null-fields' && init?.method === 'PUT') { submitted = JSON.parse(String(init.body)); return new Response(JSON.stringify({ ...report, id: 'replacement-null-fields', reportNumber: 'R-NULL-EDITED' }), { headers: { 'Content-Type': 'application/json' } }); }
    if (path === '/api/lab-reports/report-null-fields') return new Response(JSON.stringify(report), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports/replacement-null-fields') return new Response(JSON.stringify({ ...report, id: 'replacement-null-fields', reportNumber: 'R-NULL-EDITED' }), { headers: { 'Content-Type': 'application/json' } });
    return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
  });
  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'R-NULL ↗' }));
  await user.click(await screen.findByRole('button', { name: 'Edit draft' }));
  expect(screen.getByLabelText('Reported result section 1 row 1')).toHaveValue('ND');
  expect(screen.getByLabelText('Exact numeric string section 1 row 1')).toHaveValue('');
  await user.clear(screen.getByLabelText('Report number'));
  await user.type(screen.getByLabelText('Report number'), 'R-NULL-EDITED');
  fireEvent.submit(screen.getByRole('button', { name: 'Save draft changes' }).closest('form')!);
  await waitFor(() => expect(submitted?.payload.reportNumber).toBe('R-NULL-EDITED'));
  expect(submitted?.payload.receivedAt).toBe('2026-10-20T10:00:42.789Z');
  expect(submitted?.payload.resultSets[0].results).toEqual([
    { testLabel: 'Aflatoxin', originalResultText: 'ND', resultKind: 'NOT_DETECTED' },
    { testLabel: 'Cadmium', originalResultText: '0.05', resultKind: 'NUMERIC', numericValue: '0.05', unit: 'mg/kg' },
  ]);
});


it('does not expose draft edit, delete, or confirmation controls without write permission', async () => {
  const report = { id: 'report-a', reportNumber: 'R-001', laboratoryName: 'North Lab', issuedOn: '2026-10-19', receivedAt: '2026-10-20T10:00:00Z', reportKind: 'FINAL', status: 'UNRESOLVED', fileName: 'original.pdf', fileSize: 12, contentType: 'application/pdf', sha256: 'hash', recordedBy: 'operator', capturedAt: '2026-10-20T10:00:00Z', recordedAt: '2026-10-20T10:00:00Z', confirmedBy: null, confirmedAt: null, suggestedMarketCode: 'VN', assessmentStatus: 'NOT_ASSESSED', assessmentReasons: [], resultSets: [] };
  fetchMock.mockImplementation(async input => {
    const path = String(input);
    if (path === '/api/lab-reports?page=1&pageSize=20') return new Response(JSON.stringify({ items: [report], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports/report-a') return new Response(JSON.stringify(report), { headers: { 'Content-Type': 'application/json' } });
    return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
  });
  render(<LabReports canWrite={false} />);
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'R-001 ↗' }));
  await screen.findByRole('heading', { name: 'R-001' });
  expect(screen.queryByRole('button', { name: 'Edit draft' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Delete draft' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Review and confirm' })).not.toBeInTheDocument();
});
it('keeps confirmed reports immutable in UI even for writers', async () => {
  const report = { id: 'report-a', reportNumber: 'R-001', laboratoryName: 'North Lab', issuedOn: '2026-10-19', receivedAt: '2026-10-20T10:00:00Z', reportKind: 'FINAL', status: 'CONFIRMED', fileName: 'original.pdf', fileSize: 12, contentType: 'application/pdf', sha256: 'hash', recordedBy: 'operator', capturedAt: '2026-10-20T10:00:00Z', recordedAt: '2026-10-20T10:00:00Z', confirmedBy: 'reviewer', confirmedAt: '2026-10-21T10:00:00Z', suggestedMarketCode: 'VN', assessmentStatus: 'NOT_ASSESSED', assessmentReasons: [], resultSets: [] };
  fetchMock.mockImplementation(async input => {
    const path = String(input);
    if (path === '/api/lab-reports?page=1&pageSize=20') return new Response(JSON.stringify({ items: [report], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports/report-a') return new Response(JSON.stringify(report), { headers: { 'Content-Type': 'application/json' } });
    return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
  });
  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'R-001 ↗' }));
  await screen.findByRole('heading', { name: 'R-001' });
  expect(screen.queryByRole('button', { name: 'Edit draft' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Delete draft' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Review and confirm' })).not.toBeInTheDocument();
});


it('warns before deleting draft, keeps cancel safe, and sends exact DELETE envelope', async () => {
  const report = { id: 'report-a', reportNumber: 'R-001', laboratoryName: 'North Lab', issuedOn: '2026-10-19', receivedAt: '2026-10-20T10:00:00Z', reportKind: 'FINAL', status: 'PENDING_REVIEW', fileName: 'original.pdf', fileSize: 12, contentType: 'application/pdf', sha256: 'hash', recordedBy: 'operator', capturedAt: '2026-10-20T10:00:00Z', recordedAt: '2026-10-20T10:00:00Z', confirmedBy: null, confirmedAt: null, suggestedMarketCode: 'VN', assessmentStatus: 'NOT_ASSESSED', assessmentReasons: [], resultSets: [] };
  let deletion: Record<string, unknown> | undefined;
  fetchMock.mockImplementation(async (input, init) => {
    const path = String(input);
    if (path === '/api/auth/csrf') return new Response(JSON.stringify({ token: 'csrf' }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports?page=1&pageSize=20') return new Response(JSON.stringify({ items: [report], page: 1, pageSize: 1, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports/report-a' && init?.method === 'DELETE') { deletion = JSON.parse(String(init.body)); return new Response(null, { status: 204 }); }
    if (path === '/api/lab-reports/report-a') return new Response(JSON.stringify(report), { headers: { 'Content-Type': 'application/json' } });
    return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
  });
  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'R-001 ↗' }));
  await user.click(await screen.findByRole('button', { name: 'Delete draft' }));
  expect(screen.getByText(/Original file, results, and audit history remain retained/)).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'DELETE')).toBe(false);
  await user.click(screen.getByRole('button', { name: 'Delete draft' }));
  await user.click(within(screen.getByRole('dialog', { name: 'Delete draft report?' })).getByRole('button', { name: 'Delete draft' }));
  await waitFor(() => expect(deletion?.clientOperationId).toEqual(expect.any(String)));
  expect(deletion).toMatchObject({ payloadSchemaVersion: 1 });
  expect(deletion).not.toHaveProperty('payload');
  await waitFor(() => expect(screen.getByRole('heading', { name: 'Report register' })).toBeInTheDocument());
});

it('selects readable report label and submits selected report ID', async () => {
  const report = { id: 'report-a', reportNumber: 'R-001', laboratoryName: 'North Lab', issuedOn: '2026-10-19', receivedAt: '2026-10-20T10:00:00Z', reportKind: 'FINAL', status: 'UNRESOLVED' };
  let submitted: { payload: { relatedReportId?: string; relationshipReason?: string } } | undefined;
  fetchMock.mockImplementation(async (input, init) => {
    if (String(input) === '/api/auth/csrf') return new Response(JSON.stringify({ token: 'csrf' }), { headers: { 'Content-Type': 'application/json' } });
    if (String(input) === '/api/lab-reports?page=1&pageSize=20') return new Response(JSON.stringify({ items: [report], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    if (String(input) === '/api/lab-reports' && init?.method === 'POST') { submitted = JSON.parse(String(init.body)); return new Response(JSON.stringify({ id: 'new-report' }), { headers: { 'Content-Type': 'application/json' } }); }
    if (String(input) === '/api/lab-reports/new-report') return new Response(JSON.stringify({ ...report, id: 'new-report', reportNumber: 'R-002', fileName: 'report.txt', fileSize: 16, contentType: 'text/plain', sha256: 'hash', recordedBy: 'operator', capturedAt: '2026-10-20T10:00:00Z', recordedAt: '2026-10-20T10:00:00Z', confirmedBy: null, confirmedAt: null, suggestedMarketCode: 'UNKNOWN', assessmentStatus: 'NOT_ASSESSED', assessmentReasons: [], resultSets: [] }), { headers: { 'Content-Type': 'application/json' } });
    return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
  });
  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: '+ Receive report' }));
  await user.click(screen.getByRole('checkbox', { name: 'Link to an earlier report' }));
  await user.selectOptions(await screen.findByLabelText('Related report'), 'report-a');
  await user.type(screen.getByLabelText('Search loaded reports'), 'not matching');
  expect(screen.getByRole('option', { name: /R-001 · North Lab · 2026-10-19 · FINAL/ })).toBeInTheDocument();
  await user.clear(screen.getByLabelText('Search loaded reports'));
  await user.type(screen.getByLabelText('Relationship reason'), 'Corrected transcription');
  await user.type(screen.getByRole('textbox', { name: /^Laboratory$/ }), 'External laboratory');
  await user.type(screen.getByLabelText('Report number'), 'R-002');
  await user.type(screen.getByLabelText('Issue date'), '2026-10-20');
  await user.upload(screen.getByLabelText('Original report file'), new File(['Original evidence'], 'report.txt', { type: 'text/plain' }));
  await user.click(screen.getByRole('checkbox', { name: /this original file contains this company/ }));
  await user.type(screen.getByLabelText('Test parameter section 1 row 1'), 'Reported test');
  await user.type(screen.getByLabelText('Reported result section 1 row 1'), 'ND');
  fireEvent.submit(screen.getByRole('button', { name: 'Save report evidence' }).closest('form')!);
  await waitFor(() => expect(submitted?.payload.relatedReportId).toBe('report-a'));
  expect(submitted?.payload.relationshipReason).toBe('Corrected transcription');
  expect(await screen.findByRole('heading', { name: 'R-002' })).toBeInTheDocument();
});

it('clears optional relation and reason when link checkbox turns off', async () => {
  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: '+ Receive report' }));
  await user.click(screen.getByRole('checkbox', { name: 'Link to an earlier report' }));
  await user.type(screen.getByLabelText('Relationship reason'), 'Not linked');
  await user.click(screen.getByRole('checkbox', { name: 'Link to an earlier report' }));
  expect(screen.queryByLabelText('Relationship reason')).not.toBeInTheDocument();
  expect(screen.queryByLabelText('Related report')).not.toBeInTheDocument();
});
it('keeps selected sample visible and submits it when search excludes it', async () => {
  const sampleA = { id: 'sample-a', code: 'SMP-001', sealCode: 'SEAL-001', sourceLotId: 'lot-a' };
  let submittedBody: { payload: { resultSets: Array<{ sampleId?: string }> } } | undefined;
  fetchMock.mockImplementation(async (input, init) => {
    const path = String(input);
    if (path === '/api/samples?page=1&pageSize=20') {
      return new Response(JSON.stringify({ items: [sampleA], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    }
    if (path === '/api/auth/csrf') return new Response(JSON.stringify({ token: 'csrf' }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports/report-a') return new Response(JSON.stringify({ id: 'report-a', reportNumber: 'R-002', laboratoryName: 'External laboratory', issuedOn: '2026-10-20', receivedAt: '2026-10-20T10:00:00Z', reportKind: 'FINAL', status: 'UNRESOLVED', fileName: 'report.txt', fileSize: 16, contentType: 'text/plain', sha256: 'hash', recordedBy: 'operator', capturedAt: '2026-10-20T10:00:00Z', recordedAt: '2026-10-20T10:00:00Z', confirmedBy: null, confirmedAt: null, suggestedMarketCode: 'UNKNOWN', assessmentStatus: 'NOT_ASSESSED', assessmentReasons: [], resultSets: [{ id: 'set-a', sampleId: sampleA.id, sourceLotId: null, laboratorySampleReference: null, statedSampleCode: sampleA.code, statedSealCode: sampleA.sealCode, sectionReference: null, results: [] }] }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports' && init?.method === 'POST') {
      submittedBody = JSON.parse(String(init.body));
      return new Response(JSON.stringify({ id: 'report-a' }), { headers: { 'Content-Type': 'application/json' } });
    }
    return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
  });

  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: '+ Receive report' }));
  const selection = await screen.findByRole('combobox', { name: 'Registered sample for section 1 selection' });
  await screen.findByRole('option', { name: 'SMP-001 · seal SEAL-001' });
  await user.selectOptions(selection, sampleA.id);
  await user.type(screen.getByPlaceholderText('Search loaded samples'), 'no matching sample');

  expect(selection).toHaveValue(sampleA.id);
  expect(screen.getByRole('option', { name: 'SMP-001 · seal SEAL-001' })).toBeInTheDocument();

  await user.type(screen.getByRole('textbox', { name: /^Laboratory$/ }), 'External laboratory');
  await user.type(screen.getByLabelText('Report number'), 'R-002');
  await user.type(screen.getByLabelText('Issue date'), '2026-10-20');
  await user.upload(screen.getByLabelText('Original report file'), new File(['Original evidence'], 'report.txt', { type: 'text/plain' }));
  await user.click(screen.getByRole('checkbox', { name: /this original file contains this company/ }));
  await user.type(screen.getByLabelText('Test parameter section 1 row 1'), 'Reported test');
  await user.type(screen.getByLabelText('Reported result section 1 row 1'), 'ND');
  // jsdom misreports uploaded file validity; exercise the application's submit validation.
  fireEvent.submit(screen.getByRole('button', { name: 'Save report evidence' }).closest('form')!);

  await waitFor(() => expect(submittedBody?.payload.resultSets[0].sampleId).toBe(sampleA.id));
  expect(await screen.findByRole('heading', { name: 'R-002' })).toBeInTheDocument();
});
it.each([
  { name: 'confirmed identity over proposal', resultSets: [{ id: 'set-1', sampleId: 'sample-a', confirmedSampleId: 'sample-b' }], expected: 'sample-b' },
  { name: 'different section identities', resultSets: [{ id: 'set-1', sampleId: 'sample-a', confirmedSampleId: 'sample-a' }, { id: 'set-2', sampleId: 'sample-b', confirmedSampleId: 'sample-b' }], expected: '' },
  { name: 'unresolved section identity', resultSets: [{ id: 'set-1', sampleId: 'sample-a', confirmedSampleId: 'sample-a' }, { id: 'set-2', sampleId: null, confirmedSampleId: null }], expected: '' },
])('prefills only unambiguous effective sample identity: $name', async ({ resultSets, expected }) => {
  const parent = { id: 'parent-report', laboratoryName: 'North Lab', reportNumber: 'R-PARENT', issuedOn: '2026-10-19', receivedAt: '2026-10-20T10:00:00Z', reportKind: 'FINAL', status: 'UNRESOLVED', fileName: 'report.pdf', contentType: 'application/pdf', fileSize: 1, sha256: 'hash', recordedBy: 'operator', capturedAt: '2026-10-20T10:00:00Z', recordedAt: '2026-10-20T10:00:00Z', confirmedBy: null, confirmedAt: null, resultSets: resultSets.map(set => ({ ...set, sourceLotId: null, laboratorySampleReference: null, statedSampleCode: null, statedSealCode: null, sectionReference: null, results: [] })), suggestedMarketCode: 'UNKNOWN', assessmentReasons: [] };
  const samples = ['sample-a', 'sample-b'].map((id, index) => ({ id, code: `SMP-00${index + 1}`, sealCode: `SEAL-00${index + 1}`, sourceLotId: 'lot-a' }));
  fetchMock.mockImplementation(async input => {
    const path = String(input);
    if (path === '/api/lab-reports?page=1&pageSize=20') return new Response(JSON.stringify({ items: [parent], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports/parent-report') return new Response(JSON.stringify(parent), { headers: { 'Content-Type': 'application/json' } });
    if (path.startsWith('/api/samples/')) return new Response(JSON.stringify(samples.find(sample => sample.id === decodeURIComponent(path.split('/').pop()!))), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/samples?page=1&pageSize=20') return new Response(JSON.stringify({ items: samples, page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
  });
  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'R-PARENT ↗' }));
  await user.click(await screen.findByRole('button', { name: 'Create linked supplementary' }));
  const selection = await screen.findByRole('combobox', { name: 'Registered sample for section 1 selection' });
  if (expected) await screen.findByRole('option', { name: 'SMP-002 · seal SEAL-002' });
  expect(selection).toHaveValue(expected);
});
it('shows rejected confirmation inside dialog, allows correction and retry, and preserves backend NOT_ASSESSED status', async () => {
  const sample = { id: 'sample-a', code: 'SMP-001', sealCode: 'SEAL-001', sourceLotId: 'lot-a' };
  const report = {
    id: 'report-a', laboratoryName: 'North Lab', reportNumber: 'R-001', issuedOn: '2026-10-19', receivedAt: '2026-10-20T10:00:00Z',
    reportKind: 'FINAL', status: 'UNRESOLVED', fileName: 'report.pdf', contentType: 'application/pdf', fileSize: 1, sha256: 'hash',
    recordedBy: 'operator', capturedAt: '2026-10-20T10:00:00Z', recordedAt: '2026-10-20T10:00:00Z', confirmedBy: null as string | null, confirmedAt: null as string | null,
    assessmentStatus: 'NOT_ASSESSED', assessmentReasons: [], suggestedMarketCode: 'UNKNOWN',
    resultSets: [{ id: 'set-a', sampleId: sample.id, confirmedSampleId: null, sourceLotId: null, laboratorySampleReference: null,
      statedSampleCode: sample.code, statedSealCode: sample.sealCode, sectionReference: null, results: [
        { testLabel: 'Analyte', substanceId: '11111111-1111-4111-8111-111111111111', originalResultText: '0.1 mg/kg', resultKind: 'NUMERIC', numericValue: '0.1', unit: 'mg/kg' },
      ] }],
  };
  let confirmedReport = report;
  let confirmationAttempts = 0;
  let firstConfirmation: { payload: { matches: Array<{ identityBasis: string }> } } | undefined;
  let correctedConfirmation: { payload: { matches: Array<{ identityBasis: string }> } } | undefined;
  fetchMock.mockImplementation(async (input, init) => {
    const path = String(input);
    if (path === '/api/auth/csrf') return new Response(JSON.stringify({ token: 'csrf' }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports?page=1&pageSize=20') return new Response(JSON.stringify({ items: [report], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports/report-a') return new Response(JSON.stringify(confirmedReport), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/samples?page=1&pageSize=20') return new Response(JSON.stringify({ items: [sample], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/samples/sample-a') return new Response(JSON.stringify(sample), { headers: { 'Content-Type': 'application/json' } });
    if (path === '/api/lab-reports/report-a/confirm' && init?.method === 'POST') {
      const body = JSON.parse(String(init.body));
      confirmationAttempts++;
      if (confirmationAttempts === 1) {
        firstConfirmation = body;
        return new Response(JSON.stringify({ detail: 'Identity basis does not match printed sample.' }), { status: 400, headers: { 'Content-Type': 'application/problem+json' } });
      }
      correctedConfirmation = body;
      confirmedReport = { ...report, status: 'CONFIRMED', confirmedBy: 'reviewer', confirmedAt: '2026-10-20T11:00:00Z' };
      return new Response(JSON.stringify({ ...confirmedReport, residueBindings: [
        { resultId: 'result-a', sourceId: 'lot-a', published: true, reasons: [] },
        { resultId: 'result-b', sourceId: 'lot-a', published: false, reasons: ['Missing compatible adopted profile'] },
      ] }), { headers: { 'Content-Type': 'application/json' } });
    }
    return new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
  });

  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'R-001 ↗' }));
  expect(await screen.findByText('NOT_ASSESSED')).toBeVisible();
  await user.click(await screen.findByRole('button', { name: 'Review and confirm' }));
  const dialog = screen.getByRole('dialog', { name: 'Review report transcription' });
  await screen.findByRole('option', { name: 'SMP-001 · seal SEAL-001' });
  await user.click(screen.getByRole('checkbox', { name: /I checked sample\/seal identity/ }));
  await user.click(screen.getByRole('checkbox', { name: /I checked all result text/ }));
  await user.click(screen.getByRole('button', { name: 'Confirm identity and bind compatible results' }));

  expect(await screen.findByRole('alert')).toHaveTextContent('Identity basis does not match printed sample.');
  expect(screen.getByRole('dialog', { name: 'Review report transcription' })).toBe(dialog);
  expect(firstConfirmation?.payload.matches[0].identityBasis).toBe('SAMPLE_CODE');
  await user.selectOptions(screen.getByLabelText('Identity basis'), 'SEAL_CODE');
  await user.click(screen.getByRole('button', { name: 'Confirm identity and bind compatible results' }));

  await waitFor(() => expect(correctedConfirmation?.payload.matches[0].identityBasis).toBe('SEAL_CODE'));
  await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Review report transcription' })).not.toBeInTheDocument());
  expect(screen.getByText('NOT_ASSESSED')).toBeVisible();
  expect(screen.getByLabelText('Confirmation residue bindings')).toHaveTextContent('1 compatible residue rows bound; 1 require exception review');
  expect(screen.getByLabelText('Confirmation residue bindings')).toHaveTextContent('Missing compatible adopted profile');
  expect(fetchMock.mock.calls.filter(([url]) => String(url).includes('/residue-review/confirm'))).toHaveLength(0);
});
it('preserves imported row details over section defaults and gates contradictory interpretation', async () => {
  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: '+ Receive report' }));
  await user.type(screen.getByLabelText('Default unit section 1'), 'mg/kg');
  await user.click(screen.getByRole('button', { name: 'Import CSV / paste TSV' }));
  fireEvent.change(screen.getByLabelText('Paste laboratory result CSV or TSV'), { target: { value: [
    'testLabel\tsubstanceId\toriginalResultText\tresultKind\tnumericValue\tunit\tlod\tloq\tmethod\tuncertainty\treference',
    'Residue\t\t<0.010\tNUMERIC\t0.01\tmg/100g\t0.002\t\tLC-MS\t\tCertificate p. 2',
    'Appearance\t\tTrace\tQUALITATIVE\t\tmg/kg\t\t\tVisual\t\tCertificate p. 3',
  ].join('\n') } });
  await user.click(screen.getByRole('button', { name: 'Preview rows' }));
  await user.click(await screen.findByRole('button', { name: 'Add previewed rows' }));
  expect(screen.getByLabelText('Unit section 1 row 1')).toHaveValue('mg/100g');
  await user.click(screen.getAllByText(/Additional lab details/)[0]);
  expect(screen.getByLabelText('Confirm structured interpretation section 1 row 1')).not.toBeChecked();
  expect(screen.getByLabelText('Confirm structured interpretation section 1 row 2')).toBeChecked();
});
