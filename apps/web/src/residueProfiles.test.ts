import { expect, it } from 'vitest';
import { exactDuplicateCandidates, groupRuleSeries, inEffectiveWindow } from './residueProfiles';
import type { Rule } from './ResidueRuleMaintenance';

const rule: Rule = {
  id: 'original', code: 'RICE', versionNo: 1, companyId: null, kind: 'PROCESS', substanceId: 'substance', commodityId: 'commodity', residueDefinition: 'parent',
  inputStage: 'RAW_PADDY', outputStage: 'DRIED_PADDY', inputBasis: 'WHOLE', outputBasis: 'WHOLE', outputFraction: 'DRIED_PADDY', process: 'DRYING', conditions: 'UNCONDITIONAL',
  analyticalMethod: null, unit: 'mg/kg', decimalPlaces: 3, interpretationKind: null, ndThresholdKind: null, factorLower: '1', factorUpper: '1', factorSemantics: 'STEP_CONCENTRATION', homogeneousMixing: false,
  effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: null, supersedesId: null, evidenceRef: 'study', applicabilityStatement: 'Reviewed exact applicability', reviewedBy: 'Scientist', reviewAuthority: 'QA', reviewedAt: '2026-01-01T00:00:00Z', publishedBy: 'publisher', publishedAt: '2026-01-01T00:00:00Z',
};
it('keeps company scope and every conflicting tip while separating linked history', () => {
  const groups = groupRuleSeries([rule, { ...rule, id: 'head', versionNo: 2, supersedesId: rule.id }, { ...rule, id: 'conflict', versionNo: 3 },
    { ...rule, id: 'company-one', companyId: 'one' }, { ...rule, id: 'company-two', companyId: 'two' }]);
  expect(groups).toHaveLength(3);
  expect(groups[0].tips.map(item => item.id)).toEqual(['conflict', 'head']);
  expect(groups[0].history.map(item => item.id)).toEqual(['original']);
});
it('does not call history, company overrides or differing applicability exact duplicate candidates', () => {
  const versions = [rule, { ...rule, id: 'next', versionNo: 2, supersedesId: rule.id }, { ...rule, id: 'override', companyId: 'one' },
    { ...rule, id: 'different-product', code: 'PRODUCT', outputFraction: 'BRAN' }, { ...rule, id: 'different-value', code: 'VALUE', factorUpper: '2' }];
  expect(exactDuplicateCandidates(versions)).toEqual([]);
  expect(exactDuplicateCandidates([rule, { ...rule, id: 'candidate', code: 'SAME' }])).toHaveLength(1);
  expect(exactDuplicateCandidates([rule, { ...rule, id: 'later', code: 'LATER', effectiveFrom: '2027-01-01T00:00:00Z' }].map(item => item.id === rule.id ? { ...item, effectiveTo: '2027-01-01T00:00:00Z' } : item))).toEqual([]);
});
it('uses inclusive effective start and exclusive effective end, separate from publication order', () => {
  const now = Date.parse('2026-10-23T00:00:00Z');
  expect(inEffectiveWindow({ effectiveFrom: '2026-10-23T00:00:00Z', effectiveTo: null }, now)).toBe(true);
  expect(inEffectiveWindow({ effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: '2026-10-23T00:00:00Z' }, now)).toBe(false);
  expect(inEffectiveWindow({ effectiveFrom: '2099-01-01T00:00:00Z', effectiveTo: null }, now)).toBe(false);
});
