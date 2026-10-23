import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { ResidueRuleMaintenance } from './ResidueRuleMaintenance';

const fetchMock = vi.fn<typeof fetch>();
const substanceId = '11111111-1111-4111-8111-111111111111';
const commodityId = '22222222-2222-4222-8222-222222222222';
const ruleId = '33333333-3333-4333-8333-333333333333';
const oldRule = {
  id: ruleId, code: 'RICE-RES', versionNo: 1, companyId: null, kind: 'INTERPRETATION', substanceId, commodityId,
  residueDefinition: 'parent compound', inputStage: 'RAW_PADDY', outputStage: 'RAW_PADDY', inputBasis: 'WHOLE', outputBasis: 'WHOLE',
  outputFraction: '', process: '', conditions: '', analyticalMethod: 'LC-MS/MS', unit: 'mg/kg', decimalPlaces: 3,
  interpretationKind: 'ND', ndThresholdKind: 'LOD', factorLower: null, factorUpper: null, factorSemantics: null, homogeneousMixing: false,
  effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: null, supersedesId: null, evidenceRef: 'study-1',
  applicabilityStatement: 'Verified rice scope', reviewedBy: 'Dr A', reviewAuthority: 'QA', reviewedAt: '2026-01-01T00:00:00Z',
  publishedBy: 'publisher', publishedAt: '2026-01-01T00:00:00Z',
};
let publishedRules: Array<typeof oldRule | typeof latestRule>;
let failNextPublication: boolean;
let conflictNextPublication: boolean;
const latestRule = { ...oldRule, id: '44444444-4444-4444-8444-444444444444', versionNo: 2, supersedesId: ruleId, reviewedBy: 'Dr External' };
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const postCalls = () => fetchMock.mock.calls.filter(([url, init]) => String(url) === '/api/residue-rules' && init?.method === 'POST');
const fillReview = async (user: ReturnType<typeof userEvent.setup>, prefix: string) => {
  await user.type(screen.getByLabelText('Evidence reference'), `${prefix}-evidence`);
  await user.type(screen.getByLabelText('Applicability statement'), `${prefix} applicability`);
  await user.type(screen.getByLabelText('Scientific reviewer'), 'Dr A');
  await user.type(screen.getByLabelText('Review authority'), 'QA');
  fireEvent.change(screen.getByLabelText('Effective from'), { target: { value: '2027-01-01T00:00' } });
  fireEvent.change(screen.getByLabelText('Reviewed at'), { target: { value: '2026-10-01T00:00' } });
};
const fillInterpretation = async (user: ReturnType<typeof userEvent.setup>, prefix: string) => {
  await user.click(screen.getByRole('button', { name: 'Interpret laboratory results' }));
  await user.selectOptions(screen.getByLabelText('Substance'), substanceId);
  await user.selectOptions(screen.getByLabelText('Commodity'), commodityId);
  await user.type(screen.getByLabelText('Residue definition'), 'parent compound');
  await user.selectOptions(screen.getByLabelText('Material stage'), 'RAW_PADDY');
  await user.type(screen.getByLabelText('Analytical basis'), 'WHOLE');
  await user.selectOptions(screen.getByLabelText('Interpretation'), 'NUMERIC_ABSOLUTE_UNCERTAINTY');
  await user.type(screen.getByLabelText('Analytical method'), 'LC-MS/MS');
  await user.selectOptions(screen.getByLabelText('Concentration unit'), 'mg/kg');
  await user.type(screen.getByLabelText('Reviewed outward precision (decimal places)'), '3');
  await fillReview(user, prefix);
};

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  publishedRules = [oldRule];
  failNextPublication = false;
  conflictNextPublication = false;
  fetchMock.mockImplementation(async (url, init) => {
    const path = String(url).split('?')[0];
    if (path === '/api/auth/csrf') return response({ token: 'csrf' });
    if (path === '/api/residue-rules' && init?.method === 'POST') {
      if (failNextPublication) { failNextPublication = false; return response({ message: 'Publication failed' }, 500); }
      if (conflictNextPublication) { conflictNextPublication = false; return response({ message: 'Rule history changed' }, 409); }
      const payload = JSON.parse(String(init.body));
      publishedRules = [...publishedRules, { ...oldRule, ...payload, publishedBy: 'publisher', publishedAt: '2026-10-22T00:00:00Z' }];
      return response({});
    }
    if (path === '/api/residue-rules') return response({ items: publishedRules });
    if (path === '/api/active-substances') return response({ items: [{ id: substanceId, code: 'SUB', name: 'Substance' }], hasMore: false });
    if (path === '/api/commodities') return response({ items: [{ id: commodityId, code: 'RICE', name: 'Rice' }], hasMore: false });
    return response({ items: [], hasMore: false });
  });
});

