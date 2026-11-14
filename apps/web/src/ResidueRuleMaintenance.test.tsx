import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { clearLocalSession } from './api';
import { ResidueRuleMaintenance, type Rule } from './ResidueRuleMaintenance';

const fetchMock = vi.fn<typeof fetch>();
const substanceId = '11111111-1111-4111-8111-111111111111';
const commodityId = '22222222-2222-4222-8222-222222222222';
const stepId = '44444444-4444-4444-8444-444444444444';
const profile: Rule = {
  id: '33333333-3333-4333-8333-333333333333', code: 'RICE-RES', versionNo: 1, companyId: null, kind: 'FLOW', schemaVersion: 2,
  substanceId, commodityId, residueDefinition: 'parent compound', unit: 'mg/kg', decimalPlaces: 3,
  laboratory: [{ analyticalMethod: 'LC-MS/MS', numericInterpretationKind: 'NUMERIC_ABSOLUTE_UNCERTAINTY', relativeUncertaintyPercent: null, relativeUncertaintyAtZero: false, belowLimit: true, ndThresholdKind: 'LOD' }],
  steps: [{ id: stepId, process: 'DRYING', inputStage: 'RAW_PADDY', outputStage: 'DRIED_PADDY', outputFraction: 'DRIED_PADDY', conditions: 'UNCONDITIONAL', factorLower: '1', factorUpper: '1', factorSemantics: 'STEP_CONCENTRATION', anchorStage: null, homogeneousMixing: false }],
  asReceivedConfirmed: true, effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: null, supersedesId: null, evidenceRef: 'study-1',
  applicabilityStatement: 'Verified rice scope', reviewedBy: 'Dr A', reviewAuthority: 'QA', reviewedAt: '2026-01-01T00:00:00Z', publishedBy: 'publisher', publishedAt: '2026-01-01T00:00:00Z',
};
let rules: Rule[];
let failure: number;
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const posts = () => fetchMock.mock.calls.filter(([url, init]) => String(url) === '/api/residue-rules' && init?.method === 'POST');
const body = (index = 0) => JSON.parse(String(posts()[index][1]?.body));
const change = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });
const submit = () => fireEvent.submit(screen.getByRole('button', { name: 'Publish reviewed profile' }).closest('form')!);

beforeEach(() => {
  clearLocalSession(); vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset(); rules = [profile]; failure = 0;
  fetchMock.mockImplementation(async (url, init) => {
    const path = String(url).split('?')[0];
    if (path === '/api/auth/csrf') return response({ token: 'csrf' });
    if (path === `/api/residue-rules/${profile.id}/retire` && init?.method === 'POST') {
      const retirement = { profileId: profile.id, code: profile.code, companyId: null, reason: JSON.parse(String(init.body)).reason, retiredBy: 'reviewer', retiredAt: '2026-02-01T00:00:00Z' };
      rules = rules.map(rule => ({ ...rule, retirement }));
      return response(retirement);
    }
    if (path === '/api/residue-rules' && init?.method === 'POST') {
      if (failure) { const status = failure; failure = 0; return response({ message: 'Publication failed' }, status); }
      rules = [...rules, { ...JSON.parse(String(init.body)), publishedBy: 'publisher', publishedAt: '2026-01-01T00:00:00Z' }];
      return response({});
    }
    if (path === '/api/residue-rules') return response({ items: rules });
    if (path === '/api/active-substances') return response({ items: [{ id: substanceId, code: 'SUB', name: 'Substance' }], hasMore: false });
    if (path === '/api/commodities') return response({ items: [{ id: commodityId, code: 'RICE', name: 'Rice' }], hasMore: false });
    return response({ items: [], hasMore: false });
  });
});

async function openNew(user: UserEvent) {
  await waitFor(() => expect(screen.getByRole('button', { name: 'Create profile' })).toBeEnabled());
  await user.click(screen.getByRole('button', { name: 'Create profile' }));
  await user.selectOptions(screen.getByLabelText('Substance'), substanceId);
  await user.selectOptions(screen.getByLabelText('Commodity'), commodityId);
  change('Residue definition', 'parent compound');
  await user.click(screen.getByRole('button', { name: 'Add laboratory method' }));
  change('Exact analytical method', 'LC-MS/MS');
  await user.selectOptions(screen.getByLabelText('Numeric results'), 'NUMERIC_ABSOLUTE_UNCERTAINTY');
  await user.click(screen.getByLabelText('Support below-limit results'));
  await user.selectOptions(screen.getByLabelText('ND threshold'), 'LOD');
  await user.click(screen.getByRole('button', { name: 'Add processing step' }));
  await user.selectOptions(within(screen.getByRole('dialog')).getByLabelText('Operation'), 'DRYING');
  await user.click(screen.getByText('Advanced step settings'));
  await user.selectOptions(screen.getByLabelText('Conditions'), 'UNCONDITIONAL');
  await user.click(screen.getByText('Advanced profile settings'));
  change('Reviewed outward precision (decimal places)', '3');
  change('Evidence reference', 'study');
  change('Applicability statement', 'Reviewed rice scope');
  await reviewFields();
}

async function reviewFields() {
  change('Scientific reviewer', 'Dr Current');
  change('Review authority', 'Current QA');
  change('Effective from', '2027-01-01T00:00');
  change('Reviewed at', '2026-10-01T00:00');
}

