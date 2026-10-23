import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { clearLocalSession } from './api';
import { ResidueEvidenceControls } from './ResidueEvidenceControls';

const fetchMock = vi.fn<typeof fetch>();
const confirmedReport = {
  id: 'report-1', reportNumber: 'R-001', status: 'CONFIRMED',
  resultSets: [
    { id: 'set-1', sourceLotId: 'source-one', results: [{ id: 'result-one', substanceId: 'substance-one', testLabel: 'Analyte A', originalResultText: '0.12', resultKind: 'NUMERIC' }] },
    { id: 'set-2', sourceLotId: null, results: [{ id: 'unbound-result', substanceId: 'substance-one', testLabel: 'Analyte A', originalResultText: '0.13', resultKind: 'NUMERIC' }] },
    { id: 'set-3', sourceLotId: 'source-two', results: [{ id: 'result-two', testLabel: 'No analyte binding', originalResultText: '0.14', resultKind: 'NUMERIC' }] },
  ],
};
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });

beforeEach(() => {
  clearLocalSession();
  fetchMock.mockReset();
  fetchMock.mockImplementation(async (input, init) => {
    const path = String(input);
    if (path === '/api/auth/csrf') return json({ token: 'csrf' });
    if (path === '/api/residue-report-selections/preview' && init?.method === 'POST') return json({ lower: '0.10', upper: '0.14', unit: 'mg/kg', basis: 'WHOLE', reasons: [], provenance: [], rules: [], decimalPlaces: 3 });
    if ((path === '/api/residue-source-groups' || path === '/api/residue-report-selections') && init?.method !== 'POST') return json({ items: [] });
    if (path === '/api/residue-report-selections' && init?.method === 'POST') return json({});
    return json({ items: [], hasMore: false });
  });
  vi.stubGlobal('fetch', fetchMock);
});

it('binds chosen confirmed report result to its linked source and analyte', async () => {
  const user = userEvent.setup();
  render(<ResidueEvidenceControls active allowed companyId="company-1" report={confirmedReport} />);
  const picker = await screen.findByLabelText('Confirmed report result');
  expect(within(picker).queryByRole('option', { name: /unbound-result/ })).not.toBeInTheDocument();
  expect(within(picker).queryByRole('option', { name: /No analyte binding/ })).not.toBeInTheDocument();
  await user.selectOptions(picker, 'result-one');
  expect(await screen.findByText('Bound to source source-one and substance substance-one.')).toBeInTheDocument();
  await user.selectOptions(picker, '');
  expect(screen.queryByText('Bound to source source-one and substance substance-one.')).not.toBeInTheDocument();
});

it('offers no result choices until report is confirmed', async () => {
  render(<ResidueEvidenceControls active allowed companyId="company-1" report={{ ...confirmedReport, status: 'PENDING' }} />);
  expect(await screen.findByText(/Only confirmed reports can provide a selectable result/)).toBeInTheDocument();
  expect(screen.getByLabelText('Confirmed report result')).toHaveValue('');
  expect(screen.getByRole('button', { name: 'Publish reviewed selection' })).toBeDisabled();
});

it('keeps supporting confirmed results available in fallback maintenance disclosure', async () => {
  const user = userEvent.setup();
  render(<ResidueEvidenceControls active allowed companyId="company-1" report={confirmedReport} />);
  await screen.findByLabelText('Confirmed report result');
  await user.click(screen.getByText('Fallback evidence maintenance', { selector: 'summary' }));
  const supporting = screen.getByLabelText(/Supporting confirmed result IDs/);
  expect(supporting).toHaveAttribute('multiple');
  expect(within(supporting).getByRole('option', { name: /result-one/ })).toBeInTheDocument();
});

it('keeps fallback maintenance independently available from register context', async () => {
  const user = userEvent.setup();
  render(<ResidueEvidenceControls active allowed companyId="company-1" />);
  expect(screen.getByLabelText('Group code')).not.toBeVisible();
  await user.click(screen.getByText('Fallback evidence maintenance', { selector: 'summary' }));
  expect(screen.getByLabelText('Group code')).toBeVisible();
  expect(screen.queryByText(/Published reviewed report-result evidence/)).not.toBeInTheDocument();
});
it('shows non-mutating server preview and clears it when proposed evidence changes', async () => {
  const user = userEvent.setup();
  render(<ResidueEvidenceControls active allowed companyId="company-1" report={confirmedReport} />);
  await user.selectOptions(await screen.findByLabelText('Confirmed report result'), 'result-one');
  fireEvent.change(within(screen.getByRole('group', { name: 'Record reviewed report-result evidence' })).getByLabelText('Effective from'), { target: { value: '2026-10-23T00:00' } });
  await user.click(screen.getByRole('button', { name: 'Preview server assessment' }));
  expect(await screen.findByLabelText('Server residue preview')).toHaveTextContent('0.10–0.14 mg/kg');
  expect(await screen.findByLabelText('Server residue preview')).toHaveTextContent('Lab-reported result/uncertainty text: 0.12');
  expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/api/residue-report-selections/preview')).toHaveLength(1);
  expect(fetchMock.mock.calls.filter(([url, init]) => String(url) === '/api/residue-report-selections' && init?.method === 'POST')).toHaveLength(0);
  await user.type(within(screen.getByRole('group', { name: 'Record reviewed report-result evidence' })).getByLabelText('Material basis'), ' dry');
  expect(screen.queryByLabelText('Server residue preview')).not.toBeInTheDocument();
});
