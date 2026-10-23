import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent, { type UserEvent } from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { clearLocalSession } from './api';
import { ResidueMatrix } from './ResidueMatrix';
import type { Rule } from './ResidueRuleMaintenance';

const fetchMock = vi.fn<typeof fetch>();
const onPublished = vi.fn();
const substanceId = '11111111-1111-4111-8111-111111111111';
const commodityId = '22222222-2222-4222-8222-222222222222';
const companyId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const rule: Rule = {
  id: '33333333-3333-4333-8333-333333333333', code: 'NUMERIC-RICE', versionNo: 1, companyId: null,
  kind: 'INTERPRETATION', substanceId, commodityId, residueDefinition: 'parent compound', analyticalMethod: 'LC-MS/MS',
  inputStage: 'RAW_PADDY', outputStage: 'RAW_PADDY', inputBasis: 'WHOLE', outputBasis: 'WHOLE', outputFraction: '', process: '', conditions: '',
  unit: 'mg/kg', decimalPlaces: 3, interpretationKind: 'NUMERIC_RELATIVE_UNCERTAINTY', ndThresholdKind: null,
  relativeUncertaintyPercent: '10', relativeUncertaintyAtZero: false,
  factorLower: null, factorUpper: null, factorSemantics: null, homogeneousMixing: false,
  effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: null, supersedesId: null, evidenceRef: 'reviewed study',
  applicabilityStatement: 'Rice only', reviewedBy: 'Dr A', reviewAuthority: 'QA', reviewedAt: '2026-01-01T00:00:00Z',
  publishedBy: 'publisher', publishedAt: '2026-01-01T00:00:00Z',
};
const process: Rule = { ...rule, id: '44444444-4444-4444-8444-444444444444', code: 'RICE-DRYING', kind: 'PROCESS', process: 'DRYING',
  outputStage: 'DRIED_PADDY', outputFraction: 'whole', conditions: 'reviewed dry operation', interpretationKind: null,
  relativeUncertaintyPercent: null, factorLower: '0.40', factorUpper: '0.70', factorSemantics: 'STEP_CONCENTRATION' };
