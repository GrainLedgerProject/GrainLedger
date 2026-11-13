import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { clearLocalSession } from './api';
import { ResidueRuleMaintenance } from './ResidueRuleMaintenance';

const fetchMock = vi.fn<typeof fetch>();
const substanceId = '11111111-1111-4111-8111-111111111111';
const commodityId = '22222222-2222-4222-8222-222222222222';
const ruleId = '33333333-3333-4333-8333-333333333333';
const oldRule = {
  id: ruleId, code: 'RICE-RES', versionNo: 1, companyId: null, kind: 'INTERPRETATION', substanceId, commodityId,
  residueDefinition: 'parent compound', inputStage: 'RAW_PADDY', outputStage: 'RAW_PADDY', inputBasis: 'WHOLE', outputBasis: 'WHOLE',
  outputFraction: '', process: '', conditions: '', analyticalMethod: 'LC-MS/MS', unit: 'mg/kg', decimalPlaces: 3,
  interpretationKind: 'NUMERIC_RELATIVE_UNCERTAINTY', ndThresholdKind: null, factorLower: null as string | null, factorUpper: null as string | null, factorSemantics: null as string | null, homogeneousMixing: false,
  relativeUncertaintyPercent: '10', relativeUncertaintyAtZero: false,
  effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: null, supersedesId: null as string | null, evidenceRef: 'study-1',
  applicabilityStatement: 'Verified rice scope', reviewedBy: 'Dr A', reviewAuthority: 'QA', reviewedAt: '2026-01-01T00:00:00Z',
  publishedBy: 'publisher', publishedAt: '2026-01-01T00:00:00Z',
};
let rules: typeof oldRule[];
let failure: number;
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const posts = () => fetchMock.mock.calls.filter(([url, init]) => String(url) === '/api/residue-rules' && init?.method === 'POST');
// These fixtures exercise publication, not per-keystroke editing. Paste text to
// avoid re-rendering the full scientific profile for every fixture character.
async function fillText(user: UserEvent, label: string, value: string) {
  const input = screen.getByLabelText(label);
  await user.clear(input);
  await user.click(input);
  await user.paste(value);
}
async function review(user: UserEvent) {
  await fillText(user, 'Scientific reviewer', 'Dr Current');
  await fillText(user, 'Review authority', 'Current QA');
  fireEvent.change(screen.getByLabelText('Effective from'), { target: { value: '2027-01-01T00:00' } });
  fireEvent.change(screen.getByLabelText('Reviewed at'), { target: { value: '2026-10-01T00:00' } });
  const confirmation = screen.getByLabelText('I reviewed the complete scientific profile, evidence and effective period.');
  if (!(confirmation as HTMLInputElement).checked) await user.click(confirmation);
}
async function startNew(user: UserEvent, task: string) {
  await user.click(screen.getByRole('button', { name: 'Create rule' }));
  await user.click(screen.getByRole('button', { name: task }));
}
async function fillNew(user: UserEvent, process = false) {
  await startNew(user, process ? 'Define processing factors' : 'Interpret laboratory results');
  await user.selectOptions(screen.getByLabelText('Substance'), substanceId);
  await user.selectOptions(screen.getByLabelText('Commodity'), commodityId);
  await fillText(user, 'Residue definition', 'parent compound');
  if (process) {
    await user.selectOptions(screen.getByLabelText('Factor semantics'), 'STEP_CONCENTRATION');
    await user.selectOptions(screen.getByLabelText('Operation'), 'MILLING');
    await user.selectOptions(screen.getByLabelText('Operation material path'), 'DRIED_PADDY:HEAD_RICE:HEAD_RICE');
    await fillText(user, 'Input analytical basis', 'WHOLE');
    await fillText(user, 'Output analytical basis', 'EDIBLE');
    await user.selectOptions(screen.getByLabelText('Conditions'), 'CONDITIONAL');
    await fillText(user, 'Applicable conditions', 'dry milling');
    await fillText(user, 'Concentration factor (decimal)', '0.200');
  } else {
    await user.selectOptions(screen.getByLabelText('Material stage'), 'RAW_PADDY');
    await fillText(user, 'Analytical basis', 'WHOLE');
    await user.selectOptions(screen.getByLabelText('Interpretation'), 'NUMERIC_ABSOLUTE_UNCERTAINTY');
    await fillText(user, 'Analytical method', 'LC-MS/MS');
  }
  await user.type(screen.getByLabelText('Reviewed outward precision (decimal places)'), '3');
  await fillText(user, 'Evidence reference', 'reviewed-study');
  await fillText(user, 'Applicability statement', 'Reviewed rice scope');
  await review(user);
}
beforeEach(() => {
  clearLocalSession(); vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset(); rules = [oldRule]; failure = 0;
  fetchMock.mockImplementation(async (url, init) => {
    const path = String(url).split('?')[0];
    if (path === '/api/auth/csrf') return response({ token: 'csrf' });
    if (path === '/api/residue-rules' && init?.method === 'POST') {
      if (failure) { const status = failure; failure = 0; return response({ message: 'Publication failed' }, status); }
      rules = [...rules, JSON.parse(String(init.body))]; return response({});
    }
    if (path === '/api/residue-rules') return response({ items: rules });
    if (path === '/api/active-substances') return response({ items: [{ id: substanceId, code: 'SUB', name: 'Substance' }], hasMore: false });
    if (path === '/api/commodities') return response({ items: [{ id: commodityId, code: 'RICE', name: 'Rice' }], hasMore: false });
    return response({ items: [], hasMore: false });
  });
});
it('keeps expert creation closed and supplies no scientific defaults when opened', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Create rule' })).toBeEnabled());
  expect(screen.queryByLabelText('Residue definition')).not.toBeInTheDocument();
  await startNew(user, 'Interpret laboratory results');
  for (const label of ['Substance', 'Commodity', 'Material stage', 'Analytical basis', 'Analytical method', 'Reviewed outward precision (decimal places)', 'Scientific reviewer', 'Reviewed at']) expect(screen.getByLabelText(label)).toHaveValue(label.includes('decimal places') ? null : '');
  expect(screen.getByText('Concentration unit: mg/kg')).toBeInTheDocument();
  expect(screen.queryByRole('combobox', { name: 'Concentration unit' })).not.toBeInTheDocument();
  expect((screen.getByRole('button', { name: 'Publish reviewed rule' }).closest('form') as HTMLFormElement).checkValidity()).toBe(false);
  expect(posts()).toHaveLength(0);
});