it('starts each task on demand and publishes generated independent identities without omitting scientific fields', async () => {
  const user = userEvent.setup();
  render(<ResidueRuleMaintenance active allowed />);
  await screen.findByText('New rule');
  expect(screen.queryByLabelText('Residue definition')).not.toBeInTheDocument();
  await fillInterpretation(user, 'interpretation');
  const materialStage = screen.getByLabelText('Material stage');
  expect(materialStage.tagName).toBe('SELECT');
  expect(within(materialStage).getAllByRole('option').map(option => (option as HTMLOptionElement).value)).toEqual(['', 'RAW_PADDY', 'DRIED_PADDY', 'HEAD_RICE', 'BROKEN_RICE', 'BRAN', 'HUSK', 'BLENDED_RICE', 'BAGGED_RICE']);
  expect(materialStage).toHaveValue('RAW_PADDY');
  const form = screen.getByRole('button', { name: 'Publish reviewed rule' }).closest('form') as HTMLFormElement;
  expect(form.checkValidity()).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(postCalls()).toHaveLength(1));
  const first = JSON.parse(String(postCalls()[0][1]?.body));
  expect(first.id).toMatch(/^[0-9a-f-]{36}$/i);
  expect(first.code).toBe(`RES-${first.id}`);
  expect(first).toMatchObject({ kind: 'INTERPRETATION', inputStage: 'RAW_PADDY', outputStage: 'RAW_PADDY', versionNo: 1, supersedesId: null, interpretationKind: 'NUMERIC_ABSOLUTE_UNCERTAINTY', ndThresholdKind: null, unit: 'mg/kg', decimalPlaces: 3 });
  await screen.findByRole('button', { name: 'Define processing factors' });

  await user.click(screen.getByRole('button', { name: 'Define processing factors' }));
  await user.selectOptions(screen.getByLabelText('Substance'), substanceId);
  await user.selectOptions(screen.getByLabelText('Commodity'), commodityId);
  await user.type(screen.getByLabelText('Residue definition'), 'parent compound');
  await user.selectOptions(screen.getByLabelText('Input stage'), 'RAW_PADDY');
  await user.selectOptions(screen.getByLabelText('Output stage'), 'HEAD_RICE');
  await user.type(screen.getByLabelText('Input analytical basis'), 'WHOLE');
  await user.type(screen.getByLabelText('Output analytical basis'), 'EDIBLE');
  await user.type(screen.getByLabelText('Output fraction / product identity'), 'head rice');
  await user.type(screen.getByLabelText('Process'), 'milling');
  await user.type(screen.getByLabelText('Conditions or UNCONDITIONAL applicability'), 'dry milling');
  await user.type(screen.getByLabelText('Factor lower bound (decimal)'), '0.2');
  await user.type(screen.getByLabelText('Factor upper bound (decimal)'), '0.4');
  await user.selectOptions(screen.getByLabelText('Factor semantics'), 'STEP_CONCENTRATION');
  await user.click(screen.getByLabelText('Reviewed homogeneous mixing applicability'));
  await user.selectOptions(screen.getByLabelText('Concentration unit'), 'mg/kg');
  await user.type(screen.getByLabelText('Reviewed outward precision (decimal places)'), '3');
  await fillReview(user, 'process');
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(postCalls()).toHaveLength(2));
  const second = JSON.parse(String(postCalls()[1][1]?.body));
  expect(second.code).toBe(`RES-${second.id}`);
  expect(second.id).not.toBe(first.id);
  expect(second).toMatchObject({ kind: 'PROCESS', versionNo: 1, supersedesId: null, factorLower: '0.2', factorUpper: '0.4', factorSemantics: 'STEP_CONCENTRATION', homogeneousMixing: true, conditions: 'dry milling' });
});
it('requires an explicit supported stage when revising a historical rule with unsupported stage values', async () => {
  publishedRules = [{ ...oldRule, inputStage: 'LEGACY_STAGE', outputStage: 'LEGACY_STAGE' }];
  const user = userEvent.setup();
  render(<ResidueRuleMaintenance active allowed />);
  await screen.findByText('New rule');
  await user.click(screen.getByRole('button', { name: /^Revise rule/ }));
  const materialStage = screen.getByLabelText('Material stage');
  expect(materialStage).toHaveValue('');
  const form = screen.getByRole('button', { name: 'Publish reviewed rule' }).closest('form') as HTMLFormElement;
  expect(form.checkValidity()).toBe(false);
  expect(postCalls()).toHaveLength(0);
});


