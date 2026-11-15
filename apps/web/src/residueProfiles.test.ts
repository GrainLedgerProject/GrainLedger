import { expect, it } from 'vitest';
import { exactDuplicateCandidates, groupRuleSeries, inEffectiveWindow } from './residueProfiles';
import type { Rule } from './ResidueRuleMaintenance';

const rule: Rule = {
  id: 'original', code: 'RICE', versionNo: 1, companyId: null, kind: 'FLOW', schemaVersion: 2,
  substanceId: 'substance', commodityId: 'commodity', residueDefinition: 'parent', unit: 'mg/kg', decimalPlaces: 3,
  laboratory: [{ analyticalMethod: 'LC-MS/MS', numericInterpretationKind: 'NUMERIC_ABSOLUTE_UNCERTAINTY', relativeUncertaintyPercent: null, relativeUncertaintyAtZero: false, belowLimit: true, ndThresholdKind: 'LOD' }],
  steps: [{ id: 'step-one', process: 'DRYING', inputStage: 'RAW_PADDY', outputStage: 'DRIED_PADDY', outputFraction: 'DRIED_PADDY', conditions: 'UNCONDITIONAL', factorLower: '1', factorUpper: '1', factorSemantics: 'STEP_CONCENTRATION', anchorStage: null, homogeneousMixing: false }],
  asReceivedConfirmed: true, effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: null, supersedesId: null,
  evidenceRef: 'study', applicabilityStatement: 'Reviewed exact applicability', reviewedBy: 'Scientist', reviewAuthority: 'QA', reviewedAt: '2026-01-01T00:00:00Z', publishedBy: 'publisher', publishedAt: '2026-01-01T00:00:00Z',
};

it('groups scoped series and keeps conflicting tips separate from linked history', () => {
  const groups = groupRuleSeries([rule, { ...rule, id: 'head', versionNo: 2, supersedesId: rule.id }, { ...rule, id: 'conflict', versionNo: 3 },
    { ...rule, id: 'company-one', companyId: 'one' }, { ...rule, id: 'company-two', companyId: 'two' }]);
  expect(groups).toHaveLength(3);
  expect(groups[0].tips.map(item => item.id)).toEqual(['conflict', 'head']);
  expect(groups[0].history.map(item => item.id)).toEqual(['original']);
});

it('compares complete profile values but ignores step identity when finding overlapping duplicates', () => {
  const duplicate = { ...rule, id: 'candidate', code: 'SAME', steps: [{ ...rule.steps[0], id: 'another-id' }] };
  expect(exactDuplicateCandidates([rule, duplicate])).toHaveLength(1);
  expect(exactDuplicateCandidates([rule, { ...duplicate, steps: [{ ...duplicate.steps[0], factorUpper: '2' }] }])).toEqual([]);
  expect(exactDuplicateCandidates([rule, { ...duplicate, laboratory: [{ ...rule.laboratory[0], analyticalMethod: 'OTHER' }] }])).toEqual([]);
  expect(exactDuplicateCandidates([rule, { ...duplicate, companyId: 'one' }])).toEqual([]);
  expect(exactDuplicateCandidates([rule, { ...duplicate, effectiveFrom: '2027-01-01T00:00:00Z' }].map(item => item.id === rule.id ? { ...item, effectiveTo: '2027-01-01T00:00:00Z' } : item))).toEqual([]);
  expect(exactDuplicateCandidates([rule, { ...rule, id: 'next', versionNo: 2, supersedesId: rule.id }])).toEqual([]);
});

it('uses inclusive effective start and exclusive effective end', () => {
  const now = Date.parse('2026-10-23T00:00:00Z');
  expect(inEffectiveWindow({ effectiveFrom: '2026-10-23T00:00:00Z', effectiveTo: null }, now)).toBe(true);
  expect(inEffectiveWindow({ effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: '2026-10-23T00:00:00Z' }, now)).toBe(false);
  expect(inEffectiveWindow({ effectiveFrom: '2099-01-01T00:00:00Z', effectiveTo: null }, now)).toBe(false);
});