it('keeps keyboard focus inside expert form after choosing a task and closes with Escape', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  const trigger = screen.getByRole('button', { name: 'Create rule' });
  await waitFor(() => expect(trigger).toBeEnabled());
  await startNew(user, 'Interpret laboratory results');
  expect(screen.getByRole('dialog', { name: 'Create rule' })).toHaveFocus();
  await user.keyboard('{Escape}');
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(trigger).toHaveFocus();
  expect(posts()).toHaveLength(0);
});
it('publishes independent identities and duplicates a single exact factor only on submission', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Create rule' })).toBeEnabled()); await fillNew(user);
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(1));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()); await fillNew(user, true);
  expect(screen.queryByLabelText('Factor upper bound (decimal)')).not.toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(2));
  const [first, second] = posts().map(([, init]) => JSON.parse(String(init?.body)));
  expect(first).toMatchObject({ kind: 'INTERPRETATION', versionNo: 1, supersedesId: null, unit: 'mg/kg', decimalPlaces: 3, ndThresholdKind: null });
  expect(second).toMatchObject({ kind: 'PROCESS', inputStage: 'DRIED_PADDY', outputStage: 'HEAD_RICE', outputFraction: 'HEAD_RICE', factorLower: '0.200', factorUpper: '0.200', factorSemantics: 'STEP_CONCENTRATION', conditions: 'dry milling' });
  expect(first.code).toBe(`RES-${first.id}`); expect(second.code).toBe(`RES-${second.id}`); expect(second.id).not.toBe(first.id);
});
it('reuses reviewed applicability without retyping and requires fresh scientific review', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await user.click(await screen.findByRole('button', { name: /^Revise rule/ }));
  expect(screen.queryByLabelText('Analytical method')).not.toBeInTheDocument();
  expect(screen.getByLabelText('Profile applicability')).toHaveTextContent('LC-MS/MS');
  expect(screen.getByLabelText('Relative uncertainty (%)')).toHaveValue('10');
  for (const label of ['Scientific reviewer', 'Review authority', 'Reviewed at', 'Effective from']) expect(screen.getByLabelText(label)).toHaveValue('');
  await user.clear(screen.getByLabelText('Relative uncertainty (%)')); await user.type(screen.getByLabelText('Relative uncertainty (%)'), '12.500');
  await user.click(screen.getByLabelText('Reviewed relative uncertainty at numeric zero'));
  await review(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(JSON.parse(String(posts()[0][1]?.body))).toMatchObject({ code: oldRule.code, versionNo: 2, supersedesId: ruleId, substanceId, commodityId, analyticalMethod: 'LC-MS/MS', unit: 'mg/kg', decimalPlaces: 3, evidenceRef: 'study-1', relativeUncertaintyPercent: '12.500', relativeUncertaintyAtZero: true, reviewedBy: 'Dr Current' });
  expect(oldRule.relativeUncertaintyPercent).toBe('10');
});
it('preserves differing historical interval endpoints and exposes applicability only on expert request', async () => {
  rules = [{ ...oldRule, kind: 'PROCESS', interpretationKind: '', process: 'MILLING', inputStage: 'DRIED_PADDY', outputFraction: 'HEAD_RICE', conditions: 'dry milling', outputStage: 'HEAD_RICE', outputBasis: 'EDIBLE', factorLower: '0.200', factorUpper: '0.400', factorSemantics: 'STEP_CONCENTRATION' }];
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await user.click(await screen.findByRole('button', { name: /^Revise rule/ }));
  expect(screen.getByLabelText('Use range')).toBeChecked();
  expect(screen.getByLabelText('Factor lower bound (decimal)')).toHaveValue('0.200');
  expect(screen.getByLabelText('Factor upper bound (decimal)')).toHaveValue('0.400');
  expect(screen.queryByLabelText('Operation material path')).not.toBeInTheDocument();
  await user.click(screen.getByLabelText('Edit scientific applicability (expert)'));
  expect(screen.getByLabelText('Operation material path')).toHaveValue('DRIED_PADDY:HEAD_RICE:HEAD_RICE');
  await review(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(JSON.parse(String(posts()[0][1]?.body))).toMatchObject({ factorLower: '0.200', factorUpper: '0.400' });
});
it('retains UUID on retry and refreshes stale history without overwriting local draft', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await user.click(await screen.findByRole('button', { name: /^Revise rule/ })); await review(user);
  failure = 500; await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' })); await screen.findByRole('alert');
  const id = JSON.parse(String(posts()[0][1]?.body)).id;
  rules = [oldRule, { ...oldRule, id: '44444444-4444-4444-8444-444444444444', versionNo: 2, supersedesId: ruleId }];
  failure = 409; await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await screen.findByText(/Published rule history changed\. Your draft is unchanged/);
  expect(JSON.parse(String(posts()[1][1]?.body)).id).toBe(id);
  expect(screen.getByLabelText('Scientific reviewer')).toHaveValue('Dr Current');
  expect(screen.queryByRole('region', { name: 'Published residue rules' })).not.toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Cancel revision' }));
  await user.click(screen.getByRole('button', { name: /Show earlier versions/ }));
  const buttons = screen.getAllByRole('button', { name: /^Revise rule/ });
  expect(buttons[0]).toBeEnabled(); expect(buttons[1]).toBeDisabled();
  await user.click(buttons[0]); expect(screen.getByLabelText('Scientific reviewer')).toHaveValue('');
  await review(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(3));
  expect(JSON.parse(String(posts()[2][1]?.body))).toMatchObject({ versionNo: 3, supersedesId: rules[1].id });
});
it('does not normalize unsupported historical stages silently', async () => {
  rules = [{ ...oldRule, inputStage: 'LEGACY_STAGE', outputStage: 'LEGACY_STAGE' }];
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await user.click(await screen.findByRole('button', { name: /^Revise rule/ })); await review(user);
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Choose supported input and output stages'); expect(posts()).toHaveLength(0);
  expect(screen.getByLabelText('Edit scientific applicability (expert)')).toBeChecked();
  expect(screen.getByLabelText('Material stage')).toHaveValue('');
  expect(within(screen.getByLabelText('Material stage')).getByRole('option', { name: 'RAW_PADDY' })).toBeInTheDocument();
});
it('requires explicit reviewed ND threshold and clears numeric-only settings on publish', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Create rule' })).toBeEnabled()); await fillNew(user);
  await user.selectOptions(screen.getByLabelText('Interpretation'), 'NUMERIC_RELATIVE_UNCERTAINTY');
  await user.type(screen.getByLabelText('Relative uncertainty (%)'), '12.500');
  await user.selectOptions(screen.getByLabelText('Interpretation'), 'ND');
  const form = screen.getByRole('button', { name: 'Publish reviewed rule' }).closest('form') as HTMLFormElement;
  expect(form.checkValidity()).toBe(false);
  expect(screen.getByLabelText('ND threshold policy')).toHaveValue('');
  await user.selectOptions(screen.getByLabelText('ND threshold policy'), 'LOD');
  await review(user);
  expect(form.checkValidity()).toBe(true);
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(JSON.parse(String(posts()[0][1]?.body))).toMatchObject({ interpretationKind: 'ND', ndThresholdKind: 'LOD', relativeUncertaintyPercent: null, relativeUncertaintyAtZero: false, factorLower: null, factorUpper: null });
});
it('clones a system profile into company scope without retyping applicability or carrying prior review', async () => {
  const companyId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed companyId={companyId} />);
  await user.click(await screen.findByRole('button', { name: /^Clone as company rule/ }));
  expect(screen.queryByLabelText('Substance')).not.toBeInTheDocument();
  expect(screen.getByLabelText('Scientific reviewer')).toHaveValue('');
  expect(screen.getByLabelText('Reviewed at')).toHaveValue('');
  await review(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed company rule' }));
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(JSON.parse(String(posts()[0][1]?.body))).toMatchObject({ companyId, code: oldRule.code, versionNo: 1, supersedesId: null, substanceId, commodityId, analyticalMethod: 'LC-MS/MS', relativeUncertaintyPercent: '10', reviewedBy: 'Dr Current' });
});
it('requires fresh reviewer and date after editing scientific values in an already-reviewed draft', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await user.click(await screen.findByRole('button', { name: /^Revise rule/ })); await review(user);
  await user.type(screen.getByLabelText('Relative uncertainty (%)'), '0');
  expect(screen.getByLabelText('Scientific reviewer')).toHaveValue('');
  expect(screen.getByLabelText('Reviewed at')).toHaveValue('');
  expect((screen.getByRole('button', { name: 'Publish reviewed rule' }).closest('form') as HTMLFormElement).checkValidity()).toBe(false);
  expect(posts()).toHaveLength(0);
});

