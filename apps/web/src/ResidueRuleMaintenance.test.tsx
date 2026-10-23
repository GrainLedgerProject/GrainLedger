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
async function review(user: UserEvent) {
  await user.type(screen.getByLabelText('Scientific reviewer'), 'Dr Current');
  await user.type(screen.getByLabelText('Review authority'), 'Current QA');
  fireEvent.change(screen.getByLabelText('Effective from'), { target: { value: '2027-01-01T00:00' } });
  fireEvent.change(screen.getByLabelText('Reviewed at'), { target: { value: '2026-10-01T00:00' } });
}
async function startNew(user: UserEvent, task: string) {
  await user.click(screen.getByText('Create new scientific profile'));
  await user.click(screen.getByRole('button', { name: task }));
}
async function fillNew(user: UserEvent, process = false) {
  await startNew(user, process ? 'Define processing factors' : 'Interpret laboratory results');
  await user.selectOptions(screen.getByLabelText('Substance'), substanceId);
  await user.selectOptions(screen.getByLabelText('Commodity'), commodityId);
  await user.type(screen.getByLabelText('Residue definition'), 'parent compound');
  if (process) {
    await user.selectOptions(screen.getByLabelText('Input stage'), 'RAW_PADDY');
    await user.selectOptions(screen.getByLabelText('Output stage'), 'HEAD_RICE');
    await user.type(screen.getByLabelText('Input analytical basis'), 'WHOLE');
    await user.type(screen.getByLabelText('Output analytical basis'), 'EDIBLE');
    await user.type(screen.getByLabelText('Output fraction / product identity'), 'head rice');
    await user.type(screen.getByLabelText('Process'), 'MILLING');
    await user.type(screen.getByLabelText('Conditions or UNCONDITIONAL applicability'), 'dry milling');
    await user.type(screen.getByLabelText('Concentration factor (decimal)'), '0.200');
    await user.selectOptions(screen.getByLabelText('Factor semantics'), 'STEP_CONCENTRATION');
  } else {
    await user.selectOptions(screen.getByLabelText('Material stage'), 'RAW_PADDY');
    await user.type(screen.getByLabelText('Analytical basis'), 'WHOLE');
    await user.selectOptions(screen.getByLabelText('Interpretation'), 'NUMERIC_ABSOLUTE_UNCERTAINTY');
    await user.type(screen.getByLabelText('Analytical method'), 'LC-MS/MS');
  }
  await user.selectOptions(screen.getByLabelText('Concentration unit'), 'mg/kg');
  await user.type(screen.getByLabelText('Reviewed outward precision (decimal places)'), '3');
  await user.type(screen.getByLabelText('Evidence reference'), 'reviewed-study');
  await user.type(screen.getByLabelText('Applicability statement'), 'Reviewed rice scope');
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
  await screen.findByText('Create new scientific profile');
  expect(screen.queryByLabelText('Residue definition')).not.toBeInTheDocument();
  await startNew(user, 'Interpret laboratory results');
  for (const label of ['Substance', 'Commodity', 'Material stage', 'Analytical basis', 'Analytical method', 'Concentration unit', 'Reviewed outward precision (decimal places)', 'Scientific reviewer', 'Reviewed at']) expect(screen.getByLabelText(label)).toHaveValue(label.includes('decimal places') ? null : '');
  expect((screen.getByRole('button', { name: 'Publish reviewed rule' }).closest('form') as HTMLFormElement).checkValidity()).toBe(false);
  expect(posts()).toHaveLength(0);
});
it('publishes independent identities and duplicates a single exact factor only on submission', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await screen.findByText('Create new scientific profile'); await fillNew(user);
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(1));
  await screen.findByText('Create new scientific profile'); await fillNew(user, true);
  expect(screen.queryByLabelText('Factor upper bound (decimal)')).not.toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Publish reviewed rule' }));
  await waitFor(() => expect(posts()).toHaveLength(2));
  const [first, second] = posts().map(([, init]) => JSON.parse(String(init?.body)));
  expect(first).toMatchObject({ kind: 'INTERPRETATION', versionNo: 1, supersedesId: null, unit: 'mg/kg', decimalPlaces: 3, ndThresholdKind: null });
  expect(second).toMatchObject({ kind: 'PROCESS', factorLower: '0.200', factorUpper: '0.200', factorSemantics: 'STEP_CONCENTRATION' });
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
  rules = [{ ...oldRule, kind: 'PROCESS', interpretationKind: '', process: 'MILLING', outputFraction: 'head rice', conditions: 'dry milling', outputStage: 'HEAD_RICE', outputBasis: 'EDIBLE', factorLower: '0.200', factorUpper: '0.400', factorSemantics: 'STEP_CONCENTRATION' }];
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await user.click(await screen.findByRole('button', { name: /^Revise rule/ }));
  expect(screen.getByLabelText('Use range')).toBeChecked();
  expect(screen.getByLabelText('Factor lower bound (decimal)')).toHaveValue('0.200');
  expect(screen.getByLabelText('Factor upper bound (decimal)')).toHaveValue('0.400');
  expect(screen.queryByLabelText('Input stage')).not.toBeInTheDocument();
  await user.click(screen.getByLabelText('Edit scientific applicability (expert)'));
  expect(screen.getByLabelText('Input stage')).toHaveValue('RAW_PADDY');
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
  const buttons = screen.getAllByRole('button', { name: /^Revise rule/ });
  expect(buttons[0]).toBeDisabled(); expect(buttons[1]).toBeEnabled();
  await user.click(buttons[1]); expect(screen.getByLabelText('Scientific reviewer')).toHaveValue('');
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
  await user.click(screen.getByLabelText('Edit scientific applicability (expert)'));
  expect(screen.getByLabelText('Material stage')).toHaveValue('');
  expect(within(screen.getByLabelText('Material stage')).getByRole('option', { name: 'RAW_PADDY' })).toBeInTheDocument();
});
it('requires explicit reviewed ND threshold and clears numeric-only settings on publish', async () => {
  const user = userEvent.setup(); render(<ResidueRuleMaintenance active allowed />);
  await screen.findByText('Create new scientific profile'); await fillNew(user);
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
  await user.click(await screen.findByRole('button', { name: 'Edit system policy · RICE-RES' }));
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