it('keeps generated identity stable after failed publication and retry', async () => {
  const user = userEvent.setup();
  render(<ResidueRuleMaintenance active allowed />);
  await screen.findByText('New rule');
  await fillInterpretation(user, 'retry');
  failNextPublication = true;
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await screen.findByRole('alert');
  const failedPayload = JSON.parse(String(postCalls()[0][1]?.body));
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(postCalls()).toHaveLength(2));
  const retriedPayload = JSON.parse(String(postCalls()[1][1]?.body));
  expect(retriedPayload.id).toBe(failedPayload.id);
  expect(retriedPayload.code).toBe(failedPayload.code);
});

it('revises only latest series version, locks series identity, and exits to fresh independent rule', async () => {
  const user = userEvent.setup();
  render(<ResidueRuleMaintenance active allowed />);
  await screen.findByText('New rule');
  const oldAction = await screen.findByRole('button', { name: 'Revise rule · Interpret laboratory results · SUB · Substance · RICE · Rice · ND · RAW_PADDY · WHOLE · LC-MS/MS · LOD · v1' });
  const oldRow = oldAction.closest('tr');
  expect(oldRow).not.toBeNull();
  const details = within(oldRow!).getByRole('button', { expanded: false });
  expect(details).toHaveAttribute('aria-expanded', 'false');
  await user.click(details);
  expect(details).toHaveAttribute('aria-expanded', 'true');
  expect(screen.getByText('study-1')).toBeInTheDocument();
  expect(screen.getByText(/reviewed at numeric zero: No/)).toBeInTheDocument();
  await user.click(details);
  expect(details).toHaveAttribute('aria-expanded', 'false');
  await user.click(oldAction);
  expect(screen.getByRole('status')).toHaveTextContent(/Revising Interpret laboratory results for SUB · Substance · RICE · Rice\. Previous publication remains unchanged/);
  expect(screen.queryByLabelText('Substance')).not.toBeInTheDocument();
  expect(screen.getByText(/Create new rule to change series identity/)).toBeInTheDocument();
  expect(screen.getByLabelText('Interpretation')).toHaveValue('ND');
  expect(screen.getByLabelText('ND threshold policy')).toHaveValue('LOD');
  expect(screen.getByLabelText('Effective from')).toHaveValue('');
  expect(screen.getByLabelText('Scientific reviewer')).toHaveValue('');
  await fillReview(user, 'revision');
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(publishedRules.some(rule => rule.versionNo === 2 && rule.supersedesId === ruleId)).toBe(true));
  const revision = JSON.parse(String(postCalls()[0][1]?.body));
  expect(revision).toMatchObject({ code: oldRule.code, versionNo: 2, supersedesId: ruleId, kind: oldRule.kind, substanceId, commodityId });
  await screen.findByRole('button', { name: /Revise rule · Interpret laboratory results.*v2/ });
  expect(oldRule).toMatchObject({ versionNo: 1, reviewedBy: 'Dr A' });
  expect(within(oldRow!).getByText(/Already superseded by RICE-RES v2/)).toBeInTheDocument();

  await user.click(screen.getByRole('button', { name: 'Revise rule · Interpret laboratory results · SUB · Substance · RICE · Rice · ND · RAW_PADDY · WHOLE · LC-MS/MS · LOD · v2' }));
  expect(screen.getByLabelText('Scientific reviewer')).toHaveValue('');
  expect(screen.getByRole('button', { name: 'Revise rule · Interpret laboratory results · SUB · Substance · RICE · Rice · ND · RAW_PADDY · WHOLE · LC-MS/MS · LOD · v1' })).toBeDisabled();
  await user.click(screen.getByRole('button', { name: 'Cancel revision' }));
  await user.click(screen.getByRole('button', { name: 'Define processing factors' }));
  expect(screen.queryByLabelText('Rule code')).not.toBeInTheDocument();
  await user.selectOptions(screen.getByLabelText('Substance'), substanceId);
  await user.selectOptions(screen.getByLabelText('Commodity'), commodityId);
  await user.type(screen.getByLabelText('Residue definition'), 'different series');
  await user.selectOptions(screen.getByLabelText('Input stage'), 'RAW_PADDY');
  await user.selectOptions(screen.getByLabelText('Output stage'), 'HEAD_RICE');
  await user.type(screen.getByLabelText('Input analytical basis'), 'WHOLE');
  await user.type(screen.getByLabelText('Output analytical basis'), 'EDIBLE');
  await user.type(screen.getByLabelText('Output fraction / product identity'), 'head rice');
  await user.type(screen.getByLabelText('Process'), 'milling');
  await user.type(screen.getByLabelText('Conditions or UNCONDITIONAL applicability'), 'dry milling');
  await user.type(screen.getByLabelText('Factor lower bound (decimal)'), '0.2');
  await user.type(screen.getByLabelText('Factor upper bound (decimal)'), '0.4');
  await user.selectOptions(screen.getByLabelText('Factor semantics'), 'STEP_CONCENTRATION');
  await user.selectOptions(screen.getByLabelText('Concentration unit'), 'mg/kg');
  await user.type(screen.getByLabelText('Reviewed outward precision (decimal places)'), '3');
  await fillReview(user, 'independent-series');
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(postCalls()).toHaveLength(2));
  const independent = JSON.parse(String(postCalls()[1][1]?.body));
  expect(independent).toMatchObject({ kind: 'PROCESS', versionNo: 1, supersedesId: null, residueDefinition: 'different series' });
  expect(independent.id).not.toBe(revision.id);
  expect(independent.code).toBe(`RES-${independent.id}`);
  expect(independent.code).not.toBe(oldRule.code);
});