it('retains matrix publication feedback while refreshed Catalog data is loading and after it arrives', async () => {
  const user = userEvent.setup();
  const originalFetch = fetchMock.getMockImplementation()!;
  let matrixPublished = false;
  let releaseRefresh!: (value: Response) => void;
  const refresh = new Promise<Response>(resolve => { releaseRefresh = resolve; });
  fetchMock.mockImplementation(async (url, init) => {
    const path = String(url).split('?')[0];
    if (path === '/api/residue-matrix/publish' && init?.method === 'POST') {
      matrixPublished = true;
      return response({ rules: [], settings: [] });
    }
    if (path === '/api/residue-rules' && !init?.method && matrixPublished) return refresh;
    return originalFetch(url, init);
  });
  render(<ResidueRuleMaintenance active allowed />);
  await user.click(await screen.findByRole('button', { name: 'Edit selected profiles' }));
  await user.click(screen.getByRole('button', { name: 'Edit system policy · RICE-RES' }));
  fireEvent.change(screen.getByLabelText('Relative uncertainty (%) · RICE-RES'), { target: { value: '20' } });
  fireEvent.change(screen.getByLabelText('Matrix effective from'), { target: { value: '2026-10-01T00:00' } });
  fireEvent.change(screen.getByLabelText('Matrix reviewed at'), { target: { value: '2026-09-01T00:00' } });
  fireEvent.change(screen.getByLabelText('Matrix scientific reviewer'), { target: { value: 'Reviewed scientist' } });
  fireEvent.change(screen.getByLabelText('Matrix review authority'), { target: { value: 'QA' } });
  await user.click(screen.getByLabelText(/I reviewed all changed complete profiles/));
  await user.click(screen.getByRole('button', { name: 'Publish changed matrix rules' }));
  const success = 'Matrix rules and explicitly selected adoption settings published together; unchanged scientific versions retained.';
  expect(await screen.findByText(success)).toBeInTheDocument();
  expect(screen.getByText('Loading published residue rules…')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Edit system policy · RICE-RES' })).toBeDisabled();
  await act(async () => { releaseRefresh(response({ items: rules })); });
  await waitFor(() => expect(screen.queryByText('Loading published residue rules…')).not.toBeInTheDocument());
  expect(screen.getByText(success)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Edit system policy · RICE-RES' })).toBeEnabled();
});

it('requires an explicit complete profile instead of picking the newest method or copying across substances', async () => {
  const otherId = '44444444-4444-4444-8444-444444444444';
  rules = [oldRule,
    { ...oldRule, id: otherId, code: 'OTHER-METHOD', analyticalMethod: 'GC-MS', inputBasis: 'DRY', outputBasis: 'DRY', decimalPlaces: 6 },
    { ...oldRule, id: '55555555-5555-4555-8555-555555555555', code: 'OTHER-SUBSTANCE', substanceId: otherId }];
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  const selector = await screen.findByLabelText('Reviewed profile');
  expect(selector).toHaveValue('');
  expect(screen.getByRole('button', { name: 'Use reviewed profile' })).toBeDisabled();
  expect(screen.queryByLabelText('Residue definition')).not.toBeInTheDocument();
  await user.selectOptions(selector, ruleId);
  await user.click(screen.getByRole('button', { name: 'Use reviewed profile' }));
  expect(screen.getByLabelText('Profile applicability')).toHaveTextContent('LC-MS/MS');
  expect(screen.getByLabelText('Profile applicability')).toHaveTextContent('RAW_PADDY · WHOLE');
  expect(screen.queryByLabelText('Substance')).not.toBeInTheDocument();
  await user.click(screen.getByLabelText('Edit scientific applicability (expert)'));
  expect(screen.queryByLabelText('Substance')).not.toBeInTheDocument();
  expect(screen.queryByLabelText('Commodity')).not.toBeInTheDocument();
  await user.click(screen.getByLabelText('Edit scientific applicability (expert)'));
  await review(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(JSON.parse(String(posts()[0][1]?.body))).toMatchObject({
    code: oldRule.code, versionNo: 2, supersedesId: oldRule.id, substanceId, commodityId,
    residueDefinition: oldRule.residueDefinition, analyticalMethod: 'LC-MS/MS', inputBasis: 'WHOLE', outputBasis: 'WHOLE',
    unit: 'mg/kg', decimalPlaces: 3, relativeUncertaintyPercent: '10', evidenceRef: oldRule.evidenceRef, applicabilityStatement: oldRule.applicabilityStatement,
  });
});

it('offers only current reusable versions and never chooses a source when no reviewed profile exists', async () => {
  rules = [];
  const user = userEvent.setup(); const view = render(<ResidueRuleMaintenance active allowed />);
  await screen.findByText('No reusable profile. Ask an expert to create the missing applicability below.');
  expect(within(screen.getByLabelText('Reviewed profile')).getAllByRole('option')).toHaveLength(1);
  expect(screen.getByRole('button', { name: 'Use reviewed profile' })).toBeDisabled();
  expect(posts()).toHaveLength(0);
  view.unmount();
  const nextId = '44444444-4444-4444-8444-444444444444';
  rules = [oldRule, { ...oldRule, id: nextId, versionNo: 2, supersedesId: ruleId }];
  render(<ResidueRuleMaintenance active allowed />);
  await waitFor(() => expect(within(screen.getByLabelText('Reviewed profile')).getAllByRole('option')).toHaveLength(2));
  expect(within(screen.getByLabelText('Reviewed profile')).queryByRole('option', { name: /RICE-RES v1/ })).not.toBeInTheDocument();
  await user.selectOptions(screen.getByLabelText('Reviewed profile'), nextId);
  await user.click(screen.getByRole('button', { name: 'Use reviewed profile' }));
  await review(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(JSON.parse(String(posts()[0][1]?.body))).toMatchObject({ code: oldRule.code, versionNo: 3, supersedesId: nextId });
});

it.each([
  ['DRYING', '', 'RAW_PADDY', 'DRIED_PADDY', 'DRIED_PADDY'],
  ['MILLING', 'DRIED_PADDY:BRAN:BRAN', 'DRIED_PADDY', 'BRAN', 'BRAN'],
  ['BAGGING', 'HEAD_RICE:BAGGED_RICE:HEAD_RICE', 'HEAD_RICE', 'BAGGED_RICE', 'HEAD_RICE'],
  ['BAGGING', 'BROKEN_RICE:BAGGED_RICE:BROKEN_RICE', 'BROKEN_RICE', 'BAGGED_RICE', 'BROKEN_RICE'],
  ['BAGGING', 'BLENDED_RICE:BAGGED_RICE:BLENDED_RICE', 'BLENDED_RICE', 'BAGGED_RICE', 'BLENDED_RICE'],
  ['BLENDING', 'BROKEN_RICE:BLENDED_RICE:BLENDED_RICE', 'BROKEN_RICE', 'BLENDED_RICE', 'BLENDED_RICE'],
  ['MERGE', 'BAGGED_RICE:BAGGED_RICE:BROKEN_RICE', 'BAGGED_RICE', 'BAGGED_RICE', 'BROKEN_RICE'],
  ['CUSTODY_WITHDRAW', 'BAGGED_RICE:BAGGED_RICE:HEAD_RICE', 'BAGGED_RICE', 'BAGGED_RICE', 'HEAD_RICE'],
])('publishes canonical %s material path %s without asking for structural strings', async (operation, path, inputStage, outputStage, outputFraction) => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Create rule' })).toBeEnabled()); await fillNew(user, true);
  await user.selectOptions(screen.getByLabelText('Operation'), operation);
  if (path) await user.selectOptions(screen.getByLabelText('Operation material path'), path);
  else expect(screen.getByLabelText('Derived material path')).toHaveTextContent('RAW_PADDY → DRIED_PADDY · DRIED_PADDY');
  expect(screen.queryByLabelText('Output fraction / product identity')).not.toBeInTheDocument();
  expect(screen.queryByLabelText('Input stage')).not.toBeInTheDocument();
  await review(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(JSON.parse(String(posts()[0][1]?.body))).toMatchObject({ process: operation, inputStage, outputStage, outputFraction,
    inputBasis: 'WHOLE', outputBasis: 'EDIBLE', factorSemantics: 'STEP_CONCENTRATION', factorLower: '0.200', factorUpper: '0.200' });
});

it('does not invent scientific values when structural drying stages can be derived', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Create rule' })).toBeEnabled()); await startNew(user, 'Define processing factors');
  for (const label of ['Factor semantics', 'Operation', 'Conditions', 'Input analytical basis', 'Output analytical basis', 'Concentration factor (decimal)'])
    expect(screen.getByLabelText(label)).toHaveValue('');
  expect(screen.getByLabelText('Reviewed outward precision (decimal places)')).toHaveValue(null);
  expect(screen.getByLabelText('Reviewed homogeneous mixing applicability')).not.toBeChecked();
  await user.selectOptions(screen.getByLabelText('Factor semantics'), 'STEP_CONCENTRATION');
  await user.selectOptions(screen.getByLabelText('Operation'), 'DRYING');
  expect(screen.getByLabelText('Derived material path')).toHaveTextContent('RAW_PADDY → DRIED_PADDY · DRIED_PADDY');
  expect(screen.getByLabelText('Concentration factor (decimal)')).toHaveValue('');
  expect(screen.getByLabelText('Conditions')).toHaveValue('');
  await user.click(screen.getByLabelText('I reviewed the complete scientific profile, evidence and effective period.'));
  fireEvent.submit(screen.getByRole('button', { name: 'Publish reviewed rule' }).closest('form')!);
  expect(await screen.findByRole('alert')).toHaveTextContent('Set reviewed outward precision');
  expect(posts()).toHaveLength(0);
});

it('stores an explicit no-additional-conditions choice and hides only the unnecessary text field', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Create rule' })).toBeEnabled()); await fillNew(user, true);
  await user.selectOptions(screen.getByLabelText('Conditions'), 'UNCONDITIONAL');
  expect(screen.queryByLabelText('Applicable conditions')).not.toBeInTheDocument();
  expect(screen.getByLabelText('Scientific reviewer')).toHaveValue('');
  await review(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(JSON.parse(String(posts()[0][1]?.body)).conditions).toBe('UNCONDITIONAL');
});

it('retains cumulative raw-paddy anchors and scientific bases through guided reuse and expert inspection', async () => {
  rules = [{ ...oldRule, kind: 'PROCESS', interpretationKind: '', process: 'MILLING', inputStage: 'RAW_PADDY', outputStage: 'HEAD_RICE',
    outputFraction: 'HEAD_RICE', inputBasis: 'WHOLE_PADDY', outputBasis: 'EDIBLE', conditions: 'dry milling only',
    factorLower: '0.2', factorUpper: '0.4', factorSemantics: 'CUMULATIVE_CONCENTRATION' }];
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await waitFor(() => expect(within(screen.getByLabelText('Reviewed profile')).getAllByRole('option')).toHaveLength(2));
  await user.selectOptions(screen.getByLabelText('Reviewed profile'), ruleId);
  await user.click(screen.getByRole('button', { name: 'Use reviewed profile' }));
  expect(screen.getByLabelText('Profile applicability')).toHaveTextContent('CUMULATIVE_CONCENTRATION');
  await user.click(screen.getByLabelText('Edit scientific applicability (expert)'));
  expect(screen.queryByLabelText('Operation material path')).not.toBeInTheDocument();
  expect(screen.getByLabelText('Cumulative input-stage anchor')).toHaveValue('RAW_PADDY');
  expect(screen.getByLabelText('Cumulative output material')).toHaveValue('HEAD_RICE:HEAD_RICE');
  expect(screen.getByLabelText('Input analytical basis')).toHaveValue('WHOLE_PADDY');
  await user.clear(screen.getByLabelText('Factor lower bound (decimal)')); await user.type(screen.getByLabelText('Factor lower bound (decimal)'), '0.25');
  await review(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(JSON.parse(String(posts()[0][1]?.body))).toMatchObject({ factorSemantics: 'CUMULATIVE_CONCENTRATION',
    inputStage: 'RAW_PADDY', outputStage: 'HEAD_RICE', outputFraction: 'HEAD_RICE', inputBasis: 'WHOLE_PADDY', outputBasis: 'EDIBLE',
    factorLower: '0.25', factorUpper: '0.4', conditions: 'dry milling only' });
});

it('keeps historical noncanonical step applicability visible but refuses publication without an explicit correction', async () => {
  rules = [{ ...oldRule, kind: 'PROCESS', interpretationKind: '', process: 'MILLING', inputStage: 'RAW_PADDY', outputStage: 'HEAD_RICE',
    outputFraction: 'head rice', conditions: 'dry milling', factorLower: '0.2', factorUpper: '0.2', factorSemantics: 'STEP_CONCENTRATION' }];
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await user.click(await screen.findByRole('button', { name: /^Revise rule/ }));
  expect(screen.getByLabelText('Profile applicability')).toHaveTextContent('RAW_PADDY');
  expect(screen.getByLabelText('Profile applicability')).toHaveTextContent('head rice');
  await review(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Choose a supported operation and material path');
  expect(posts()).toHaveLength(0);
  expect(screen.getByLabelText('Operation material path')).toHaveValue('');
  expect(screen.getByLabelText('Profile applicability')).toHaveTextContent('head rice');
});

it('requires explicit confirmation even when submission bypasses native required-field checks', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await user.click(await screen.findByRole('button', { name: /^Revise rule/ }));
  await review(user);
  await user.click(screen.getByLabelText('I reviewed the complete scientific profile, evidence and effective period.'));
  fireEvent.submit(screen.getByRole('button', { name: 'Publish reviewed rule' }).closest('form')!);
  expect(await screen.findByRole('alert')).toHaveTextContent('Review and confirm the complete profile');
  expect(posts()).toHaveLength(0);
});

it('creates a company clone through the profile-first flow without moving applicability or inheriting review identity', async () => {
  const companyId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed companyId={companyId} />);
  await waitFor(() => expect(within(screen.getByLabelText('Reviewed profile')).getAllByRole('option')).toHaveLength(2));
  await user.selectOptions(screen.getByLabelText('Reviewed profile'), ruleId);
  await user.click(screen.getByRole('button', { name: 'Use reviewed profile' }));
  expect(screen.getByLabelText('Scientific reviewer')).toHaveValue('');
  expect(screen.getByLabelText('Review authority')).toHaveValue('');
  expect(screen.getByLabelText('Reviewed at')).toHaveValue('');
  expect(screen.getByLabelText('Effective from')).toHaveValue('');
  await user.click(screen.getByLabelText('Edit scientific applicability (expert)'));
  expect(screen.queryByLabelText('Substance')).not.toBeInTheDocument();
  expect(screen.queryByLabelText('Commodity')).not.toBeInTheDocument();
  await review(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed company rule' }));
  await waitFor(() => expect(posts()).toHaveLength(1));
  expect(JSON.parse(String(posts()[0][1]?.body))).toMatchObject({ companyId, code: oldRule.code, versionNo: 1, supersedesId: null,
    substanceId, commodityId, residueDefinition: oldRule.residueDefinition, analyticalMethod: oldRule.analyticalMethod,
    inputBasis: oldRule.inputBasis, outputBasis: oldRule.outputBasis, relativeUncertaintyPercent: '10', reviewedBy: 'Dr Current', reviewAuthority: 'Current QA' });
});

it('preserves selected matrix drafts when dialog closes and restores focus to its trigger', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  const trigger = await screen.findByRole('button', { name: 'Edit selected profiles' });
  await user.click(trigger);
  await user.click(screen.getByRole('button', { name: 'Edit system policy · RICE-RES' }));
  await user.clear(screen.getByLabelText('Relative uncertainty (%) · RICE-RES'));
  await user.type(screen.getByLabelText('Relative uncertainty (%) · RICE-RES'), '17');
  await user.keyboard('{Escape}');
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(trigger).toHaveFocus();
  await user.click(screen.getByRole('button', { name: 'Resume selected profile edits (1)' }));
  expect(screen.getByLabelText('Relative uncertainty (%) · RICE-RES')).toHaveValue('17');
  expect(posts()).toHaveLength(0);
});

it('keeps latest scoped heads and conflicting tips visible while historical search reveals earlier metadata', async () => {
  rules = [oldRule, { ...oldRule, id: 'new-head', versionNo: 2, supersedesId: ruleId, evidenceRef: 'new-study', analyticalMethod: 'NEW-METHOD', effectiveFrom: '2099-01-01T00:00:00Z' },
    { ...oldRule, id: 'conflict-one', code: 'CONFLICT' }, { ...oldRule, id: 'conflict-two', code: 'CONFLICT', versionNo: 2 }];
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await screen.findByText(/2 current scoped series/);
  expect(screen.getAllByText('Latest published tip')).toHaveLength(3);
  expect(screen.queryByText('Superseded history')).not.toBeInTheDocument();
  expect(screen.getByText('Scheduled · not an adoption decision')).toBeInTheDocument();
  expect(screen.getByText(/Conflicting published tips — every tip retained/)).toBeInTheDocument();
  await user.selectOptions(screen.getByLabelText('Rule scope'), 'system');
  await user.selectOptions(screen.getByLabelText('Rule task'), 'INTERPRETATION');
  expect(screen.queryByText('Superseded history')).not.toBeInTheDocument();
  await user.type(screen.getByLabelText('Search rules and history'), 'RICE-RES');
  expect(screen.queryByText('Superseded history')).not.toBeInTheDocument();
  await user.clear(screen.getByLabelText('Search rules and history'));
  await user.type(screen.getByLabelText('Search rules and history'), 'study-1');
  expect(screen.getByText('Superseded history')).toBeInTheDocument();
  expect(screen.getAllByText('Latest published tip')).toHaveLength(3);
  expect(screen.getByRole('button', { name: 'Details for RICE-RES v1 · System' })).toBeInTheDocument();
  await user.clear(screen.getByLabelText('Search rules and history'));
  await user.type(screen.getByLabelText('Search rules and history'), 'no-such-profile');
  expect(screen.getByText(/No series match these filters/)).toBeInTheDocument();
});
