import type { Rule } from './ResidueRuleMaintenance';

export const ruleSeriesKey = (rule: Rule) => JSON.stringify([rule.companyId || null, rule.code]);
export const inEffectiveWindow = (item: { effectiveFrom: string; effectiveTo: string | null }, now = Date.now()) => Date.parse(item.effectiveFrom) <= now && (!item.effectiveTo || now < Date.parse(item.effectiveTo));
export const effectiveWindowLabel = (item: { effectiveFrom: string; effectiveTo: string | null }) => inEffectiveWindow(item) ? 'Within effective window now' : Date.parse(item.effectiveFrom) > Date.now() ? 'Scheduled' : 'Outside effective window';
export function groupRuleSeries(rules: Rule[]) {
  const groups = new Map<string, Rule[]>();
  for (const rule of rules) {
    const key = ruleSeriesKey(rule);
    const group = groups.get(key);
    if (group) group.push(rule); else groups.set(key, [rule]);
  }
  return Array.from(groups, ([key, versions]) => {
    const predecessors = new Set(versions.map(rule => rule.supersedesId));
    const ordered = versions.sort((a, b) => b.versionNo - a.versionNo);
    return { key, versions: ordered, tips: ordered.filter(rule => !predecessors.has(rule.id)), history: ordered.filter(rule => predecessors.has(rule.id)) };
  });
}
// Exact candidates include policy values, not merely matching substance or applicability.
export function exactDuplicateCandidates(rules: Rule[]) {
  const key = (rule: Rule) => JSON.stringify([rule.companyId || null, rule.kind, rule.substanceId, rule.commodityId, rule.residueDefinition,
    rule.inputStage, rule.outputStage, rule.inputBasis, rule.outputBasis, rule.outputFraction, rule.process, rule.conditions, rule.analyticalMethod || null,
    rule.unit, rule.decimalPlaces, rule.interpretationKind, rule.ndThresholdKind, rule.factorLower, rule.factorUpper, rule.factorSemantics,
    rule.homogeneousMixing, rule.relativeUncertaintyPercent || null, !!rule.relativeUncertaintyAtZero]);
  const groups = new Map<string, Rule[]>();
  for (const rule of rules) {
    const identity = key(rule), group = groups.get(identity);
    if (group) group.push(rule); else groups.set(identity, [rule]);
  }
  const candidates: Array<[Rule, Rule]> = [];
  for (const group of groups.values()) for (let left = 0; left < group.length; left++) for (let right = left + 1; right < group.length; right++) {
    const a = group[left], b = group[right];
    if (a.code !== b.code && Date.parse(a.effectiveFrom) < (b.effectiveTo ? Date.parse(b.effectiveTo) : Infinity) && Date.parse(b.effectiveFrom) < (a.effectiveTo ? Date.parse(a.effectiveTo) : Infinity)) candidates.push([a, b]);
  }
  return candidates;
}