it('refreshes on stale revision conflict, preserves draft, and lets user explicitly revise latest version', async () => {
  const user = userEvent.setup();
  render(<ResidueRuleMaintenance active allowed />);
  await screen.findByText('New rule');
  await user.click(screen.getByRole('button', { name: 'Revise rule · Interpret laboratory results · SUB · Substance · RICE · Rice · ND · RAW_PADDY · WHOLE · LC-MS/MS · LOD · v1' }));
  await user.clear(screen.getByLabelText('Evidence reference'));
  await user.type(screen.getByLabelText('Evidence reference'), 'local-evidence');
  await user.clear(screen.getByLabelText('Applicability statement'));
  await user.type(screen.getByLabelText('Applicability statement'), 'local draft scope');
  await user.clear(screen.getByLabelText('Scientific reviewer'));
  await user.type(screen.getByLabelText('Scientific reviewer'), 'Dr Local');
  await user.clear(screen.getByLabelText('Review authority'));
  await user.type(screen.getByLabelText('Review authority'), 'Local QA');
  fireEvent.change(screen.getByLabelText('Effective from'), { target: { value: '2027-01-01T00:00' } });
  fireEvent.change(screen.getByLabelText('Reviewed at'), { target: { value: '2026-10-01T00:00' } });
  publishedRules = [oldRule, latestRule];
  conflictNextPublication = true;
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  expect(await screen.findByRole('alert')).toHaveTextContent(/Published rule history changed\. Your draft is unchanged/);
  expect(screen.getByLabelText('Evidence reference')).toHaveValue('local-evidence');
  expect(screen.getByLabelText('Applicability statement')).toHaveValue('local draft scope');
  expect(screen.getByLabelText('Scientific reviewer')).toHaveValue('Dr Local');
  expect(await screen.findByRole('button', { name: 'Revise rule · Interpret laboratory results · SUB · Substance · RICE · Rice · ND · RAW_PADDY · WHOLE · LC-MS/MS · LOD · v2' })).toBeEnabled();
  expect(screen.getByRole('button', { name: 'Revise rule · Interpret laboratory results · SUB · Substance · RICE · Rice · ND · RAW_PADDY · WHOLE · LC-MS/MS · LOD · v1' })).toBeDisabled();
  expect(postCalls()).toHaveLength(1);

  await user.click(screen.getByRole('button', { name: 'Revise rule · Interpret laboratory results · SUB · Substance · RICE · Rice · ND · RAW_PADDY · WHOLE · LC-MS/MS · LOD · v2' }));
  expect(screen.getByLabelText('Scientific reviewer')).toHaveValue('');
  expect(screen.getByLabelText('Effective from')).toHaveValue('');
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  await fillReview(user, 'latest-review');
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(postCalls()).toHaveLength(2));
  const thirdVersion = JSON.parse(String(postCalls()[1][1]?.body));
  expect(thirdVersion).toMatchObject({ code: oldRule.code, versionNo: 3, supersedesId: latestRule.id, kind: oldRule.kind, substanceId, commodityId });
});