async function confirm(user: UserEvent) {
  const convention = screen.getByLabelText('I confirm concentrations are mg/kg as received at each physical stage.');
  const review = screen.getByLabelText('I reviewed the complete scientific profile, evidence and effective period.');
  if (!(convention as HTMLInputElement).checked) await user.click(convention);
  if (!(review as HTMLInputElement).checked) await user.click(review);
}

it('requires as-received attestation even when native form validation is bypassed', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await openNew(user);
  change('Concentration factor', '1');
  await user.click(screen.getByLabelText('I reviewed the complete scientific profile, evidence and effective period.'));
  submit();
  expect(await screen.findByRole('alert')).toHaveTextContent('as-received');
  expect(posts()).toHaveLength(0);
  await confirm(user); submit();
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(body()).toMatchObject({ kind: 'FLOW', schemaVersion: 2, asReceivedConfirmed: true, unit: 'mg/kg', laboratory: [{ analyticalMethod: 'LC-MS/MS', numericInterpretationKind: 'NUMERIC_ABSOLUTE_UNCERTAINTY', belowLimit: true, ndThresholdKind: 'LOD' }], steps: [{ process: 'DRYING', factorLower: '1', factorUpper: '1', conditions: 'UNCONDITIONAL' }] });
  expect(body()).not.toHaveProperty('inputBasis');
  expect(body().steps[0]).not.toHaveProperty('outputBasis');
});

it('preserves invalid factor draft without publishing, then publishes corrected value', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await openNew(user);
  change('Concentration factor', 'not-a-factor'); await confirm(user); submit();
  expect(await screen.findByRole('alert')).toHaveTextContent('factor');
  expect(posts()).toHaveLength(0);
  expect(screen.getByLabelText('Concentration factor')).toHaveValue('not-a-factor');
  expect(screen.getByLabelText('Exact analytical method')).toHaveValue('LC-MS/MS');
  change('Concentration factor', '0.25'); await confirm(user); submit();
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(body().steps[0]).toMatchObject({ factorLower: '0.25', factorUpper: '0.25' });
});

it('retains component identity across revision and preserves draft after stale publication', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Create profile' })).toBeEnabled());
  await user.selectOptions(screen.getByLabelText('Reviewed profile'), profile.id);
  await user.click(screen.getByRole('button', { name: 'Use reviewed profile' }));
  expect(screen.getByLabelText('I confirm concentrations are mg/kg as received at each physical stage.')).not.toBeChecked();
  change('Concentration factor', '0.5'); await reviewFields(); await confirm(user);
  failure = 409; submit();
  expect(await screen.findByRole('alert')).toHaveTextContent('Draft preserved');
  expect(screen.getByLabelText('Concentration factor')).toHaveValue('0.5');
  expect(body().steps[0].id).toBe(stepId);
  submit(); await waitFor(() => expect(posts()).toHaveLength(2));
  expect(body(1)).toMatchObject({ code: profile.code, supersedesId: profile.id, versionNo: 2, steps: [{ id: stepId, factorLower: '0.5', factorUpper: '0.5' }] });
  expect(body(1).id).toBe(body().id);
});

it('allocates a new component identity when the physical path changes', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Create profile' })).toBeEnabled());
  await user.selectOptions(screen.getByLabelText('Reviewed profile'), profile.id);
  await user.click(screen.getByRole('button', { name: 'Use reviewed profile' }));
  await user.selectOptions(within(screen.getByRole('dialog')).getByLabelText('Operation'), 'MILLING');
  await user.selectOptions(screen.getByLabelText('Operation material path'), 'DRIED_PADDY:HEAD_RICE:HEAD_RICE');
  await reviewFields(); await confirm(user); submit();
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(body().steps[0]).toMatchObject({ process: 'MILLING', inputStage: 'DRIED_PADDY', outputStage: 'HEAD_RICE' });
  expect(body().steps[0].id).not.toBe(stepId);
});

it('requires reason and confirmation, then exposes retirement without reuse or revision', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Retire profile' })).toBeEnabled());
  await user.click(screen.getByRole('button', { name: 'Retire profile' }));
  const dialog = screen.getByRole('dialog', { name: 'Retire profile series' });
  const confirmRetirement = within(dialog).getByRole('button', { name: 'Confirm retirement' });
  expect(confirmRetirement).toBeDisabled();
  await user.type(within(dialog).getByLabelText('Reason for retirement'), '  Replaced by reviewed policy  ');
  expect(confirmRetirement).toBeDisabled();
  await user.click(within(dialog).getByLabelText('I confirm retirement of this entire profile series.'));
  await user.click(confirmRetirement);
  await screen.findByText('Retired series');
  const request = fetchMock.mock.calls.find(([url]) => String(url) === `/api/residue-rules/${profile.id}/retire`);
  expect(JSON.parse(String(request?.[1]?.body))).toEqual({ reason: 'Replaced by reviewed policy' });
  expect(screen.queryByRole('button', { name: 'Revise profile' })).not.toBeInTheDocument();
  expect(screen.queryByRole('option', { name: /RICE-RES/ })).not.toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Details' }));
  expect(screen.getByText(/Series retired .* by reviewer. Reason: Replaced by reviewed policy/)).toBeInTheDocument();
});
