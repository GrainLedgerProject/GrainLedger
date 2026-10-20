import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { LabReports } from './LabReports';
import { clearLocalSession } from './api';

const fetchMock = vi.fn<typeof fetch>();

beforeEach(() => {
  clearLocalSession();
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  fetchMock.mockImplementation(async () => new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, hasMore: false }), {
    headers: { 'Content-Type': 'application/json' },
  }));
});

it.each(['testLabel', 'originalResultText'] as const)('validates a cleared required %s before uploading report evidence', async field => {
  render(<LabReports canWrite />);
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: '+ Receive report' }));
  await user.type(screen.getByRole('textbox', { name: /^Laboratory$/ }), 'External laboratory');
  await user.type(screen.getByLabelText('Report number'), 'R-001');
  await user.type(screen.getByLabelText('Issue date'), '2026-10-20');
  await user.upload(screen.getByLabelText('Original report file'), new File(['Original evidence'], 'report.txt', { type: 'text/plain' }));
  await user.click(screen.getByRole('checkbox', { name: /this original file contains this company/ }));
  await user.type(screen.getByLabelText('testLabel section 1 row 1'), 'Reported test');
  await user.type(screen.getByLabelText('originalResultText section 1 row 1'), 'ND');
  await user.clear(screen.getByLabelText(`${field} section 1 row 1`));
  // jsdom misreports uploaded file validity; exercise the application's submit validation.
  fireEvent.submit(screen.getByRole('button', { name: 'Save report evidence' }).closest('form')!);

  expect(await screen.findByRole('alert')).toHaveTextContent(/test labels and original text/);
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

it('selects readable report label and submits selected report ID', async () => {
  const report = { id: 'report-a', reportNumber: 'R-001', laboratoryName: 'North Lab', issuedOn: '2026-10-19', receivedAt: '2026-10-20T10:00:00Z', reportKind: 'FINAL', status: 'UNRESOLVED' };
  let submitted: { payload: { relatedReportId?: string; relationshipReason?: string } } | undefined;
  fetchMock.mockImplementation(async (input, init) => {
    if (String(input) === '/api/auth/csrf') return new Response(JSON.stringify({ token: 'csrf' }), { headers: { 'Content-Type': 'application/json' } });
    if (String(input) === '/api/lab-reports?page=1&pageSize=20') return new Response(JSON.stringify({ items: [report], page: 1, pageSize: 20, hasMore: false }), { headers: { 'Content-Type': 'application/json' } });
    if (String(input) === '/api/lab-reports' && init?.method === 'POST') { submitted = JSON.parse(String(init.body)); return new Response(JSON.stringify({ id: 'new-report' }), { headers: { 'Content-Type': 'application/json' } }); }
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
  await user.type(screen.getByLabelText('testLabel section 1 row 1'), 'Reported test');
  await user.type(screen.getByLabelText('originalResultText section 1 row 1'), 'ND');
  fireEvent.submit(screen.getByRole('button', { name: 'Save report evidence' }).closest('form')!);
  await waitFor(() => expect(submitted?.payload.relatedReportId).toBe('report-a'));
  expect(submitted?.payload.relationshipReason).toBe('Corrected transcription');
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
    if (path === '/api/lab-reports/report-a') return new Response(null, { status: 204 });
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
  await user.type(screen.getByLabelText('testLabel section 1 row 1'), 'Reported test');
  await user.type(screen.getByLabelText('originalResultText section 1 row 1'), 'ND');
  // jsdom misreports uploaded file validity; exercise the application's submit validation.
  fireEvent.submit(screen.getByRole('button', { name: 'Save report evidence' }).closest('form')!);

  await waitFor(() => expect(submittedBody?.payload.resultSets[0].sampleId).toBe(sampleA.id));
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
      return new Response(null, { status: 204 });
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
  await user.click(screen.getByRole('button', { name: 'Confirm checked record' }));

  expect(await screen.findByRole('alert')).toHaveTextContent('Identity basis does not match printed sample.');
  expect(screen.getByRole('dialog', { name: 'Review report transcription' })).toBe(dialog);
  expect(firstConfirmation?.payload.matches[0].identityBasis).toBe('SAMPLE_CODE');
  await user.selectOptions(screen.getByLabelText('Identity basis'), 'SEAL_CODE');
  await user.click(screen.getByRole('button', { name: 'Confirm checked record' }));

  await waitFor(() => expect(correctedConfirmation?.payload.matches[0].identityBasis).toBe('SEAL_CODE'));
  await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Review report transcription' })).not.toBeInTheDocument());
  expect(screen.getByText('NOT_ASSESSED')).toBeVisible();
});