it('clears stale conditional settings and blocks publication without scientific review, ND policy, unit, or precision', async () => {
  const user = userEvent.setup();
  render(<ResidueRuleMaintenance active allowed />);
  await screen.findByText('New rule');
  await user.click(screen.getByRole('button', { name: 'Define processing factors' }));
  await user.selectOptions(screen.getByLabelText('Input stage'), 'DRIED_PADDY');
  await user.selectOptions(screen.getByLabelText('Output stage'), 'HEAD_RICE');
  await user.type(screen.getByLabelText('Input analytical basis'), 'RAW');
  await user.type(screen.getByLabelText('Output analytical basis'), 'PRODUCT');
  await user.type(screen.getByLabelText('Factor lower bound (decimal)'), '0.2');
  await user.type(screen.getByLabelText('Process'), 'MILLING');
  await user.click(screen.getByRole('button', { name: 'Create new rule' }));
  await user.click(screen.getByRole('button', { name: 'Interpret laboratory results' }));
  expect(screen.queryByLabelText('Factor lower bound (decimal)')).not.toBeInTheDocument();
  await user.selectOptions(screen.getByLabelText('Interpretation'), 'ND');
  const form = screen.getByRole('button', { name: 'Publish reviewed rule' }).closest('form') as HTMLFormElement;
  expect(form.checkValidity()).toBe(false);
  expect(postCalls()).toHaveLength(0);
  await user.selectOptions(screen.getByLabelText('ND threshold policy'), 'LOD');
  await user.selectOptions(screen.getByLabelText('Substance'), substanceId);
  await user.selectOptions(screen.getByLabelText('Commodity'), commodityId);
  await user.type(screen.getByLabelText('Residue definition'), 'parent compound');
  await user.selectOptions(screen.getByLabelText('Material stage'), 'RAW_PADDY');
  await user.type(screen.getByLabelText('Analytical basis'), 'WHOLE');
  await user.type(screen.getByLabelText('Analytical method'), 'LC-MS/MS');
  expect(form.checkValidity()).toBe(false);
  expect(postCalls()).toHaveLength(0);
  await fillReview(user, 'missing-science');
  expect(form.checkValidity()).toBe(false);
  await user.selectOptions(screen.getByLabelText('Concentration unit'), 'mg/kg');
  expect(form.checkValidity()).toBe(false);
  await user.type(screen.getByLabelText('Reviewed outward precision (decimal places)'), '3');
  expect(form.checkValidity()).toBe(true);
});
it('publishes relative uncertainty as exact percentage text and explicit reviewed zero setting', async () => {
  const user = userEvent.setup();
  render(<ResidueRuleMaintenance active allowed />);
  await screen.findByText('New rule');
  await fillInterpretation(user, 'relative');
  await user.selectOptions(screen.getByLabelText('Interpretation'), 'NUMERIC_RELATIVE_UNCERTAINTY');
  await user.type(screen.getByLabelText('Relative uncertainty (%)'), '12.500');
  await user.click(screen.getByLabelText('Reviewed relative uncertainty at numeric zero'));
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(postCalls()).toHaveLength(1));
  expect(JSON.parse(String(postCalls()[0][1]?.body))).toMatchObject({
    interpretationKind: 'NUMERIC_RELATIVE_UNCERTAINTY',
    relativeUncertaintyPercent: '12.500',
    relativeUncertaintyAtZero: true,
  });
});
