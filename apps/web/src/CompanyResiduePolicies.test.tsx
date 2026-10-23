import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { clearLocalSession } from './api';
import { CompanyResiduePolicies } from './CompanyResiduePolicies';

const fetchMock = vi.fn<typeof fetch>();
const companyId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const systemRule = { id: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', code: 'RICE-RES', versionNo: 1, companyId: null, kind: 'INTERPRETATION', substanceId: 'substance', commodityId: 'commodity', residueDefinition: 'parent compound', inputStage: 'RAW_PADDY', outputStage: 'RAW_PADDY', inputBasis: 'WHOLE', outputBasis: 'WHOLE', outputFraction: '', process: '', conditions: '', analyticalMethod: 'LC-MS/MS', unit: 'mg/kg', decimalPlaces: 3, interpretationKind: 'NUMERIC_RELATIVE_UNCERTAINTY', ndThresholdKind: null, factorLower: null, factorUpper: null, factorSemantics: null, homogeneousMixing: false, relativeUncertaintyPercent: '10', relativeUncertaintyAtZero: false, effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: null, supersedesId: null, evidenceRef: 'scientific-source', applicabilityStatement: 'Rice only', reviewedBy: 'Dr A', reviewAuthority: 'QA', reviewedAt: '2026-01-01T00:00:00Z', publishedBy: 'admin', publishedAt: '2026-01-01T00:00:00Z' };
let rules: Array<Record<string, unknown>>;
let settings: Array<Record<string, unknown>>;
let posted: Record<string, unknown>[];
const companyRule = { ...systemRule, id: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', companyId, versionNo: 1, relativeUncertaintyPercent: null, relativeUncertaintyAtZero: false };
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

beforeEach(() => {
  clearLocalSession();
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  rules = [systemRule];
  settings = [];
  posted = [];
  fetchMock.mockImplementation(async (url, init) => {
    const path = String(url).split('?')[0];
    if (path === '/api/auth/csrf') return response({ token: 'csrf' });
    if (path === '/api/residue-rules') return response({ items: rules });
    if (path === '/api/residue-policy-settings' && init?.method === 'POST') {
      const payload = JSON.parse(String(init.body)); posted.push(payload); settings = [...settings, payload]; return response({});
    }
    if (path === '/api/residue-policy-settings') return response({ items: settings });
    if (path === '/api/active-substances') return response({ items: [], hasMore: false });
    if (path === '/api/commodities') return response({ items: [], hasMore: false });
    return response({ items: [], hasMore: false });
  });
});

it('publishes explicit system-default adoption through scoped company settings endpoint', async () => {
  const user = userEvent.setup();
  render(<CompanyResiduePolicies active allowed companyId={companyId} />);
  await screen.findByText(/No policy settings published/);
  await screen.findByRole('option', { name: /RICE-RES/ });
  await user.selectOptions(screen.getByLabelText('Residue profile'), 'RICE-RES');
  await user.type(screen.getByLabelText('Evidence reference'), 'company-adoption-review');
  await user.type(screen.getByLabelText('Applicability statement'), 'Use reviewed rice profile');
  await user.type(screen.getByLabelText('Scientific reviewer'), 'Reviewer');
  await user.type(screen.getByLabelText('Review authority'), 'QA');
  fireEvent.change(screen.getByLabelText('Effective from'), { target: { value: '2026-10-23T00:00' } });
  fireEvent.change(screen.getByLabelText('Reviewed at'), { target: { value: '2026-10-22T00:00' } });
  await user.click(screen.getByRole('button', { name: 'Publish reviewed policy setting' }));
  await waitFor(() => expect(posted).toHaveLength(1));
  expect(posted[0]).toMatchObject({ companyId, ruleCode: 'RICE-RES', versionNo: 1, mode: 'SYSTEM_DEFAULT', supersedesId: null, evidenceRef: 'company-adoption-review' });
  expect(fetchMock.mock.calls.some(([url]) => String(url) === '/api/residue-policy-settings' )).toBe(true);
});

it('publishes company override only after same-Code company rule exists', async () => {
  rules = [systemRule, companyRule];
  const user = userEvent.setup();
  render(<CompanyResiduePolicies active allowed companyId={companyId} />);
  await screen.findByText(/No policy settings published/);
  await screen.findByRole('option', { name: /RICE-RES/ });
  await user.selectOptions(screen.getByLabelText('Residue profile'), 'RICE-RES');
  await user.selectOptions(screen.getByLabelText('Setting mode'), 'COMPANY_OVERRIDE');
  await user.type(screen.getByLabelText('Evidence reference'), 'override-review');
  await user.type(screen.getByLabelText('Applicability statement'), 'Use reviewed company override');
  await user.type(screen.getByLabelText('Scientific reviewer'), 'Reviewer');
  await user.type(screen.getByLabelText('Review authority'), 'QA');
  fireEvent.change(screen.getByLabelText('Effective from'), { target: { value: '2026-10-23T00:00' } });
  fireEvent.change(screen.getByLabelText('Reviewed at'), { target: { value: '2026-10-22T00:00' } });
  await user.click(screen.getByRole('button', { name: 'Publish reviewed policy setting' }));
  await waitFor(() => expect(posted).toHaveLength(1));
  expect(posted[0]).toMatchObject({ companyId, ruleCode: 'RICE-RES', mode: 'COMPANY_OVERRIDE', supersedesId: null });
});
