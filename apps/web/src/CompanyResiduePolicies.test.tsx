import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { clearLocalSession } from './api';
import { CompanyResiduePolicies } from './CompanyResiduePolicies';

const fetchMock = vi.fn<typeof fetch>();
const companyId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const systemRule = { id: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', code: 'RICE-RES', versionNo: 1, companyId: null, kind: 'INTERPRETATION', substanceId: 'substance', commodityId: 'commodity', residueDefinition: 'parent compound', inputStage: 'RAW_PADDY', outputStage: 'RAW_PADDY', inputBasis: 'WHOLE', outputBasis: 'WHOLE', outputFraction: '', process: '', conditions: '', analyticalMethod: 'LC-MS/MS', unit: 'mg/kg', decimalPlaces: 3, interpretationKind: 'NUMERIC_RELATIVE_UNCERTAINTY', ndThresholdKind: null, factorLower: null, factorUpper: null, factorSemantics: null, homogeneousMixing: false, relativeUncertaintyPercent: '10', relativeUncertaintyAtZero: false, effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: null, supersedesId: null, evidenceRef: 'scientific-source', applicabilityStatement: 'Rice only', reviewedBy: 'Dr A', reviewAuthority: 'Scientific QA', reviewedAt: '2026-01-01T00:00:00Z', publishedBy: 'admin', publishedAt: '2026-01-01T00:00:00Z' };
const otherRule = { ...systemRule, id: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', code: 'RICE-PROCESS', evidenceRef: 'processing-study', applicabilityStatement: 'Dry rice processing only' };
const companyRule = { ...systemRule, id: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', companyId, relativeUncertaintyPercent: '20', evidenceRef: 'company-study' };
let rules: Array<Record<string, unknown>>, settings: Array<Record<string, unknown>>, posted: Array<{ items: Array<Record<string, unknown>> }>, failure: number;
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
// Paste fixture text while retaining user interaction and publication checks.
async function pasteText(user: UserEvent, input: HTMLElement, value: string) {
  await user.click(input);
  await user.paste(value);
}
async function fillReview(user: UserEvent) {
  await pasteText(user, screen.getByLabelText('Adoption reviewer'), 'Actual adoption reviewer');
  await pasteText(user, screen.getByLabelText('Review authority'), 'Company QA');
  fireEvent.change(screen.getByLabelText('Effective from'), { target: { value: '2026-10-23T00:00' } });
  fireEvent.change(screen.getByLabelText('Reviewed at'), { target: { value: '2026-10-22T00:00' } });
  await user.click(screen.getByRole('checkbox', { name: /I reviewed every selected profile/ }));
}
beforeEach(() => {
  clearLocalSession(); vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset();
  rules = [systemRule]; settings = []; posted = []; failure = 0;
  fetchMock.mockImplementation(async (url, init) => {
    const path = String(url).split('?')[0];
    if (path === '/api/auth/csrf') return response({ token: 'csrf' });
    if (path === '/api/residue-rules' && init?.method === 'POST') {
      const payload = JSON.parse(String(init.body)); rules = [...rules, payload]; return response(payload);
    }
    if (path === '/api/residue-rules') return response({ items: rules });
    if (path === '/api/residue-policy-settings/batch' && init?.method === 'POST') {
      const payload = JSON.parse(String(init.body)); posted.push(payload);
      if (failure) { const status = failure; failure = 0; return response({ message: 'Publication failed' }, status); }
      settings = [...settings, ...payload.items]; return response({ items: payload.items });
    }
    if (path === '/api/residue-policy-settings') return response({ items: settings });
    if (path === '/api/active-substances') return response({ items: [{ id: 'substance', code: 'SUB', name: 'Human substance' }], hasMore: false });
    if (path === '/api/commodities') return response({ items: [{ id: 'commodity', code: 'RICE', name: 'Human commodity' }], hasMore: false });
    return response({ items: [], hasMore: false });
  });
});
it('reuses source evidence and applicability, but requires a distinct explicit company adoption review', async () => {
  const user = userEvent.setup(); render(<CompanyResiduePolicies active allowed companyId={companyId} />);
  await screen.findByRole('checkbox', { name: /Human substance.*Human commodity.*RICE-RES/ });
  expect(screen.queryByLabelText('Adoption reviewer')).not.toBeInTheDocument();
  await user.click(screen.getByRole('checkbox', { name: /RICE-RES/ }));
  expect(screen.getByLabelText('Evidence reference · RICE-RES')).toHaveValue('scientific-source');
  expect(screen.getByLabelText('Applicability statement · RICE-RES')).toHaveValue('Rice only');
  expect(screen.getByLabelText('Adoption reviewer')).toHaveValue('');
  expect(screen.getByLabelText('Reviewed at')).toHaveValue('');
  expect(screen.getByRole('button', { name: 'Publish reviewed policy settings' })).toBeDisabled();
  await fillReview(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed policy settings' }));
  await waitFor(() => expect(posted).toHaveLength(1));
  expect(posted[0].items).toHaveLength(1);
  expect(posted[0].items[0]).toMatchObject({ companyId, ruleCode: 'RICE-RES', versionNo: 1, mode: 'SYSTEM_DEFAULT', supersedesId: null, evidenceRef: 'scientific-source', applicabilityStatement: 'Rice only', reviewedBy: 'Actual adoption reviewer', reviewAuthority: 'Company QA' });
  expect(posted[0].items[0]).not.toHaveProperty('publishedBy');
});
it('publishes selected profiles in one request with one review and separate immutable setting identities', async () => {
  rules = [systemRule, otherRule];
  const user = userEvent.setup(); render(<CompanyResiduePolicies active allowed companyId={companyId} />);
  await screen.findByRole('checkbox', { name: /RICE-PROCESS/ });
  await user.click(screen.getByRole('checkbox', { name: /RICE-RES/ }));
  await user.click(screen.getByRole('checkbox', { name: /RICE-PROCESS/ }));
  expect(screen.getAllByLabelText('Adoption reviewer')).toHaveLength(1);
  await fillReview(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed policy settings' }));
  await waitFor(() => expect(posted).toHaveLength(1));
  const [first, second] = posted[0].items;
  expect(first.id).not.toBe(second.id);
  expect(first.reviewedAt).toBe(second.reviewedAt); expect(first.effectiveFrom).toBe(second.effectiveFrom);
  expect(first.evidenceRef).toBe('scientific-source'); expect(second.evidenceRef).toBe('processing-study');
  expect(second.applicabilityStatement).toBe('Dry rice processing only');
});
it('selects reviewed company source on override and appends explicit return to system history', async () => {
  rules = [systemRule, companyRule];
  settings = [{ id: 'first-setting', companyId, ruleCode: 'RICE-RES', versionNo: 1, mode: 'SYSTEM_DEFAULT' }];
  const user = userEvent.setup(); render(<CompanyResiduePolicies active allowed companyId={companyId} />);
  await screen.findByRole('checkbox', { name: /RICE-RES/ });
  await user.click(screen.getByRole('checkbox', { name: /RICE-RES/ }));
  await user.selectOptions(screen.getByLabelText('Setting mode'), 'COMPANY_OVERRIDE');
  expect(screen.getByLabelText('Evidence reference · RICE-RES')).toHaveValue('company-study');
  await fillReview(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed policy settings' }));
  await waitFor(() => expect(posted).toHaveLength(1));
  expect(posted[0].items[0]).toMatchObject({ mode: 'COMPANY_OVERRIDE', versionNo: 2, supersedesId: 'first-setting' });
  await waitFor(() => expect(screen.queryByLabelText('Adoption reviewer')).not.toBeInTheDocument());
  await user.selectOptions(screen.getByLabelText('Setting mode'), 'SYSTEM_DEFAULT');
  await user.click(screen.getByRole('checkbox', { name: /RICE-RES/ }));
  expect(screen.getByLabelText('Evidence reference · RICE-RES')).toHaveValue('scientific-source');
  await fillReview(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed policy settings' }));
  await waitFor(() => expect(posted).toHaveLength(2));
  expect(posted[1].items[0]).toMatchObject({ mode: 'SYSTEM_DEFAULT', versionNo: 3, supersedesId: posted[0].items[0].id });
});
it('does not offer unavailable override as system fallback', async () => {
  const user = userEvent.setup(); render(<CompanyResiduePolicies active allowed companyId={companyId} />);
  await screen.findByRole('checkbox', { name: /RICE-RES/ });
  await user.selectOptions(screen.getByLabelText('Setting mode'), 'COMPANY_OVERRIDE');
  expect(screen.getByRole('checkbox', { name: /RICE-RES/ })).toBeDisabled();
  expect(posted).toHaveLength(0);
});
it('retains batch UUIDs on failed publication and retry', async () => {
  const user = userEvent.setup(); render(<CompanyResiduePolicies active allowed companyId={companyId} />);
  await screen.findByRole('checkbox', { name: /RICE-RES/ });
  await user.click(screen.getByRole('checkbox', { name: /RICE-RES/ })); await fillReview(user);
  failure = 500; await user.click(screen.getByRole('button', { name: 'Publish reviewed policy settings' })); await screen.findByRole('alert');
  await user.click(screen.getByRole('button', { name: 'Publish reviewed policy settings' }));
  await waitFor(() => expect(posted).toHaveLength(2)); expect(posted[0]).toEqual(posted[1]);
});
it('requires adoption confirmation again after changing the shared review', async () => {
  const user = userEvent.setup(); render(<CompanyResiduePolicies active allowed companyId={companyId} />);
  await screen.findByRole('checkbox', { name: /RICE-RES/ });
  await user.click(screen.getByRole('checkbox', { name: /RICE-RES/ })); await fillReview(user);
  expect(screen.getByRole('button', { name: 'Publish reviewed policy settings' })).toBeEnabled();
  await user.type(screen.getByLabelText('Adoption reviewer'), ' amended');
  expect(screen.getByRole('checkbox', { name: /I reviewed every selected profile/ })).not.toBeChecked();
  expect(screen.getByRole('button', { name: 'Publish reviewed policy settings' })).toBeDisabled();
  expect(posted).toHaveLength(0);
});
it('blocks checked adoption after embedded scientific revision until refreshed sources receive fresh adoption review', async () => {
  rules = [systemRule, companyRule];
  const user = userEvent.setup(); render(<CompanyResiduePolicies active allowed companyId={companyId} />);
  await screen.findByRole('checkbox', { name: /RICE-RES/ });
  await user.selectOptions(screen.getByLabelText('Setting mode'), 'COMPANY_OVERRIDE');
  await user.click(screen.getByRole('checkbox', { name: /RICE-RES/ }));
  await user.click(screen.getByText('Customize adoption evidence or applicability'));
  await user.clear(screen.getByLabelText('Evidence reference · RICE-RES'));
  await pasteText(user, screen.getByLabelText('Evidence reference · RICE-RES'), 'Company-specific adoption evidence');
  await fillReview(user);
  expect(screen.getByRole('button', { name: 'Publish reviewed policy settings' })).toBeEnabled();

  const editor = within(screen.getByRole('heading', { name: 'Company residue rules' }).closest('section')!);
  await user.click(editor.getByRole('button', { name: /^Revise rule/ }));
  await user.clear(editor.getByLabelText('Relative uncertainty (%)'));
  await pasteText(user, editor.getByLabelText('Relative uncertainty (%)'), '25');
  await user.clear(editor.getByLabelText('Evidence reference'));
  await pasteText(user, editor.getByLabelText('Evidence reference'), 'new-scientific-study');
  await user.clear(editor.getByLabelText('Applicability statement'));
  await pasteText(user, editor.getByLabelText('Applicability statement'), 'New reviewed rice applicability');
  await pasteText(user, editor.getByLabelText('Scientific reviewer'), 'Actual scientist');
  await pasteText(user, editor.getByLabelText('Review authority'), 'Scientific QA');
  fireEvent.change(editor.getByLabelText('Effective from'), { target: { value: '2026-10-23T00:00' } });
  fireEvent.change(editor.getByLabelText('Reviewed at'), { target: { value: '2026-10-22T00:00' } });
  await user.click(editor.getByLabelText('I reviewed the complete scientific profile, evidence and effective period.'));
  await user.click(editor.getByRole('button', { name: 'Publish reviewed company rule' }));

  await screen.findByRole('button', { name: 'Review updated profile sources' });
  expect(screen.getByRole('checkbox', { name: /I reviewed every selected profile/ })).not.toBeChecked();
  expect(screen.getByRole('checkbox', { name: /I reviewed every selected profile/ })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Publish reviewed policy settings' })).toBeDisabled();
  expect(screen.getByLabelText('Evidence reference · RICE-RES')).toHaveValue('Company-specific adoption evidence');
  expect(screen.getByLabelText('Applicability statement · RICE-RES')).toHaveValue('Rice only');
  expect(posted).toHaveLength(0);

  await user.click(screen.getByRole('button', { name: 'Review updated profile sources' }));
  expect(screen.getByLabelText('Evidence reference · RICE-RES')).toHaveValue('Company-specific adoption evidence');
  expect(screen.getByLabelText('Applicability statement · RICE-RES')).toHaveValue('New reviewed rice applicability');
  expect(screen.getByLabelText('Adoption reviewer')).toHaveValue('');
  expect(screen.getByLabelText('Reviewed at')).toHaveValue('');
  await fillReview(user); await user.click(screen.getByRole('button', { name: 'Publish reviewed policy settings' }));
  await waitFor(() => expect(posted).toHaveLength(1));
  expect(posted[0].items[0]).toMatchObject({ evidenceRef: 'Company-specific adoption evidence', applicabilityStatement: 'New reviewed rice applicability', reviewedBy: 'Actual adoption reviewer' });
  expect(posted[0].items[0]).not.toHaveProperty('sourceRuleId');
});

it('does not silently choose a same-code profile when published series has conflicting roots', async () => {
  rules = [systemRule, { ...systemRule, id: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', versionNo: 2 }];
  const user = userEvent.setup(); render(<CompanyResiduePolicies active allowed companyId={companyId} />);
  expect(await screen.findByRole('checkbox', { name: /RICE-RES/ })).toBeDisabled();
  await user.click(await screen.findByRole('button', { name: 'Edit selected profiles' }));
  expect(await screen.findByRole('button', { name: 'Edit company policy · RICE-RES' })).toBeDisabled();
  expect(screen.getAllByText(/Conflicting applicability/).length).toBeGreaterThan(0);
  expect(posted).toHaveLength(0);
});

it('searches earlier adoption evidence without replacing latest setting and preserves selections across filters', async () => {
  rules = [systemRule, otherRule];
  settings = [
    { id: 'earlier', companyId, ruleCode: 'RICE-RES', versionNo: 1, mode: 'COMPANY_OVERRIDE', supersedesId: null, effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: null, evidenceRef: 'old-evidence', applicabilityStatement: 'Prior reviewed scope' },
    { id: 'head', companyId, ruleCode: 'RICE-RES', versionNo: 2, mode: 'SYSTEM_DEFAULT', supersedesId: 'earlier', effectiveFrom: '2099-01-01T00:00:00Z', effectiveTo: null, evidenceRef: 'new-evidence', applicabilityStatement: 'Current reviewed scope' },
  ];
  const user = userEvent.setup(); render(<CompanyResiduePolicies active allowed companyId={companyId} />);
  await user.click(await screen.findByRole('checkbox', { name: /RICE-RES/ }));
  await user.type(screen.getByLabelText('Find profiles to adopt'), 'RICE-PROCESS');
  expect(screen.queryByRole('checkbox', { name: /RICE-RES/ })).not.toBeInTheDocument();
  expect(screen.getByRole('region', { name: 'Selected profile RICE-RES' })).toBeInTheDocument();
  await user.clear(screen.getByLabelText('Find profiles to adopt'));
  expect(screen.getByRole('checkbox', { name: /RICE-RES/ })).toBeChecked();
  const history = within(screen.getByRole('region', { name: 'Adoption history RICE-RES' }));
  expect(history.getByText('Earlier adoption versions · RICE-RES · 1').closest('details')).not.toHaveAttribute('open');
  await user.type(screen.getByLabelText('Search adoption history'), 'RICE-RES');
  expect(history.getByText('Earlier adoption versions · RICE-RES · 1').closest('details')).not.toHaveAttribute('open');
  await user.clear(screen.getByLabelText('Search adoption history'));
  await user.type(screen.getByLabelText('Search adoption history'), 'old-evidence');
  expect(history.getByText('Earlier adoption versions · RICE-RES · 1').closest('details')).toHaveAttribute('open');
  expect(history.getByText('v2 · System default')).toBeInTheDocument();
  expect(history.getByText('v1 · Company override')).toBeInTheDocument();
  await user.selectOptions(screen.getByLabelText('History setting mode'), 'COMPANY_OVERRIDE');
  expect(history.getByText('v2 · System default')).toBeInTheDocument();
  expect(posted).toHaveLength(0);
});