const substances = [{ id: substanceId, code: 'SUB', name: 'Catalog substance' }, { id: 'unconfigured', code: 'NEW', name: 'New Catalog substance' }];
let failure = 0;
beforeEach(() => {
  clearLocalSession(); vi.stubGlobal('fetch', fetchMock); fetchMock.mockReset(); onPublished.mockReset(); failure = 0;
  fetchMock.mockImplementation(async (url) => new Response(JSON.stringify(String(url) === '/api/auth/csrf' ? { token: 'csrf' }
    : failure ? { title: 'Stale history' } : { rules: [], settings: [] }), { status: String(url) === '/api/auth/csrf' ? 200 : failure || 200, headers: { 'Content-Type': 'application/json' } }));
});
async function completeReview(user: UserEvent) {
  fireEvent.change(screen.getByLabelText('Matrix effective from'), { target: { value: '2026-10-01T00:00' } });
  fireEvent.change(screen.getByLabelText('Matrix reviewed at'), { target: { value: '2026-09-01T00:00' } });
  await user.type(screen.getByLabelText('Matrix scientific reviewer'), 'Current reviewer');
  await user.type(screen.getByLabelText('Matrix review authority'), 'QA');
  await user.click(screen.getByLabelText(/I reviewed all changed complete profiles/));
}
it('shows dynamic substance rows and publishes only changed complete scientific rules under one review', async () => {
  const user = userEvent.setup();
  render(<ResidueMatrix rules={[rule, process]} substances={substances} commodities={[{ id: commodityId, code: 'RICE', name: 'Rice' }]} companyId={null} settings={[]} onPublished={onPublished} />);
  expect(screen.getByRole('rowheader', { name: 'NEW · New Catalog substance' })).toBeInTheDocument();
  expect(within(screen.getByRole('row', { name: /NEW · New Catalog substance/ })).getAllByText(/Missing profile/)).toHaveLength(3);
  await user.click(screen.getByRole('button', { name: 'Edit system policy · NUMERIC-RICE' }));
  const uncertainty = screen.getByLabelText('Relative uncertainty (%) · NUMERIC-RICE');
  await user.clear(uncertainty); await user.type(uncertainty, '12.500');
  await completeReview(user);
  await user.click(screen.getByRole('button', { name: 'Publish changed matrix rules' }));
  await waitFor(() => expect(onPublished).toHaveBeenCalledOnce());
  const publication = fetchMock.mock.calls.find(([url]) => String(url) === '/api/residue-matrix/publish');
  const body = JSON.parse(String(publication?.[1]?.body));
  expect(body.rules).toHaveLength(1); expect(body.settings).toEqual([]);
  expect(body.rules[0]).toMatchObject({ baseId: rule.id, sourceId: rule.id, rule: { ...rule, relativeUncertaintyPercent: '12.500' } });
  expect(body.reviewedBy).toBe('Current reviewer'); expect(body.effectiveFrom).toBe(new Date('2026-10-01T00:00').toISOString());
});
it('retains factor ranges, permits explicit scalar and requires explicit override adoption', async () => {
  const user = userEvent.setup();
  render(<ResidueMatrix rules={[process]} substances={substances} commodities={[]} companyId={companyId} settings={[]} onPublished={onPublished} />);
  await user.click(screen.getByRole('button', { name: 'Edit company policy · RICE-DRYING' }));
  expect(screen.getByLabelText('Factor range · RICE-DRYING')).toBeChecked();
  expect(screen.getByLabelText('Factor lower · RICE-DRYING')).toHaveValue('0.40');
  expect(screen.getByLabelText('Factor upper · RICE-DRYING')).toHaveValue('0.70');
  expect(screen.getByLabelText(/Explicitly adopt this complete company override/)).not.toBeChecked();
  await user.click(screen.getByLabelText('Factor range · RICE-DRYING'));
  const factor = screen.getByLabelText('Concentration factor · RICE-DRYING');
  await user.clear(factor); await user.type(factor, '0.55');
  await user.click(screen.getByLabelText(/Explicitly adopt this complete company override/));
  await completeReview(user); await user.click(screen.getByRole('button', { name: 'Publish changed matrix rules' }));
  await waitFor(() => expect(onPublished).toHaveBeenCalledOnce());
  const body = JSON.parse(String(fetchMock.mock.calls.find(([url]) => String(url) === '/api/residue-matrix/publish')?.[1]?.body));
  expect(body.rules[0]).toMatchObject({ baseId: null, sourceId: process.id, rule: { companyId, factorLower: '0.55', factorUpper: '0.55', factorSemantics: 'STEP_CONCENTRATION' } });
  expect(body.settings).toEqual([{ ruleCode: process.code, baseId: null, mode: 'COMPANY_OVERRIDE', evidenceRef: process.evidenceRef, applicabilityStatement: process.applicabilityStatement }]);
});
it('preserves stale draft without silently rebasing and clears scientific review on further edits', async () => {
  const user = userEvent.setup(); failure = 409;
  render(<ResidueMatrix rules={[rule]} substances={substances} commodities={[]} companyId={null} settings={[]} onPublished={onPublished} />);
  await user.click(screen.getByRole('button', { name: 'Edit system policy · NUMERIC-RICE' }));
  const uncertainty = screen.getByLabelText('Relative uncertainty (%) · NUMERIC-RICE');
  await user.clear(uncertainty); await user.type(uncertainty, '15'); await completeReview(user);
  await user.type(uncertainty, '0');
  expect(screen.getByLabelText('Matrix scientific reviewer')).toHaveValue('');
  expect(screen.getByLabelText('Matrix reviewed at')).toHaveValue('');
  await completeReview(user); await user.click(screen.getByRole('button', { name: 'Publish changed matrix rules' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Draft preserved');
  expect(uncertainty).toHaveValue('150'); expect(uncertainty).toBeDisabled();
  expect(onPublished).not.toHaveBeenCalled();
  await user.click(screen.getByRole('button', { name: 'Discard matrix draft and load current rules' }));
  expect(onPublished).toHaveBeenCalledOnce();
});
it('exposes overlapping applicability rather than flattening profiles', () => {
  const conflict = { ...rule, id: '55555555-5555-4555-8555-555555555555', code: 'OTHER-NUMERIC-RICE' };
  render(<ResidueMatrix rules={[rule, conflict]} substances={substances} commodities={[]} companyId={null} settings={[]} onPublished={onPublished} />);
  expect(screen.getAllByText(/Conflicting applicability/)).toHaveLength(2);
  expect(screen.getByRole('region', { name: 'Scrollable substance policy matrix' })).toHaveTextContent('OTHER-NUMERIC-RICE');
});

it.each([false, true])('publishes explicit unchanged-profile adoption independently of scientific changes (changed=%s)', async scienceChanged => {
  const user = userEvent.setup();
  const ownNumeric = { ...rule, companyId };
  const ownProcess = { ...process, companyId };
  render(<ResidueMatrix rules={[ownNumeric, ownProcess]} substances={substances} commodities={[]} companyId={companyId}
    settings={[{ id: 'prior-adoption', ruleCode: process.code, mode: 'SYSTEM_DEFAULT', supersedesId: null, effectiveFrom: rule.effectiveFrom, effectiveTo: null }]} onPublished={onPublished} />);
  if (scienceChanged) {
    await user.click(screen.getByRole('button', { name: 'Edit company policy · NUMERIC-RICE' }));
    const uncertainty = screen.getByLabelText('Relative uncertainty (%) · NUMERIC-RICE');
    await user.clear(uncertainty); await user.type(uncertainty, '25');
  }
  await user.click(screen.getByRole('button', { name: 'Edit company policy · RICE-DRYING' }));
  await user.click(screen.getByLabelText('Explicitly adopt this complete company override in same publication · RICE-DRYING'));
  expect(screen.getByText(`One completed review · ${scienceChanged ? 1 : 0} changed complete rules · 1 explicit adoption settings`)).toBeInTheDocument();
  await completeReview(user);
  await user.click(screen.getByRole('button', { name: scienceChanged ? 'Publish changed matrix rules' : 'Publish matrix adoption settings' }));
  await waitFor(() => expect(onPublished).toHaveBeenCalledOnce());
  const body = JSON.parse(String(fetchMock.mock.calls.find(([url]) => String(url) === '/api/residue-matrix/publish')?.[1]?.body));
  expect(body.rules).toHaveLength(scienceChanged ? 2 : 1);
  expect(body.rules.find((entry: { sourceId: string }) => entry.sourceId === process.id))
    .toEqual({ baseId: process.id, sourceId: process.id, rule: ownProcess });
  expect(body.settings).toEqual([{ ruleCode: process.code, baseId: 'prior-adoption', mode: 'COMPANY_OVERRIDE',
    evidenceRef: process.evidenceRef, applicabilityStatement: process.applicabilityStatement }]);
  if (scienceChanged) {
    expect(body.rules.find((entry: { sourceId: string }) => entry.sourceId === rule.id).rule.relativeUncertaintyPercent).toBe('25');
  }
});
