import { Fragment, useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { Feedback, kinds, stages, useAction } from './ledger-ui';
import { ResidueDialog } from './ResidueDialog';
import { effectiveWindowLabel, groupRuleSeries } from './residueProfiles';

export type LaboratorySettings = { analyticalMethod: string; numericInterpretationKind: string | null; relativeUncertaintyPercent: string | null; relativeUncertaintyAtZero: boolean; belowLimit: boolean; ndThresholdKind: string | null };
export type ProcessStep = { id: string; process: string; inputStage: string; outputStage: string; outputFraction: string; conditions: string; factorLower: string; factorUpper: string; factorSemantics: string; anchorStage: string | null; homogeneousMixing: boolean };
export type Rule = {
  id: string; code: string; versionNo: number; companyId?: string | null; kind: 'FLOW'; schemaVersion: 2;
  substanceId: string; commodityId: string; residueDefinition: string; unit: 'mg/kg'; decimalPlaces: number;
  laboratory: LaboratorySettings[]; steps: ProcessStep[]; asReceivedConfirmed: boolean;
  effectiveFrom: string; effectiveTo: string | null; supersedesId: string | null; evidenceRef: string; applicabilityStatement: string;
  reviewedBy: string; reviewAuthority: string; reviewedAt: string; publishedBy: string; publishedAt: string;
  retirement?: { profileId: string; code: string; companyId: string | null; reason: string; retiredBy: string; retiredAt: string } | null;
};
type RuleForm = Omit<Rule, 'publishedBy' | 'publishedAt' | 'retirement' | 'decimalPlaces'> & { decimalPlaces: number | null };
type Item = { id: string; code: string; name: string };
type Setting = { id: string; ruleCode: string; mode: string; supersedesId: string | null; effectiveFrom: string; effectiveTo: string | null };
const blank = (companyId: string | null): RuleForm => {
  const id = crypto.randomUUID();
  return { id, code: `RES-${id}`, versionNo: 1, companyId, kind: 'FLOW', schemaVersion: 2, substanceId: '', commodityId: '', residueDefinition: '', unit: 'mg/kg', decimalPlaces: null, laboratory: [], steps: [], asReceivedConfirmed: false, effectiveFrom: '', effectiveTo: null, supersedesId: null, evidenceRef: '', applicabilityStatement: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' };
};
const newLab = (): LaboratorySettings => ({ analyticalMethod: '', numericInterpretationKind: null, relativeUncertaintyPercent: null, relativeUncertaintyAtZero: false, belowLimit: false, ndThresholdKind: null });
const newStep = (): ProcessStep => ({ id: crypto.randomUUID(), process: '', inputStage: '', outputStage: '', outputFraction: '', conditions: '', factorLower: '', factorUpper: '', factorSemantics: 'STEP_CONCENTRATION', anchorStage: null, homogeneousMixing: false });
const operations = [...kinds, 'CUSTODY_INGRESS', 'CUSTODY_WITHDRAW'];
const materials = [...['RAW_PADDY', 'DRIED_PADDY', 'HEAD_RICE', 'BROKEN_RICE', 'BRAN', 'HUSK', 'BLENDED_RICE'].map(stage => ({ stage, product: stage })), ...['HEAD_RICE', 'BROKEN_RICE', 'BLENDED_RICE'].map(product => ({ stage: 'BAGGED_RICE', product }))];
type ProcessPath = Pick<ProcessStep, 'inputStage' | 'outputStage' | 'outputFraction'>;
function processPaths(operation: string): ProcessPath[] {
  switch (operation) {
    case 'DRYING': return [{ inputStage: 'RAW_PADDY', outputStage: 'DRIED_PADDY', outputFraction: 'DRIED_PADDY' }];
    case 'MILLING': return ['HEAD_RICE', 'BROKEN_RICE', 'BRAN', 'HUSK'].map(product => ({ inputStage: 'DRIED_PADDY', outputStage: product, outputFraction: product }));
    case 'BLENDING': return ['HEAD_RICE', 'BROKEN_RICE', 'BLENDED_RICE'].map(stage => ({ inputStage: stage, outputStage: 'BLENDED_RICE', outputFraction: 'BLENDED_RICE' }));
    case 'BAGGING': return ['HEAD_RICE', 'BROKEN_RICE', 'BLENDED_RICE'].map(stage => ({ inputStage: stage, outputStage: 'BAGGED_RICE', outputFraction: stage }));
    case 'MERGE': case 'CUSTODY_INGRESS': case 'CUSTODY_WITHDRAW': return materials.map(material => ({ inputStage: material.stage, outputStage: material.stage, outputFraction: material.product }));
    default: return [];
  }
}
const pathKey = (path: ProcessPath) => `${path.inputStage}:${path.outputStage}:${path.outputFraction}`;
const decimal = (value: string | null) => value !== null && /^[0-9]{1,24}(\.[0-9]{1,18})?$/.test(value);
const dateValue = (value: string) => value ? new Date(value).toISOString() : '';
const localDate = (value: string) => value ? value.slice(0, 16) : '';

const display = (value: string | number | null | undefined) => value === null || value === undefined || value === '' ? 'Not specified' : String(value);
const readable = (value: string | null | undefined) => value ? value.replaceAll('_', ' ').toLowerCase() : 'Not specified';

function ProfileReadout({ rule, substances, commodities, ranges, published = false }: { rule: RuleForm | Rule; substances: Item[]; commodities: Item[]; ranges?: Record<string, boolean>; published?: boolean }) {
  const itemName = (items: Item[], id: string) => {
    const item = items.find(candidate => candidate.id === id);
    return item ? `${item.code} · ${item.name}` : display(id);
  };
  return <section className={`residue-readout${published ? ' residue-readout-published' : ''}`} aria-label={published ? 'Published profile details' : 'Live profile preview'}>
    <h3>{published ? 'Published profile details' : 'Live profile preview'}</h3>
    {!published && <p>Current configuration only. This preview does not calculate results or determine compliance.</p>}
    <div className="residue-readout-section"><h4>Profile and applicability</h4><dl>
      <div><dt>Profile</dt><dd>{rule.code} v{rule.versionNo} · {rule.companyId ? 'Company' : 'System'}</dd></div>
      <div><dt>Supersedes profile ID</dt><dd>{rule.supersedesId || 'None'}</dd></div>
      <div><dt>Substance</dt><dd>{itemName(substances, rule.substanceId)}</dd></div>
      <div><dt>Commodity</dt><dd>{itemName(commodities, rule.commodityId)}</dd></div>
      <div><dt>Residue definition</dt><dd>{display(rule.residueDefinition)}</dd></div>
      <div><dt>Concentration basis</dt><dd>{rule.unit}, as received at each physical stage · {rule.asReceivedConfirmed ? 'confirmed' : 'not confirmed'}</dd></div>
      <div><dt>Outward precision</dt><dd>{rule.decimalPlaces === null ? 'Not specified' : `${rule.decimalPlaces} decimal places`}</dd></div>
      <div><dt>Applicability statement</dt><dd>{display(rule.applicabilityStatement)}</dd></div>
      <div><dt>Evidence reference</dt><dd>{display(rule.evidenceRef)}</dd></div>
    </dl></div>
    <div className="residue-readout-section"><h4>Laboratory results</h4><p>Source stage: RAW_PADDY. Coverage applies only to each exact analytical method and its configured result forms.</p>
      {!rule.laboratory.length ? <p>No laboratory methods configured.</p> : <ol>{rule.laboratory.map((row, index) => <li key={index}><strong>{row.analyticalMethod || 'Method not specified'}</strong><dl>
        <div><dt>Numeric results</dt><dd>{row.numericInterpretationKind === 'NUMERIC_ABSOLUTE_UNCERTAINTY' ? 'Absolute uncertainty' : row.numericInterpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' ? 'Relative uncertainty' : row.numericInterpretationKind ? readable(row.numericInterpretationKind) : 'Not supported'}</dd></div>
        {row.numericInterpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' && <><div><dt>Relative uncertainty</dt><dd>{row.relativeUncertaintyPercent ? `${row.relativeUncertaintyPercent}%` : 'Not specified'}</dd></div><div><dt>Relative uncertainty at numeric zero</dt><dd>{row.relativeUncertaintyAtZero ? 'Reviewed' : 'Not reviewed'}</dd></div></>}
        <div><dt>Below-limit results</dt><dd>{row.belowLimit ? 'Supported (reported below a limit)' : 'Not supported'}</dd></div>
        <div><dt>ND threshold</dt><dd>{row.ndThresholdKind === 'LOD' ? 'Limit of detection (LOD)' : row.ndThresholdKind === 'LOQ' ? 'Limit of quantification (LOQ)' : row.ndThresholdKind === 'REPORTING_THRESHOLD' ? 'Reporting threshold' : row.ndThresholdKind ? readable(row.ndThresholdKind) : 'Not supported'}</dd></div>
      </dl></li>)}</ol>}
    </div>
    <div className="residue-readout-section"><h4>Processing steps</h4><p>Only the configured operation, material path and conditions are covered; missing downstream coverage remains unresolved.</p>
      {!rule.steps.length ? <p>No processing steps configured. Downstream coverage remains unresolved.</p> : <ol>{rule.steps.map(row => <li key={row.id}><strong>{row.process ? readable(row.process) : 'Operation not specified'}</strong><dl>
        <div><dt>Input stage</dt><dd>{display(row.inputStage)}</dd></div><div><dt>Output stage</dt><dd>{display(row.outputStage)}</dd></div><div><dt>Output fraction</dt><dd>{display(row.outputFraction)}</dd></div>
        <div><dt>Conditions</dt><dd>{display(row.conditions)}</dd></div>
        <div><dt>Concentration factor</dt><dd>{display(row.factorLower)}{(ranges ? ranges[row.id] : row.factorLower !== row.factorUpper) && ` to ${display(row.factorUpper)}`}</dd></div>
        <div><dt>Factor semantics</dt><dd>{readable(row.factorSemantics)}</dd></div>
        {row.factorSemantics === 'CUMULATIVE_CONCENTRATION' && <div><dt>Cumulative ancestor stage</dt><dd>{display(row.anchorStage)}</dd></div>}
        <div><dt>Homogeneous mixing applicability</dt><dd>{row.homogeneousMixing ? 'Reviewed' : 'Not reviewed'}</dd></div>
      </dl></li>)}</ol>}
    </div>
    <div className="residue-readout-section"><h4>Review and publication</h4><dl>
      <div><dt>Effective from</dt><dd>{display(rule.effectiveFrom)}</dd></div><div><dt>Effective until</dt><dd>{rule.effectiveTo || 'No stated end'}</dd></div>
      <div><dt>Scientific reviewer</dt><dd>{display(rule.reviewedBy)}</dd></div><div><dt>Review authority</dt><dd>{display(rule.reviewAuthority)}</dd></div><div><dt>Reviewed at</dt><dd>{display(rule.reviewedAt)}</dd></div>
      {'publishedBy' in rule && <><div><dt>Published by</dt><dd>{display(rule.publishedBy)}</dd></div><div><dt>Published at</dt><dd>{display(rule.publishedAt)}</dd></div></>}
    </dl></div>
  </section>;
}

export function ResidueRuleMaintenance({ active, allowed, companyId = null, onPublished }: { active: boolean; allowed: boolean; companyId?: string | null; onPublished?: () => void; policySettings?: Setting[] }) {
  const [rules, setRules] = useState<Rule[]>([]), [substances, setSubstances] = useState<Item[]>([]), [commodities, setCommodities] = useState<Item[]>([]);
  const [form, setForm] = useState<RuleForm>(() => blank(companyId)), [source, setSource] = useState<Rule | null>(null), [editorOpen, setEditorOpen] = useState(false);
  const [ranges, setRanges] = useState<Record<string, boolean>>({}), [conditionModes, setConditionModes] = useState<Record<string, boolean>>({}), [confirmed, setConfirmed] = useState(false);
  const [error, setError] = useState(''), [loading, setLoading] = useState(false), [reload, setReload] = useState(0), [loadedScope, setLoadedScope] = useState<string | null | undefined>();
  const [search, setSearch] = useState(''), [operation, setOperation] = useState(''), [scope, setScope] = useState(''), [status, setStatus] = useState(''), [sort, setSort] = useState('code'), [reuseId, setReuseId] = useState('');
  const [method, setMethod] = useState('');
  const [historyOpen, setHistoryOpen] = useState<Record<string, boolean>>({}), [expanded, setExpanded] = useState<string | null>(null);
  const [retiring, setRetiring] = useState<Rule | null>(null), [retirementReason, setRetirementReason] = useState(''), [retirementConfirmed, setRetirementConfirmed] = useState(false);
  const action = useAction();
  useEffect(() => { setForm(blank(companyId)); setSource(null); setEditorOpen(false); setConfirmed(false); setReuseId(''); setRetiring(null); setRetirementReason(''); setRetirementConfirmed(false); }, [companyId]);
  useEffect(() => {
    if (!active || !allowed) return;
    let current = true; setLoading(true); setError('');
    void Promise.all([api<{ items: Rule[] }>('/api/residue-rules'), loadAll<Item>('/api/active-substances'), loadAll<Item>('/api/commodities')])
      .then(([page, substances, commodities]) => { if (current) { setRules(page.items); setSubstances(substances); setCommodities(commodities); setLoadedScope(companyId); } })
      .catch(cause => { if (current) setError(errorMessage(cause)); }).finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [active, allowed, companyId, reload]);
  function set<K extends keyof RuleForm>(key: K, value: RuleForm[K]) { setConfirmed(false); setForm(current => ({ ...current, [key]: value })); }
  function lab(index: number, patch: Partial<LaboratorySettings>) { setConfirmed(false); setForm(current => ({ ...current, laboratory: current.laboratory.map((row, i) => i === index ? { ...row, ...patch } : row) })); }
  function step(id: string, patch: Partial<ProcessStep>) {
    setConfirmed(false);
    const row = form.steps.find(item => item.id === id);
    const changed = row && !!row.inputStage && (patch.process !== undefined && patch.process !== row.process ||
      patch.inputStage !== undefined && (patch.inputStage !== row.inputStage || patch.outputStage !== row.outputStage || patch.outputFraction !== row.outputFraction) ||
      patch.conditions !== undefined && patch.conditions !== row.conditions);
    const nextId = changed ? crypto.randomUUID() : id;
    if (changed) {
      setRanges(current => ({ ...current, [nextId]: current[id] }));
      setConditionModes(current => ({ ...current, [nextId]: current[id] }));
    }
    setForm(current => ({ ...current, steps: current.steps.map(item => item.id === id ? { ...item, ...patch, id: nextId } : item) }));
  }
  function closeEditor() { action.setError(''); action.setStatus(''); setSource(null); setForm(blank(companyId)); setConfirmed(false); setEditorOpen(false); }
  function start(rule: Rule, clone: boolean) {
    action.setError(''); action.setStatus(''); setSource(rule); setConfirmed(false);
    setRanges(Object.fromEntries(rule.steps.map(row => [row.id, row.factorLower !== row.factorUpper])));
    setConditionModes(Object.fromEntries(rule.steps.map(row => [row.id, row.conditions !== 'UNCONDITIONAL'])));
    const { publishedBy: _publishedBy, publishedAt: _publishedAt, retirement: _retirement, ...rest } = rule;
    setForm({ ...rest, id: crypto.randomUUID(), companyId, versionNo: clone ? 1 : Math.max(...rules.filter(item => (item.companyId || null) === companyId && item.code === rule.code).map(item => item.versionNo)) + 1, supersedesId: clone ? null : rule.id, effectiveFrom: '', effectiveTo: null, reviewedBy: '', reviewAuthority: '', reviewedAt: '', asReceivedConfirmed: false });
    setEditorOpen(true);
  }
  async function publish(event: FormEvent) {
    event.preventDefault(); if (!allowed || loading || error || loadedScope !== companyId) return;
    await action.run(async () => {
      if (!confirmed || !form.asReceivedConfirmed) throw new Error('Confirm the complete review and as-received convention before publishing.');
      if (form.decimalPlaces === null || !Number.isInteger(form.decimalPlaces) || form.decimalPlaces < 0 || form.decimalPlaces > 18) throw new Error('Set reviewed outward precision between 0 and 18 decimal places.');
      if (!form.laboratory.length && !form.steps.length) throw new Error('Add at least one laboratory method or processing step.');
      if (form.laboratory.some(row => !row.analyticalMethod.trim() || (!row.numericInterpretationKind && !row.belowLimit && !row.ndThresholdKind) || (row.numericInterpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' && !decimal(row.relativeUncertaintyPercent)))) throw new Error('Complete each laboratory method and at least one supported result form.');
      if (new Set(form.laboratory.map(row => row.analyticalMethod)).size !== form.laboratory.length) throw new Error('Each analytical method must be unique.');
      if (form.steps.some(row => !processPaths(row.process).some(path => pathKey(path) === pathKey(row)) || !row.conditions.trim() || !decimal(row.factorLower) || (ranges[row.id] && !decimal(row.factorUpper)) || (row.factorSemantics === 'CUMULATIVE_CONCENTRATION' && !row.anchorStage))) throw new Error('Complete each supported process path, condition, factor and cumulative anchor.');
      const steps = form.steps.map(row => ({ ...row, factorUpper: ranges[row.id] ? row.factorUpper : row.factorLower, anchorStage: row.factorSemantics === 'STEP_CONCENTRATION' ? null : row.anchorStage }));
      const body = { ...form, laboratory: form.laboratory.map(row => ({ ...row, relativeUncertaintyPercent: row.numericInterpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' ? row.relativeUncertaintyPercent : null, relativeUncertaintyAtZero: row.numericInterpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' && row.relativeUncertaintyAtZero })), steps, effectiveFrom: dateValue(form.effectiveFrom), effectiveTo: form.effectiveTo ? dateValue(form.effectiveTo) : null, reviewedAt: dateValue(form.reviewedAt) };
      try { await api('/api/residue-rules', { method: 'POST', body: JSON.stringify(body) }); }
      catch (cause) {
        if (cause instanceof ApiError && cause.status === 409) {
          try { setRules((await api<{ items: Rule[] }>('/api/residue-rules')).items); } catch (refreshCause) { throw new Error(`Published profile history changed. Draft preserved; history refresh failed: ${errorMessage(refreshCause)}`); }
          throw new Error('Published profile history changed. Draft preserved. Cancel when ready, review latest history and start a fresh revision.');
        }
        throw cause;
      }
      setForm(blank(companyId)); setSource(null); setConfirmed(false); setEditorOpen(false); onPublished?.();
      try { setRules((await api<{ items: Rule[] }>('/api/residue-rules')).items); }
      catch (cause) { setError(`Profile was published, but the profile list could not be refreshed: ${errorMessage(cause)}`); }
    }, 'Reviewed residue profile published.');
  }
  async function retire(event: FormEvent) {
    event.preventDefault();
    if (!retiring || !allowed || loading || error || loadedScope !== companyId) return;
    await action.run(async () => {
      const reason = retirementReason.trim();
      if (!reason || reason.length > 1000 || !retirementConfirmed) throw new Error('Enter a reason of up to 1000 characters and confirm retirement.');
      let retirement: NonNullable<Rule['retirement']>;
      try { retirement = await api<NonNullable<Rule['retirement']>>(`/api/residue-rules/${retiring.id}/retire`, { method: 'POST', body: JSON.stringify({ reason }) }); }
      catch (cause) {
        if (cause instanceof ApiError && cause.status === 409) {
          try { setRules((await api<{ items: Rule[] }>('/api/residue-rules')).items); onPublished?.(); }
          catch (refreshCause) { throw new Error(`Profile history changed; refresh failed: ${errorMessage(refreshCause)}`); }
          throw new Error('Profile history changed. Review the latest status before trying again.');
        }
        throw cause;
      }
      setRules(current => current.map(rule => rule.code === retirement.code && (rule.companyId || null) === (retirement.companyId || null) ? { ...rule, retirement } : rule));
      setReuseId(''); setRetiring(null); setRetirementReason(''); setRetirementConfirmed(false); onPublished?.();
    }, 'Profile series retired.');
  }
  const name = (items: Item[], id: string) => { const item = items.find(value => value.id === id); return item ? `${item.code} · ${item.name}` : id; };
  const profileLabel = (rule: Rule) => `${substances.find(item => item.id === rule.substanceId)?.name || rule.substanceId} · ${commodities.find(item => item.id === rule.commodityId)?.name || rule.commodityId} · ${rule.residueDefinition}`;
  const visible = rules.filter(rule => (rule.companyId || null) === companyId || (companyId && !rule.companyId));
  const methods = Array.from(new Set(visible.flatMap(rule => rule.laboratory.map(row => row.analyticalMethod)).filter(value => value.trim()))).sort();
  const groups = groupRuleSeries(visible), successors = new Set(visible.map(rule => rule.supersedesId).filter(Boolean)), companyCodes = new Set(visible.filter(rule => rule.companyId === companyId).map(rule => rule.code));
  const reusable = groups.flatMap(group => group.tips.length === 1 ? group.tips : []).filter(rule => !rule.retirement && (!companyId || rule.companyId || !companyCodes.has(rule.code)));
  const matches = (rule: Rule) => (!method || rule.laboratory.some(row => row.analyticalMethod === method)) && (!operation || rule.steps.some(row => row.process === operation)) && (!scope || (scope === 'company' ? !!rule.companyId : !rule.companyId)) && (!status || (status === 'conflict' ? groups.some(group => group.tips.length > 1 && group.tips.some(tip => tip.id === rule.id)) : status === 'Retired' ? !!rule.retirement : !rule.retirement && effectiveWindowLabel(rule) === status)) && (!search.trim() || `${JSON.stringify(rule)} ${name(substances, rule.substanceId)} ${name(commodities, rule.commodityId)}`.toLowerCase().includes(search.trim().toLowerCase()));
  const filtered = groups.filter(group => group.versions.some(matches)).sort((a, b) => sort === 'substance' ? name(substances, a.tips[0].substanceId).localeCompare(name(substances, b.tips[0].substanceId)) : sort === 'published' ? Math.max(...b.tips.map(rule => Date.parse(rule.publishedAt))) - Math.max(...a.tips.map(rule => Date.parse(rule.publishedAt))) : a.tips[0].code.localeCompare(b.tips[0].code));
  const issues: { section: string; text: string }[] = [];
  if (!form.substanceId || !form.commodityId || !form.residueDefinition.trim()) issues.push({ section: 'profile', text: 'Complete substance, commodity and residue definition.' });
  if (!form.laboratory.length && !form.steps.length) issues.push({ section: 'laboratory', text: 'Add at least one laboratory method or processing step.' });
  form.laboratory.forEach((row, index) => {
    if (!row.analyticalMethod.trim()) issues.push({ section: 'laboratory', text: `Method ${index + 1}: enter an exact analytical method.` });
    if (!row.numericInterpretationKind && !row.belowLimit && !row.ndThresholdKind) issues.push({ section: 'laboratory', text: `Method ${index + 1}: select at least one supported result form.` });
    if (row.numericInterpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' && !decimal(row.relativeUncertaintyPercent)) issues.push({ section: 'laboratory', text: `Method ${index + 1}: enter a valid relative uncertainty.` });
  });
  if (new Set(form.laboratory.map(row => row.analyticalMethod)).size !== form.laboratory.length) issues.push({ section: 'laboratory', text: 'Each analytical method must be unique.' });
  form.steps.forEach((row, index) => {
    if (!processPaths(row.process).some(path => pathKey(path) === pathKey(row))) issues.push({ section: 'processing', text: `Step ${index + 1}: choose a supported operation and material path.` });
    if (!row.conditions.trim()) issues.push({ section: 'processing', text: `Step ${index + 1}: specify reviewed conditions in advanced step settings.` });
    if (!decimal(row.factorLower) || (ranges[row.id] && !decimal(row.factorUpper))) issues.push({ section: 'processing', text: `Step ${index + 1}: enter valid concentration factor values.` });
    if (row.factorSemantics === 'CUMULATIVE_CONCENTRATION' && !row.anchorStage) issues.push({ section: 'processing', text: `Step ${index + 1}: choose a cumulative ancestor stage in advanced step settings.` });
  });
  if (form.decimalPlaces === null || !Number.isInteger(form.decimalPlaces) || form.decimalPlaces < 0 || form.decimalPlaces > 18) issues.push({ section: 'precision', text: 'Set reviewed outward precision between 0 and 18 decimal places.' });
  if (!form.effectiveFrom || !form.evidenceRef.trim() || !form.applicabilityStatement.trim() || !form.reviewedBy.trim() || !form.reviewAuthority.trim() || !form.reviewedAt) issues.push({ section: 'review', text: 'Complete effective date, evidence, applicability and reviewer details.' });
  if (!confirmed || !form.asReceivedConfirmed) issues.push({ section: 'review', text: 'Confirm the complete review and as-received convention before publishing.' });
  return <section className="panel residue-rule-maintenance" aria-labelledby="residue-rules-title">
    <h2 id="residue-rules-title">{companyId ? 'Company residue profiles' : 'System residue profiles'}</h2>
    <p>One reviewed profile covers laboratory results and processing steps. Concentrations are mg/kg as received at each physical stage.</p>
    <button type="button" disabled={loading || !!error || !allowed || loadedScope !== companyId || action.busy} onClick={() => { closeEditor(); setRanges({}); setConditionModes({}); setEditorOpen(true); }}>Create profile</button>
    {loading && <p role="status">Loading published profiles…</p>}{error && <div className="notice error" role="alert">{error}<button type="button" className="secondary" onClick={() => setReload(value => value + 1)}>Retry loading profiles</button></div>}
    {!editorOpen && !retiring && <Feedback {...action} />}
    <section aria-label="Reuse a reviewed profile" hidden={editorOpen}><h3>Reuse a reviewed profile</h3><label>Reviewed profile<select value={reuseId} onChange={event => setReuseId(event.target.value)} disabled={loading || !!error || !allowed}><option value="">Choose a reviewed profile</option>{reusable.map(rule => <option key={rule.id} value={rule.id}>{profileLabel(rule)} · {rule.code} v{rule.versionNo} · {rule.companyId ? 'Company' : 'System'}</option>)}</select></label><button type="button" disabled={!reuseId || loading || !!error || !allowed} onClick={() => { const rule = reusable.find(item => item.id === reuseId); if (rule) start(rule, !!companyId && !rule.companyId); }}>Use reviewed profile</button></section>
    <div role="region" aria-label="Residue profile overview" hidden={editorOpen}>
      <p className="residue-counts">{groups.length} scoped series. Latest publication is not necessarily effective now; company adoption remains separate.</p>
      <div className="residue-toolbar"><label>Analytical method<select value={method} onChange={event => setMethod(event.target.value)}><option value="">All methods</option>{methods.map(value => <option key={value} value={value}>{value}</option>)}</select></label></div>
      <div className="residue-toolbar"><label>Search profiles and history<input type="search" value={search} onChange={event => setSearch(event.target.value)} /></label><label>Operation<select value={operation} onChange={event => setOperation(event.target.value)}><option value="">All operations</option>{Array.from(new Set(visible.flatMap(rule => rule.steps.map(row => row.process)))).sort().map(value => <option key={value}>{value}</option>)}</select></label><label>Scope<select value={scope} onChange={event => setScope(event.target.value)}><option value="">All scopes</option><option value="system">System</option><option value="company">Company</option></select></label><label>Status<select value={status} onChange={event => setStatus(event.target.value)}><option value="">All statuses</option>{['Within effective window now', 'Scheduled', 'Outside effective window', 'Retired'].map(value => <option key={value}>{value}</option>)}<option value="conflict">Conflicting published tips</option></select></label><label>Sort profiles<select value={sort} onChange={event => setSort(event.target.value)}><option value="code">Series code</option><option value="substance">Substance</option><option value="published">Recently published</option></select></label></div>
      {!loading && !error && !filtered.length && <p className="empty">No profiles match. Create a reviewed profile or change filters.</p>}
      {!loading && !error && !!filtered.length && <div className="table-scroll residue-rule-table-scroll" role="region" aria-label="Published residue profiles" tabIndex={0}><table className="residue-rule-table"><thead><tr><th>Profile</th><th>Applies to</th><th>Operations</th><th>Effective period</th><th>Status</th><th>Actions</th></tr></thead><tbody>{filtered.flatMap(group => {
        const historicalMatch = group.history.some(matches) && !group.tips.some(matches), open = !!historyOpen[group.key] || historicalMatch;
        return [...group.tips, ...(open ? group.history : [])].map((rule, index) => <Fragment key={rule.id}>{index === 0 && <tr className="residue-rule-history-heading"><td colSpan={6}><strong>{profileLabel(rule)} · {rule.companyId ? 'Company' : 'System'}</strong><span className="table-subtitle">{rule.code}</span>{group.tips.length > 1 && <span className="notice pending"> Conflicting published tips</span>}{!!group.history.length && <button type="button" className="secondary" disabled={historicalMatch} aria-expanded={open} onClick={() => setHistoryOpen(current => ({ ...current, [group.key]: !current[group.key] }))}>{open ? 'Collapse' : 'Show'} earlier versions · {group.history.length}</button>}</td></tr>}<tr><td><strong>{profileLabel(rule)}</strong><span className="table-subtitle">{rule.code}</span><span className="table-subtitle">v{rule.versionNo} · {rule.companyId ? 'Company' : 'System'}</span></td><td>{name(substances, rule.substanceId)}<span className="table-subtitle">{name(commodities, rule.commodityId)} · {rule.residueDefinition}</span></td><td>{Array.from(new Set(rule.steps.map(row => row.process))).join(', ') || 'Source only'}<span className="table-subtitle">{rule.laboratory.length} laboratory methods · {rule.steps.length} steps</span></td><td>{rule.effectiveFrom}<span className="table-subtitle">{rule.effectiveTo || 'No stated end'}</span></td><td>{rule.retirement ? 'Retired series' : successors.has(rule.id) ? 'Superseded history' : 'Latest published tip'}<span className="table-subtitle">{effectiveWindowLabel(rule)}</span></td><td className="admin-row-actions"><button type="button" className="secondary" aria-expanded={expanded === rule.id} aria-controls={`details-${rule.id}`} onClick={() => setExpanded(current => current === rule.id ? null : rule.id)}>Details</button>{companyId && !rule.companyId && !rule.retirement && !successors.has(rule.id) && !companyCodes.has(rule.code) && <button type="button" className="secondary" disabled={group.tips.length !== 1} onClick={() => start(rule, true)}>Clone as company profile</button>}{(rule.companyId || null) === companyId && !rule.retirement && <><button type="button" className="secondary" disabled={successors.has(rule.id) || group.tips.length !== 1 || action.busy} onClick={() => start(rule, false)}>Revise profile</button>{!successors.has(rule.id) && <button type="button" className="secondary" disabled={!allowed || loading || !!error || action.busy || group.tips.length !== 1} onClick={() => { action.setError(''); action.setStatus(''); setRetirementReason(''); setRetirementConfirmed(false); setRetiring(rule); }}>Retire profile</button>}</>}</td></tr><tr hidden={expanded !== rule.id}><td colSpan={6}><div id={`details-${rule.id}`} className="residue-rule-details-panel"><ProfileReadout rule={rule} substances={substances} commodities={commodities} published />{rule.retirement && <p>Series retired {rule.retirement.retiredAt} by {rule.retirement.retiredBy}. Reason: {rule.retirement.reason}</p>}</div></td></tr></Fragment>);
      })}</tbody></table></div>}
    </div>
    {retiring && <ResidueDialog title="Retire profile series" busy={action.busy} onClose={() => { setRetiring(null); action.setError(''); }}><Feedback {...action} /><form onSubmit={retire}><p>Retire {retiring.code} ({retiring.companyId ? 'company' : 'system'}) immediately. New evaluations cannot use this series or fall back to an older version. Published history and existing snapshots remain unchanged.</p><label>Reason for retirement<textarea required maxLength={1000} value={retirementReason} onChange={event => setRetirementReason(event.target.value)} /></label><label><input type="checkbox" required checked={retirementConfirmed} onChange={event => setRetirementConfirmed(event.target.checked)} /> I confirm retirement of this entire profile series.</label><div><button type="submit" disabled={action.busy || !retirementReason.trim() || !retirementConfirmed || !!rules.find(rule => rule.id === retiring.id)?.retirement}>Confirm retirement</button> <button type="button" className="secondary" disabled={action.busy} onClick={() => { setRetiring(null); action.setError(''); }}>Cancel</button></div></form></ResidueDialog>}
    {editorOpen && <ResidueDialog title={form.supersedesId ? 'Revise profile' : source ? 'Clone as company profile' : 'Create profile'} busy={action.busy} onClose={closeEditor}><Feedback {...action} /><form className="residue-profile-editor" onSubmit={publish} onInvalidCapture={event => { const details = (event.target as HTMLElement).closest('details'); if (details) details.open = true; }}><fieldset disabled={action.busy || loading || !!error || !allowed || loadedScope !== companyId}>
      {issues.length > 0 && <section className="residue-issues" aria-label="Setup issues"><h3>Setup issues</h3><p>Complete these items before publishing:</p><ul>{issues.map((issue, index) => <li key={`${issue.section}-${index}`}><a href={`#residue-setup-${issue.section}`} onClick={event => { event.preventDefault(); const target = document.getElementById(`residue-setup-${issue.section}`); target?.querySelectorAll('details').forEach(details => { details.open = true; }); target?.focus(); }}>{issue.text}</a></li>)}</ul></section>}
      <div id="residue-setup-profile" tabIndex={-1}>
      <fieldset><legend>Profile</legend><div className="ledger-form-grid"><label>Substance<select required disabled={!!source} value={form.substanceId} onChange={event => set('substanceId', event.target.value)}><option value="">Choose substance</option>{substances.map(item => <option key={item.id} value={item.id}>{item.code}: {item.name}</option>)}</select></label><label>Commodity<select required disabled={!!source} value={form.commodityId} onChange={event => set('commodityId', event.target.value)}><option value="">Choose commodity</option>{commodities.map(item => <option key={item.id} value={item.id}>{item.code}: {item.name}</option>)}</select></label><label>Residue definition<input required value={form.residueDefinition} onChange={event => set('residueDefinition', event.target.value)} /></label></div><p>Concentrations are mg/kg as received at each physical stage.</p></fieldset>
      </div><div id="residue-setup-laboratory" tabIndex={-1}>
      <fieldset><legend>Laboratory results</legend><p>Source stage: RAW_PADDY. Leave empty for reviewed source-group fallback only. Choose a previously used exact method or enter one; spelling and case matter.</p><datalist id="residue-method-suggestions">{methods.map(value => <option key={value} value={value} />)}</datalist>{form.laboratory.map((row, index) => <fieldset key={index}><legend>Laboratory method {index + 1}</legend><div className="ledger-form-grid"><label>Exact analytical method<input required list="residue-method-suggestions" value={row.analyticalMethod} onChange={event => lab(index, { analyticalMethod: event.target.value })} /></label><label>Numeric results<select value={row.numericInterpretationKind || ''} onChange={event => lab(index, { numericInterpretationKind: event.target.value || null })}><option value="">Not supported</option><option value="NUMERIC_ABSOLUTE_UNCERTAINTY">Absolute uncertainty</option><option value="NUMERIC_RELATIVE_UNCERTAINTY">Relative uncertainty</option></select></label>{row.numericInterpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' && <><label>Relative uncertainty (%)<input required inputMode="decimal" value={row.relativeUncertaintyPercent || ''} onChange={event => lab(index, { relativeUncertaintyPercent: event.target.value || null })} /></label><label className="checkbox-label"><input type="checkbox" checked={row.relativeUncertaintyAtZero} onChange={event => lab(index, { relativeUncertaintyAtZero: event.target.checked })} /> Reviewed relative uncertainty at numeric zero</label></>}<label className="checkbox-label"><input type="checkbox" checked={row.belowLimit} onChange={event => lab(index, { belowLimit: event.target.checked })} /> Support below-limit results</label><label>ND threshold<select value={row.ndThresholdKind || ''} onChange={event => lab(index, { ndThresholdKind: event.target.value || null })}><option value="">Not supported</option><option value="REPORTING_THRESHOLD">Reporting threshold</option><option value="LOD">Limit of detection (LOD)</option><option value="LOQ">Limit of quantification (LOQ)</option></select></label></div><button type="button" className="secondary" onClick={() => set('laboratory', form.laboratory.filter((_, i) => i !== index))}>Remove method {index + 1}</button></fieldset>)}<button type="button" className="secondary" onClick={() => set('laboratory', [...form.laboratory, newLab()])}>Add laboratory method</button></fieldset>
      </div><div id="residue-setup-processing" tabIndex={-1}>
      <fieldset><legend>Processing steps</legend><p>Optional. Missing downstream coverage remains unresolved. Enter reviewed concentration factors, not yields.</p>{form.steps.map((row, index) => <fieldset key={index}><legend>Processing step {index + 1}</legend><div className="ledger-form-grid"><label>Operation<select required value={row.process} onChange={event => { const process = event.target.value, paths = processPaths(process); step(row.id, { process, ...(paths.length === 1 ? paths[0] : { inputStage: '', outputStage: '', outputFraction: '' }) }); }}><option value="">Choose operation</option>{operations.map(value => <option key={value}>{value}</option>)}</select></label><label>Operation material path<select required value={pathKey(row)} onChange={event => { const path = processPaths(row.process).find(value => pathKey(value) === event.target.value); if (path) step(row.id, path); }}><option value="">Choose input and output</option>{processPaths(row.process).map(path => <option key={pathKey(path)} value={pathKey(path)}>{path.inputStage} → {path.outputStage} · {path.outputFraction}</option>)}</select></label><label className="checkbox-label"><input type="checkbox" checked={!!ranges[row.id]} onChange={event => { setConfirmed(false); setRanges(current => ({ ...current, [row.id]: event.target.checked })); }} /> Use factor range</label><label>{ranges[row.id] ? 'Factor lower bound' : 'Concentration factor'}<input required inputMode="decimal" value={row.factorLower} onChange={event => step(row.id, { factorLower: event.target.value })} /></label>{ranges[row.id] && <label>Factor upper bound<input required inputMode="decimal" value={row.factorUpper} onChange={event => step(row.id, { factorUpper: event.target.value })} /></label>}</div><details><summary>Advanced step settings</summary><div className="ledger-form-grid"><label>Factor semantics<select value={row.factorSemantics} onChange={event => step(row.id, { factorSemantics: event.target.value, anchorStage: null })}><option value="STEP_CONCENTRATION">Per-step concentration</option><option value="CUMULATIVE_CONCENTRATION">Cumulative concentration</option></select></label>{row.factorSemantics === 'CUMULATIVE_CONCENTRATION' && <label>Cumulative ancestor stage<select required value={row.anchorStage || ''} onChange={event => step(row.id, { anchorStage: event.target.value || null })}><option value="">Choose ancestor stage</option>{stages.map(value => <option key={value}>{value}</option>)}</select></label>}<label>Conditions<select required value={row.conditions === 'UNCONDITIONAL' ? 'UNCONDITIONAL' : conditionModes[row.id] ? 'CONDITIONAL' : ''} onChange={event => { setConditionModes(current => ({ ...current, [row.id]: event.target.value === 'CONDITIONAL' })); step(row.id, { conditions: event.target.value === 'UNCONDITIONAL' ? 'UNCONDITIONAL' : '' }); }}><option value="">Choose applicability</option><option value="UNCONDITIONAL">Unconditional</option><option value="CONDITIONAL">Specific conditions</option></select></label>{conditionModes[row.id] && <label>Exact reviewed conditions<input required value={row.conditions} onChange={event => step(row.id, { conditions: event.target.value })} /></label>}<label className="checkbox-label"><input type="checkbox" checked={row.homogeneousMixing} onChange={event => step(row.id, { homogeneousMixing: event.target.checked })} /> Reviewed homogeneous mixing applicability</label></div></details><button type="button" className="secondary" onClick={() => set('steps', form.steps.filter(item => item.id !== row.id))}>Remove step {index + 1}</button></fieldset>)}<button type="button" className="secondary" onClick={() => set('steps', [...form.steps, newStep()])}>Add processing step</button></fieldset>
      </div><div id="residue-setup-precision" tabIndex={-1}>
      <details><summary>Advanced profile settings</summary><label>Reviewed outward precision (decimal places)<input required type="number" min="0" max="18" step="1" value={form.decimalPlaces ?? ''} onChange={event => set('decimalPlaces', event.target.value === '' ? null : Number(event.target.value))} /></label></details>
      </div>
      <ProfileReadout rule={form} substances={substances} commodities={commodities} ranges={ranges} />
      <div id="residue-setup-review" tabIndex={-1}>
      <fieldset><legend>Review &amp; publish</legend><div className="ledger-form-grid"><label>Effective from<input required type="datetime-local" value={localDate(form.effectiveFrom)} onChange={event => set('effectiveFrom', event.target.value)} /></label><label>Effective until (optional)<input type="datetime-local" value={localDate(form.effectiveTo || '')} onChange={event => set('effectiveTo', event.target.value || null)} /></label><label>Evidence reference<input required value={form.evidenceRef} onChange={event => set('evidenceRef', event.target.value)} /></label><label>Applicability statement<textarea required value={form.applicabilityStatement} onChange={event => set('applicabilityStatement', event.target.value)} /></label><label>Scientific reviewer<input required value={form.reviewedBy} onChange={event => set('reviewedBy', event.target.value)} /></label><label>Review authority<input required value={form.reviewAuthority} onChange={event => set('reviewAuthority', event.target.value)} /></label><label>Reviewed at<input required type="datetime-local" value={localDate(form.reviewedAt)} onChange={event => set('reviewedAt', event.target.value)} /></label></div><label className="checkbox-label"><input required type="checkbox" checked={form.asReceivedConfirmed} onChange={event => set('asReceivedConfirmed', event.target.checked)} /> I confirm concentrations are mg/kg as received at each physical stage.</label><label className="checkbox-label"><input required type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} /> I reviewed the complete scientific profile, evidence and effective period.</label><button disabled={action.busy}>{action.busy ? 'Publishing…' : 'Publish reviewed profile'}</button> <button type="button" className="secondary" onClick={closeEditor}>Cancel profile draft</button></fieldset>
      </div>
    </fieldset></form></ResidueDialog>}
  </section>;
}
async function loadAll<T>(path: string): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; ; page++) { const result = await api<{ items: T[]; hasMore: boolean }>(`${path}?page=${page}&pageSize=100`); items.push(...result.items); if (!result.hasMore) return items; }
}
