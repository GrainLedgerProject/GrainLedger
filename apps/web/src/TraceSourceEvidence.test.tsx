import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { TraceSourceEvidence } from './TraceSourceEvidence';
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
afterEach(() => vi.unstubAllGlobals());

it('uses Capture source IDs, filters multi-sample reports, keeps qualifiers and pages evidence separately', async () => {
  const fetchMock = vi.fn<typeof fetch>(async url => {
    const query = new URL(String(url), 'http://localhost');
    expect(query.searchParams.get('sourceLotId')).toBe('capture-source');
    if (query.pathname === '/api/samples') return response({ items: [], page: 1, hasMore: false });
    return response({ page: Number(query.searchParams.get('page')), hasMore: query.searchParams.get('page') === '1', items: [{
      id: 'report', reportNumber: 'LAB-01', reportKind: 'FINAL', laboratoryName: 'External lab', issuedOn: '2026-10-21', confirmedAt: '2026-10-21T00:00:00Z',
      assessmentStatus: 'EXCEEDS_LIMIT', relatedReports: [{ id: 'amendment', reportKind: 'AMENDMENT', relationshipReason: 'Corrected transcription' }],
      resultSets: [
        { id: 'set-a', confirmedSampleId: 'sample-a', sourceLotId: 'capture-source', statedSampleCode: 'SAMPLE-A', results: [{ testLabel: 'Substance A', originalResultText: '<0.0100', unit: 'mg/kg', lod: '0.0050', loq: '0.0100', method: 'LC-MS/MS' }] },
        { id: 'set-b', confirmedSampleId: 'sample-b', sourceLotId: 'other-source', statedSampleCode: 'SAMPLE-B', results: [{ testLabel: 'Other sample result', originalResultText: '99' }] },
        { id: 'unresolved', confirmedSampleId: null, sourceLotId: 'capture-source', results: [{ testLabel: 'Unresolved result', originalResultText: '55' }] },
      ],
    }] });
  });
  vi.stubGlobal('fetch', fetchMock);
  render(<TraceSourceEvidence sourceRecordId="capture-source" onClose={() => {}} />);
  const table = await screen.findByRole('table', { name: 'Results for source capture-source in LAB-01' });
  expect(table).toHaveTextContent('<0.0100'); expect(table).toHaveTextContent('0.0050'); expect(table).toHaveTextContent('0.0100');
  expect(screen.queryByText('Other sample result')).not.toBeInTheDocument();
  expect(screen.queryByText('Unresolved result')).not.toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'View sample and handover history' })).toHaveAttribute('href', '/capture/samples/sample-a');
  const browser = userEvent.setup();
  await browser.click(screen.getByText('Whole-report VN comparison'));
  expect(screen.getByText(/summary covers the whole report/)).toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: 'Next reports' }));
  await within(screen.getByRole('navigation', { name: 'Source report pages' })).findByText('Page 2');
  expect(fetchMock.mock.calls.filter(([url]) => String(url).startsWith('/api/samples'))).toHaveLength(1);
  expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/api/ledger/trace'))).toBe(false);
});

it('shows denied evidence as an error rather than claiming there are no reports', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => response({ title: 'Permission denied' }, 403)));
  render(<TraceSourceEvidence sourceRecordId="source" onClose={() => {}} />);
  expect((await screen.findAllByRole('alert')).length).toBeGreaterThan(0);
  expect(screen.queryByText(/No confirmed report evidence/)).not.toBeInTheDocument();
});
