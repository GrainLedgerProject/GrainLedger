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
