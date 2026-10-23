import { Fragment, useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { Feedback, kinds, stages, useAction } from './ledger-ui';
import { ResidueMatrix, type MatrixSetting } from './ResidueMatrix';
import { ResidueDialog } from './ResidueDialog';
import { effectiveWindowLabel, exactDuplicateCandidates, groupRuleSeries } from './residueProfiles';

export type Rule = {
  id: string; code: string; versionNo: number; companyId?: string | null; kind: string; substanceId: string; commodityId: string; residueDefinition: string;
  inputStage: string; outputStage: string; inputBasis: string; outputBasis: string; outputFraction: string; process: string; conditions: string;
  analyticalMethod?: string | null; unit: string; decimalPlaces: number; interpretationKind: string | null; ndThresholdKind: string | null;
  factorLower: string | null; factorUpper: string | null; factorSemantics: string | null; homogeneousMixing: boolean;
  relativeUncertaintyPercent?: string | null; relativeUncertaintyAtZero?: boolean;
  effectiveFrom: string; effectiveTo: string | null; supersedesId: string | null; evidenceRef: string; applicabilityStatement: string;
  reviewedBy: string; reviewAuthority: string; reviewedAt: string; publishedBy: string; publishedAt: string;
};
type RuleForm = Omit<Rule, 'publishedBy' | 'publishedAt' | 'decimalPlaces'> & { decimalPlaces: number | null };
type Substance = { id: string; code: string; name: string };
type Commodity = { id: string; code: string; name: string };
const blank = (kind = '', companyId: string | null = null): RuleForm => {
  const id = crypto.randomUUID();
  return { id, code: `RES-${id}`, versionNo: 1, companyId, kind, substanceId: '', commodityId: '', residueDefinition: '', inputStage: '', outputStage: '', inputBasis: '', outputBasis: '', outputFraction: '', process: '', conditions: '', analyticalMethod: null, unit: 'mg/kg', decimalPlaces: null, interpretationKind: null, ndThresholdKind: null, factorLower: null, factorUpper: null, factorSemantics: null, homogeneousMixing: false, relativeUncertaintyPercent: null, relativeUncertaintyAtZero: false, effectiveFrom: '', effectiveTo: null, supersedesId: null, evidenceRef: '', applicabilityStatement: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' };
};
const dateValue = (value: string) => value ? new Date(value).toISOString() : '';
const taskNames: Record<string, string> = { INTERPRETATION: 'Interpret laboratory results', PROCESS: 'Define processing factors' };
const operations = [...kinds, 'CUSTODY_INGRESS', 'CUSTODY_WITHDRAW'];
const materials = [
  ...['RAW_PADDY', 'DRIED_PADDY', 'HEAD_RICE', 'BROKEN_RICE', 'BRAN', 'HUSK', 'BLENDED_RICE'].map(stage => ({ stage, product: stage })),
  ...['HEAD_RICE', 'BROKEN_RICE', 'BLENDED_RICE'].map(product => ({ stage: 'BAGGED_RICE', product })),
];
type ProcessPath = { inputStage: string; outputStage: string; outputFraction: string };
// Structural paths follow Ledger operations; analytical bases and factors still need scientific review.
function processPaths(operation: string): ProcessPath[] {
  switch (operation) {
    case 'DRYING': return [{ inputStage: 'RAW_PADDY', outputStage: 'DRIED_PADDY', outputFraction: 'DRIED_PADDY' }];
    case 'MILLING': return ['HEAD_RICE', 'BROKEN_RICE', 'BRAN', 'HUSK'].map(product => ({ inputStage: 'DRIED_PADDY', outputStage: product, outputFraction: product }));
    case 'BLENDING': return ['HEAD_RICE', 'BROKEN_RICE', 'BLENDED_RICE'].map(stage => ({ inputStage: stage, outputStage: 'BLENDED_RICE', outputFraction: 'BLENDED_RICE' }));
    case 'BAGGING': return ['HEAD_RICE', 'BROKEN_RICE', 'BLENDED_RICE'].map(stage => ({ inputStage: stage, outputStage: 'BAGGED_RICE', outputFraction: stage }));
    case 'MERGE':
    case 'CUSTODY_INGRESS':
    case 'CUSTODY_WITHDRAW': return materials.map(material => ({ inputStage: material.stage, outputStage: material.stage, outputFraction: material.product }));
    default: return [];
  }
}
const pathKey = (path: ProcessPath) => `${path.inputStage}:${path.outputStage}:${path.outputFraction}`;
const decimal = (value: string | null | undefined) => typeof value === 'string' && /^[0-9]{1,24}(\.[0-9]{1,18})?$/.test(value);

export function ResidueRuleMaintenance({ active, allowed, companyId = null, onPublished, policySettings = [] }: { active: boolean; allowed: boolean; companyId?: string | null; onPublished?: () => void; policySettings?: MatrixSetting[] }) {
  const [rules, setRules] = useState<Rule[]>([]), [substances, setSubstances] = useState<Substance[]>([]), [commodities, setCommodities] = useState<Commodity[]>([]);
  const [form, setForm] = useState<RuleForm>(() => blank('', companyId)), [error, setError] = useState(''), [loading, setLoading] = useState(false), [expandedRuleId, setExpandedRuleId] = useState<string | null>(null);
  const [sourceRule, setSourceRule] = useState<Rule | null>(null), [advanced, setAdvanced] = useState(false), [factorRange, setFactorRange] = useState(false);
  const [confirmed, setConfirmed] = useState(false), [conditional, setConditional] = useState(false);
  const [reuseId, setReuseId] = useState('');
  const [editorOpen, setEditorOpen] = useState(false);
  const [search, setSearch] = useState(''), [taskFilter, setTaskFilter] = useState(''), [scopeFilter, setScopeFilter] = useState(''), [statusFilter, setStatusFilter] = useState(''), [sort, setSort] = useState('code');
  const [historyOpen, setHistoryOpen] = useState<Record<string, boolean>>({});
  const action = useAction();
  const [reload, setReload] = useState(0);
  const [loadedScope, setLoadedScope] = useState<string | null | undefined>(undefined);
  useEffect(() => {
    setForm(blank('', companyId)); setSourceRule(null); setAdvanced(false); setFactorRange(false); setConfirmed(false); setConditional(false); setReuseId('');
    setEditorOpen(false);
  }, [companyId]);
  useEffect(() => {
    if (!active || !allowed) return;
    let current = true;
    setLoading(true); setError('');
    void Promise.all([api<{ items: Rule[] }>('/api/residue-rules'), loadAll<Substance>('/api/active-substances'), loadAll<Commodity>('/api/commodities')])
      .then(([rulePage, substanceItems, commodityItems]) => { if (current) { setRules(rulePage.items); setSubstances(substanceItems); setCommodities(commodityItems); setLoadedScope(companyId); } })
      .catch(cause => { if (current) setError(errorMessage(cause)); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [active, allowed, companyId, reload]);

  async function publish(event: FormEvent) {
    event.preventDefault();
    if (!allowed || loading || error || loadedScope !== companyId) return;
    await action.run(async () => {
      if (!confirmed) throw new Error('Review and confirm the complete profile before publishing.');
      if (!stages.includes(form.inputStage) || !stages.includes(form.outputStage)) {
        setAdvanced(true);
        throw new Error('Choose supported input and output stages before publishing.');
      }
      if (form.decimalPlaces === null) {
        setAdvanced(true);
        throw new Error('Set reviewed outward precision before publishing.');
      }
      const interpretation = form.kind === 'INTERPRETATION';
      const isNd = interpretation && form.interpretationKind === 'ND';
      const process = form.kind === 'PROCESS';
      const missingApplicability = ![form.substanceId, form.commodityId, form.residueDefinition, form.inputBasis, form.outputBasis].every(value => value.trim()) ||
        form.unit !== 'mg/kg' || !Number.isInteger(form.decimalPlaces) || form.decimalPlaces < 0 || form.decimalPlaces > 18 ||
        (interpretation && (!form.analyticalMethod?.trim() || form.inputStage !== form.outputStage || form.inputBasis !== form.outputBasis ||
          !['NUMERIC_ABSOLUTE_UNCERTAINTY', 'NUMERIC_RELATIVE_UNCERTAINTY', 'BELOW_LIMIT', 'ND'].includes(form.interpretationKind || '') ||
          (isNd && !['REPORTING_THRESHOLD', 'LOD', 'LOQ'].includes(form.ndThresholdKind || '')) ||
          (form.interpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' && !decimal(form.relativeUncertaintyPercent)))) ||
        (process && (!form.conditions.trim() || !form.outputFraction.trim() || !operations.includes(form.process) ||
          !['STEP_CONCENTRATION', 'CUMULATIVE_CONCENTRATION'].includes(form.factorSemantics || '') ||
          !decimal(form.factorLower) || (factorRange && !decimal(form.factorUpper))));
      if (missingApplicability) {
        setAdvanced(true);
        throw new Error('Complete the expert scientific applicability fields before publishing. No scientific defaults are supplied.');
      }
      if (process && form.factorSemantics === 'STEP_CONCENTRATION' && !processPaths(form.process).some(path => pathKey(path) === pathKey(form))) {
        setAdvanced(true);
        throw new Error('Choose a supported operation and material path. Historical values are unchanged; unsupported paths cannot be published.');
      }
      if (process && !materials.some(material => material.stage === form.outputStage && material.product === form.outputFraction)) {
        setAdvanced(true);
        throw new Error('Choose a supported output material and product identity. Historical values are unchanged.');
      }
      if (![form.effectiveFrom, form.evidenceRef, form.applicabilityStatement, form.reviewedBy, form.reviewAuthority, form.reviewedAt].every(value => value.trim()))
        throw new Error('Complete scientific review, evidence and effective period before publishing.');
      const body = {
        ...form,
        analyticalMethod: interpretation ? form.analyticalMethod || '' : null,
        interpretationKind: interpretation ? form.interpretationKind : null,
        ndThresholdKind: isNd ? form.ndThresholdKind : null,
        factorLower: process ? form.factorLower : null,
        factorUpper: process ? factorRange ? form.factorUpper : form.factorLower : null,
        factorSemantics: process ? form.factorSemantics : null,
        homogeneousMixing: process && form.homogeneousMixing,
        relativeUncertaintyPercent: interpretation && form.interpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' ? form.relativeUncertaintyPercent || null : null,
        relativeUncertaintyAtZero: interpretation && form.interpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' && form.relativeUncertaintyAtZero,
        effectiveFrom: dateValue(form.effectiveFrom), effectiveTo: form.effectiveTo ? dateValue(form.effectiveTo) : null, reviewedAt: dateValue(form.reviewedAt),
      };
      try {
        await api('/api/residue-rules', { method: 'POST', body: JSON.stringify(body) });
      } catch (cause) {
        if (cause instanceof ApiError && cause.status === 409) {
          try {
            setRules((await api<{ items: Rule[] }>('/api/residue-rules')).items);
          } catch (refreshCause) {
            throw new Error(`Published rule history changed. Your draft is unchanged, but latest history could not be loaded: ${errorMessage(refreshCause)}`);
          }
          throw new Error('Published rule history changed. Your draft is unchanged. Cancel revision when ready to discard this draft, then review the latest published rule in history and explicitly choose Revise rule to start a fresh version.');
        }
        throw cause;
      }
      setRules((await api<{ items: Rule[] }>('/api/residue-rules')).items);
      setForm(blank('', companyId));
      setSourceRule(null); setAdvanced(false); setFactorRange(false); setConfirmed(false); setConditional(false); setReuseId('');
      setEditorOpen(false);
      onPublished?.();
    }, 'Reviewed residue rule published.');
  }
  function set<K extends keyof RuleForm>(key: K, value: RuleForm[K]) { setConfirmed(false); setForm(current => ({ ...current, [key]: value })); }
  function invalidateScientificReview() {
    setConfirmed(false);
    setForm(current => ({ ...current, reviewedBy: '', reviewedAt: '' }));
  }
  function changeInterpretation(kind: string) {
    setForm(current => ({ ...current, interpretationKind: kind || null, ndThresholdKind: null }));
  }
  function startRevision(rule: Rule) {
    const series = rules.filter(item => item.companyId === (companyId || null) && item.code === rule.code);
    const nextVersion = Math.max(...series.map(item => item.versionNo)) + 1;
    action.setError('');
    action.setStatus('');
    setSourceRule(rule); setAdvanced(false); setFactorRange(rule.factorLower !== rule.factorUpper);
    setEditorOpen(true);
    setConfirmed(false); setConditional(!!rule.conditions && rule.conditions !== 'UNCONDITIONAL');
    setForm({ ...rule, id: crypto.randomUUID(), companyId, versionNo: nextVersion, supersedesId: rule.id, effectiveFrom: '', effectiveTo: null, reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
  }
  function cloneSystemRule(rule: Rule) {
    action.setError('');
    action.setStatus('');
    setSourceRule(rule); setAdvanced(false); setFactorRange(rule.factorLower !== rule.factorUpper);
    setEditorOpen(true);
    setConfirmed(false); setConditional(!!rule.conditions && rule.conditions !== 'UNCONDITIONAL');
    setForm({ ...rule, id: crypto.randomUUID(), companyId, versionNo: 1, supersedesId: null, effectiveFrom: '', effectiveTo: null, reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
  }
  const substanceName = (id: string) => { const item = substances.find(value => value.id === id); return item ? `${item.code} · ${item.name}` : id; };
  const commodityName = (id: string) => { const item = commodities.find(value => value.id === id); return item ? `${item.code} · ${item.name}` : id; };
  const interpretation = form.kind === 'INTERPRETATION', process = form.kind === 'PROCESS';
  const revising = form.supersedesId !== null;
  const visibleRules = rules.filter(rule => rule.companyId === (companyId || null) || (companyId && !rule.companyId));
  const successors = new Map(rules.filter(rule => rule.supersedesId).map(rule => [`${rule.companyId || ''}:${rule.supersedesId}`, rule] as const));
  const companyCodes = new Set(rules.filter(rule => rule.companyId === companyId).map(rule => rule.code));
  const groups = groupRuleSeries(visibleRules);
  const reusableRules = visibleRules.filter(rule => !successors.has(`${rule.companyId || ''}:${rule.id}`) &&
    groups.some(group => group.tips.length === 1 && group.tips[0].id === rule.id) &&
    (rule.companyId === (companyId || null) || !companyCodes.has(rule.code)));
  const duplicatePairs = exactDuplicateCandidates(groups.flatMap(group => group.tips));
  const query = search.trim().toLowerCase();
  const matches = (rule: Rule) => (!taskFilter || (taskFilter === 'INTERPRETATION' ? rule.kind === taskFilter : rule.process === taskFilter)) &&
    (!scopeFilter || (scopeFilter === 'company' ? !!rule.companyId : !rule.companyId)) &&
    (!statusFilter || (statusFilter === 'conflict' ? groups.some(group => group.tips.length > 1 && group.tips.some(tip => tip.id === rule.id)) : effectiveWindowLabel(rule) === statusFilter)) &&
    (!query || `${Object.values(rule).join(' ')} ${substanceName(rule.substanceId)} ${commodityName(rule.commodityId)}`.toLowerCase().includes(query));
  const filteredGroups = groups.filter(group => group.versions.some(matches)).sort((a, b) => sort === 'substance'
    ? substanceName(a.tips[0].substanceId).localeCompare(substanceName(b.tips[0].substanceId)) || a.key.localeCompare(b.key)
    : sort === 'published' ? Math.max(...b.versions.map(rule => Date.parse(rule.publishedAt))) - Math.max(...a.versions.map(rule => Date.parse(rule.publishedAt)))
      : a.tips[0].code.localeCompare(b.tips[0].code) || a.key.localeCompare(b.key));
  const historicalOnlyMatches = new Set(filteredGroups.filter(group => group.history.some(matches) && !group.tips.some(matches)).map(group => group.key));
  const displayedRules = filteredGroups.flatMap(group => [...group.tips, ...(historyOpen[group.key] || historicalOnlyMatches.has(group.key) ? group.history : [])]);
  function closeEditor() {
    action.setError(''); action.setStatus(''); setSourceRule(null); setAdvanced(false); setFactorRange(false); setConfirmed(false); setConditional(false); setForm(blank('', companyId)); setEditorOpen(false);
  }
  const paths = processPaths(form.process);
  const selectedPath = paths.some(path => pathKey(path) === pathKey(form)) ? pathKey(form) : '';
  function changeOperation(operation: string) {
    const options = processPaths(operation);
    setForm(current => ({ ...current, process: operation,
      ...(current.factorSemantics === 'STEP_CONCENTRATION' ? options.length === 1 ? options[0] : { inputStage: '', outputStage: '', outputFraction: '' } : {}) }));
  }
  return <section className="panel residue-rule-maintenance" aria-labelledby="residue-rules-title">
    <h2 id="residue-rules-title">{companyId ? 'Company residue rules' : 'System residue rules'}</h2>
    <p>{companyId ? 'Company rules use same-Code system series, retain company scope and require fresh review.' : 'Versioned, reviewed interpretation and process rules. Numeric values have no defaults; publishing does not certify scientific validity.'}</p>
    <button type="button" disabled={loading || !!error || !allowed || loadedScope !== companyId || action.busy} onClick={() => { closeEditor(); setEditorOpen(true); }}>Create rule</button>
    {loading && <p role="status">Loading published residue rules…</p>}
    {error && <div className="notice error" role="alert">{error}<button type="button" className="secondary" disabled={loading} onClick={() => setReload(value => value + 1)}>Retry loading rules</button></div>}
    {!editorOpen && <Feedback {...action} />}
    <section aria-label="Reuse a reviewed profile" hidden={editorOpen}>
      <h3>Reuse exact reviewed applicability</h3>
      <p>Choose the complete profile for the same substance, commodity, stage, product, method and scientific applicability. Reuse starts a revision or company override, never copies assumptions to another identity. Batch editing shares review work, not scientific requirements.</p>
      <label>Reviewed profile<select value={reuseId} disabled={loading || !!error || !allowed || loadedScope !== companyId} onChange={event => setReuseId(event.target.value)}>
        <option value="">Choose a complete reviewed profile</option>
        {reusableRules.map(rule => <option key={rule.id} value={rule.id}>{rule.code} v{rule.versionNo} · {rule.companyId ? 'Company' : 'System'} · {substanceName(rule.substanceId)} · {commodityName(rule.commodityId)} · {rule.kind === 'PROCESS' ? `${rule.process} · ${rule.outputFraction} · ${rule.factorSemantics}` : `${rule.interpretationKind} · ${rule.analyticalMethod}`} · {rule.inputStage}/{rule.inputBasis} → {rule.outputStage}/{rule.outputBasis}</option>)}
      </select></label>
      <button type="button" disabled={!reuseId || loading || !!error || !allowed || loadedScope !== companyId} onClick={() => {
        const source = reusableRules.find(rule => rule.id === reuseId);
        if (!source) return;
        if (companyId && !source.companyId) cloneSystemRule(source); else startRevision(source);
      }}>Use reviewed profile</button>
      {!loading && !reusableRules.length && <p>No reusable profile. Ask an expert to create the missing applicability below.</p>}
    </section>
    <div role="region" aria-label="Residue rule overview" hidden={editorOpen}>
    {allowed && loadedScope === companyId && <ResidueMatrix rules={rules} substances={substances} commodities={commodities} companyId={companyId} settings={policySettings} disabled={loading || !!error} onPublished={() => { setReload(value => value + 1); onPublished?.(); }} />}
    <p className="residue-counts">{groups.length} current scoped series · {groups.reduce((sum, group) => sum + group.tips.length, 0)} unsuperseded published tips · {groups.reduce((sum, group) => sum + group.history.length, 0)} historical versions · {duplicatePairs.length} exact duplicate candidate pairs (not history). Latest publication is not necessarily effective now; company adoption remains separate.</p>
    {duplicatePairs.length > 0 && <details><summary>Exact duplicate candidates ({duplicatePairs.length}) — not version history</summary><p>Same scope, complete applicability and policy values, overlapping effective windows, different codes. Expert evidence review required; nothing merged or adopted automatically.</p><ul>{duplicatePairs.map(([left, right]) => <li key={`${left.id}:${right.id}`}>{left.companyId ? 'Company' : 'System'} · {left.code} v{left.versionNo} / {right.code} v{right.versionNo} · {substanceName(left.substanceId)} · {commodityName(left.commodityId)} · {left.inputStage} → {left.outputStage} · {left.outputFraction || left.analyticalMethod}</li>)}</ul></details>}
    <div className="residue-toolbar">
      <label>Search rules and history<input type="search" value={search} onChange={event => setSearch(event.target.value)} /></label>
      <label>Rule task<select value={taskFilter} onChange={event => setTaskFilter(event.target.value)}><option value="">All tasks</option><option value="INTERPRETATION">Interpretation</option>{Array.from(new Set(visibleRules.filter(rule => rule.kind === 'PROCESS').map(rule => rule.process))).sort().map(task => <option key={task}>{task}</option>)}</select></label>
      <label>Rule scope<select value={scopeFilter} onChange={event => setScopeFilter(event.target.value)}><option value="">All scopes</option><option value="system">System</option><option value="company">Company</option></select></label>
      <label>Rule status<select value={statusFilter} onChange={event => setStatusFilter(event.target.value)}><option value="">All statuses</option>{['Within effective window now', 'Scheduled', 'Outside effective window'].map(status => <option key={status}>{status}</option>)}<option value="conflict">Conflicting published tips</option></select></label>
      <label>Sort rules<select value={sort} onChange={event => setSort(event.target.value)}><option value="code">Series code</option><option value="substance">Substance</option><option value="published">Recently published</option></select></label>
    </div>
    {!loading && !error && visibleRules.length > 0 && !filteredGroups.length && <p className="empty">No series match these filters. Clear search or change filters to see published profiles.</p>}
    {visibleRules.length > 0 && <div className="table-scroll residue-rule-table-scroll" role="region" aria-label="Published residue rules" tabIndex={0}>
      <table className="residue-rule-table">
        <caption>{filteredGroups.length} matching scoped series · {displayedRules.length} displayed versions</caption>
        <thead><tr><th scope="col">Rule</th><th scope="col">Applies to</th><th scope="col">Policy</th><th scope="col">Effective period</th><th scope="col">Version status</th><th scope="col">Actions</th></tr></thead>
        <tbody>{displayedRules.map(rule => {
          const successor = successors.get(`${rule.companyId || ''}:${rule.id}`);
          const summary = rule.kind === 'INTERPRETATION' ? `${rule.interpretationKind || 'Interpretation'} · ${rule.inputStage} · ${rule.inputBasis} · ${rule.analyticalMethod || 'no method'} · ${rule.ndThresholdKind || 'no ND threshold'}` : `${rule.process} · ${rule.inputStage} → ${rule.outputStage} · ${rule.outputFraction}`;
          let policy: string, policyDetail: string;
          if (rule.kind === 'INTERPRETATION') {
            switch (rule.interpretationKind) {
              case 'NUMERIC_RELATIVE_UNCERTAINTY':
                policy = `Numeric · +/-${rule.relativeUncertaintyPercent ?? 'unspecified'}%`;
                policyDetail = 'Relative uncertainty';
                break;
              case 'NUMERIC_ABSOLUTE_UNCERTAINTY':
                policy = 'Numeric · absolute uncertainty';
                policyDetail = 'Absolute uncertainty';
                break;
              case 'BELOW_LIMIT':
                policy = 'Below analytical limit';
                policyDetail = 'Result threshold';
                break;
              case 'ND':
                policy = 'Not detected (ND)';
                policyDetail = rule.ndThresholdKind === 'REPORTING_THRESHOLD' ? 'Reporting threshold' : rule.ndThresholdKind === 'LOD' ? 'Limit of detection (LOD)' : rule.ndThresholdKind === 'LOQ' ? 'Limit of quantification (LOQ)' : 'Threshold unspecified';
                break;
              default:
                policy = rule.interpretationKind || 'Interpretation unspecified';
                policyDetail = 'Interpretation policy';
            }
          } else {
            const semantics = rule.factorSemantics === 'STEP_CONCENTRATION' ? 'Per-step concentration factor' : rule.factorSemantics === 'CUMULATIVE_CONCENTRATION' ? 'Cumulative concentration factor from input-stage anchor' : 'Factor semantics unspecified';
            policy = `${rule.process} · ${rule.factorLower ?? '—'}–${rule.factorUpper ?? '—'}×`;
            policyDetail = semantics;
          }
          const status = successor ? 'Superseded history' : 'Latest published tip';
          const group = groups.find(item => item.versions.some(version => version.id === rule.id))!;
          const detailId = `residue-rule-details-${rule.id}`;
          return <Fragment key={rule.id}>
            {rule.id === group.tips[0]?.id && <tr className="residue-rule-history-heading"><td colSpan={6}><strong>{rule.code} · {rule.companyId ? 'Company' : 'System'}</strong>{group.tips.length > 1 && <span className="notice pending"> Conflicting published tips — every tip retained; no automatic selection.</span>}{group.history.length > 0 && <button type="button" className="secondary" disabled={historicalOnlyMatches.has(group.key)} aria-expanded={!!historyOpen[group.key] || historicalOnlyMatches.has(group.key)} onClick={() => setHistoryOpen(current => ({ ...current, [group.key]: !current[group.key] }))}>{historyOpen[group.key] || historicalOnlyMatches.has(group.key) ? 'Collapse' : 'Show'} earlier versions · {rule.code} · {group.history.length}</button>}{historicalOnlyMatches.has(group.key) && <span> Historical filter matches included below; latest tips retained. Clear filters to collapse history.</span>}</td></tr>}
            <tr>
              <td><div className="residue-rule-badges"><span className="badge">{rule.companyId ? 'Company' : 'System'}</span><span className="badge">v{rule.versionNo}</span></div><strong className="residue-rule-kind">{rule.kind === 'INTERPRETATION' ? 'Interpretation' : 'Processing'}</strong><span className="table-subtitle residue-rule-code">{rule.code}</span></td>
              <td className="residue-rule-applies">{substanceName(rule.substanceId)}<span className="table-subtitle">{commodityName(rule.commodityId)} · {rule.inputStage} / {rule.inputBasis} → {rule.outputStage} / {rule.outputBasis} · {rule.outputFraction || rule.analyticalMethod} · {rule.residueDefinition}</span></td>
              <td>{policy}<span className="table-subtitle">{policyDetail}</span></td>
              <td><time dateTime={rule.effectiveFrom}>{new Date(rule.effectiveFrom).toLocaleDateString(undefined, { timeZone: 'UTC', dateStyle: 'medium' })}</time><span className="table-subtitle">{rule.effectiveTo ? <>Until <time dateTime={rule.effectiveTo}>{new Date(rule.effectiveTo).toLocaleDateString(undefined, { timeZone: 'UTC', dateStyle: 'medium' })}</time></> : 'No stated end'}</span></td>
              <td><span className={`badge${successor ? ' inactive' : ''}`}>{status}</span><span className="table-subtitle">{effectiveWindowLabel(rule)} · not an adoption decision</span>{successor && <span id={`superseded-${rule.id}`} className="table-subtitle">Already superseded by {successor.code} v{successor.versionNo}; revise latest version instead.</span>}</td>
              <td className="admin-row-actions"><div className="residue-rule-actions">
                <button type="button" className="secondary" aria-expanded={expandedRuleId === rule.id} aria-controls={detailId} aria-label={`Details for ${rule.code} v${rule.versionNo} · ${rule.companyId ? 'Company' : 'System'}`} onClick={() => setExpandedRuleId(current => current === rule.id ? null : rule.id)}>Details</button>
                {companyId && !rule.companyId && !successor && !companyCodes.has(rule.code) && <button type="button" className="secondary" disabled={action.busy || group.tips.length !== 1} aria-label={`Clone as company rule · ${substanceName(rule.substanceId)} · ${commodityName(rule.commodityId)} · ${rule.code} v${rule.versionNo}`} onClick={() => cloneSystemRule(rule)}>Clone as company rule</button>}
                {rule.companyId === (companyId || null) && <button type="button" className="secondary" disabled={action.busy || successor !== undefined || group.tips.length !== 1} aria-label={`Revise rule · ${taskNames[rule.kind]} · ${substanceName(rule.substanceId)} · ${commodityName(rule.commodityId)} · ${summary} · v${rule.versionNo}`} aria-describedby={successor ? `superseded-${rule.id}` : undefined} onClick={() => startRevision(rule)}>Revise rule</button>}
              </div></td>
            </tr>
            <tr className="residue-rule-details-row" hidden={expandedRuleId !== rule.id}><td colSpan={6}><div id={detailId} className="residue-rule-details-panel">
              <h3>{rule.code} · v{rule.versionNo} scientific details</h3>
              <dl className="residue-rule-detail-grid">
                <div><dt>Residue definition</dt><dd>{rule.residueDefinition}</dd></div>
                <div><dt>Input stage / analytical basis</dt><dd>{rule.inputStage} · {rule.inputBasis}</dd></div>
                <div><dt>Output stage / analytical basis</dt><dd>{rule.outputStage} · {rule.outputBasis}</dd></div>
                {rule.kind === 'INTERPRETATION' ? <>
                  <div><dt>Interpretation and ND threshold kind</dt><dd>{rule.interpretationKind || 'Not specified'} · {rule.ndThresholdKind || 'Not applicable'}</dd></div>
                  <div><dt>Analytical method</dt><dd>{rule.analyticalMethod || 'Not specified'}</dd></div>
                  <div><dt>Relative uncertainty</dt><dd>{rule.relativeUncertaintyPercent == null ? 'Not specified' : `${rule.relativeUncertaintyPercent}%`}; reviewed at numeric zero: {rule.relativeUncertaintyAtZero ? 'Yes' : 'No'}</dd></div>
                </> : <>
                  <div><dt>Process / conditions</dt><dd>{rule.process} · {rule.conditions}</dd></div>
                  <div><dt>Output fraction / product identity</dt><dd>{rule.outputFraction}</dd></div>
                  <div><dt>Concentration-factor interval / semantics</dt><dd>{rule.factorLower}–{rule.factorUpper} · {rule.factorSemantics === 'STEP_CONCENTRATION' ? 'Per-step concentration factor' : rule.factorSemantics === 'CUMULATIVE_CONCENTRATION' ? 'Cumulative concentration factor from input-stage anchor' : rule.factorSemantics}; not a yield factor</dd></div>
                  <div><dt>Homogeneous mixing reviewed</dt><dd>{rule.homogeneousMixing ? 'Yes' : 'No'}</dd></div>
                </>}
                <div><dt>Concentration unit / outward precision</dt><dd>{rule.unit} · {rule.decimalPlaces} decimal places</dd></div>
                <div><dt>Effective period (exact)</dt><dd>{rule.effectiveFrom} — {rule.effectiveTo || 'no stated end'}</dd></div>
                <div><dt>Conditions / applicability</dt><dd>{rule.conditions || 'Not specified'} · {rule.applicabilityStatement}</dd></div>
                <div><dt>Evidence reference</dt><dd>{rule.evidenceRef}</dd></div>
                <div><dt>Scientific reviewer / authority / reviewed at</dt><dd>{rule.reviewedBy} · {rule.reviewAuthority} · {rule.reviewedAt}</dd></div>
                <div><dt>Published by / at</dt><dd>{rule.publishedBy} · {rule.publishedAt}</dd></div>
                <div><dt>Predecessor</dt><dd>{rule.supersedesId || 'None'}</dd></div>
              </dl>
            </div></td></tr>
          </Fragment>;
        })}</tbody>
      </table>
    </div>}
    {!loading && !error && visibleRules.length === 0 && <p className="empty">No reviewed scientific profiles available. Residue bounds remain unresolved until an expert publishes applicable rules.</p>}
    </div>
    {editorOpen && <ResidueDialog key={form.kind} title={form.kind ? revising ? 'Revise rule' : sourceRule ? 'Create company override' : 'Create rule' : 'Create rule'} busy={action.busy} onClose={closeEditor}>
      <Feedback {...action} />
      {!form.kind ? <section aria-label="Choose residue rule task">
        <p>Expert setup only. Reuse exact reviewed applicability where possible; no scientific values supplied by default.</p>
        <div className="ledger-actions">
          <button type="button" onClick={() => { setAdvanced(true); setForm(blank('INTERPRETATION', companyId)); }}>Interpret laboratory results</button>
          <button type="button" onClick={() => { setAdvanced(true); setForm(blank('PROCESS', companyId)); }}>Define processing factors</button>
        </div>
      </section> : <>
      {revising && <p role="status">Revising {taskNames[form.kind]} for {substanceName(form.substanceId)} · {commodityName(form.commodityId)}. Previous publication remains unchanged until new rule is reviewed and published.</p>}
      <button type="button" disabled={action.busy} onClick={closeEditor}>{revising ? 'Cancel revision' : 'Cancel profile draft'}</button>
      <form onSubmit={publish}>
        <fieldset disabled={action.busy || loading || !!error || !allowed || loadedScope !== companyId}><legend>{taskNames[form.kind]}</legend>
          {sourceRule && <>
            <h3>Reuse reviewed applicability</h3>
            <dl className="residue-rule-detail-grid" aria-label="Profile applicability">
              <div><dt>Substance / commodity</dt><dd>{substanceName(form.substanceId)} · {commodityName(form.commodityId)}</dd></div>
              <div><dt>Residue definition / method</dt><dd>{form.residueDefinition} · {form.analyticalMethod || 'Not applicable'}</dd></div>
              <div><dt>Stage / basis</dt><dd>{form.inputStage} · {form.inputBasis} → {form.outputStage} · {form.outputBasis}</dd></div>
              <div><dt>Unit / outward precision</dt><dd>{form.unit} · {form.decimalPlaces ?? 'Unspecified'} decimal places</dd></div>
              <div><dt>Scientific policy</dt><dd>{interpretation ? `${form.interpretationKind} · ${form.ndThresholdKind || 'No ND threshold'}` : `${form.process} · ${form.outputFraction} · ${form.conditions} · ${form.factorSemantics} · homogeneous mixing: ${form.homogeneousMixing ? 'reviewed' : 'not reviewed'}`}</dd></div>
            </dl>
            <label className="checkbox-label"><input type="checkbox" checked={advanced} onChange={event => setAdvanced(event.target.checked)} /> Edit scientific applicability (expert)</label>
            <p>Changing scientific values requires fresh review below. Original publication and review remain unchanged.</p>
          </>}
          {advanced && <>
          <fieldset onChange={invalidateScientificReview}><legend>Applicability</legend>
            <div className="ledger-form-grid">
            {sourceRule ? <p>Series: {taskNames[form.kind]} · {substanceName(form.substanceId)} · {commodityName(form.commodityId)}. Create a new expert profile to change substance or commodity.</p> : <>
              <label>Substance<select required value={form.substanceId} onChange={e => set('substanceId', e.target.value)}><option value="">Choose substance</option>{substances.map(s => <option key={s.id} value={s.id}>{s.code} · {s.name}</option>)}</select></label>
              <label>Commodity<select required value={form.commodityId} onChange={e => set('commodityId', e.target.value)}><option value="">Choose commodity</option>{commodities.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label>
            </>}
            <label>Residue definition<input required value={form.residueDefinition} onChange={e => set('residueDefinition', e.target.value)} /></label>
            {interpretation ? <>
              <label>Material stage<select required value={stages.includes(form.inputStage) && form.inputStage === form.outputStage ? form.inputStage : ''} onChange={e => setForm(current => ({ ...current, inputStage: e.target.value, outputStage: e.target.value }))}><option value="">Choose material stage</option>{stages.map(stage => <option key={stage} value={stage}>{stage}</option>)}</select></label>
              <label>Analytical basis<input required value={form.inputBasis} onChange={e => setForm(current => ({ ...current, inputBasis: e.target.value, outputBasis: e.target.value }))} /></label>
            </> : <>
              <label>Factor semantics<select required value={form.factorSemantics || ''} onChange={e => {
                const semantics = e.target.value || null;
                setForm(current => ({ ...current, factorSemantics: semantics,
                  ...(semantics === 'STEP_CONCENTRATION' && paths.length === 1 ? paths[0] : {}) }));
              }}><option value="">Choose factor semantics</option><option value="STEP_CONCENTRATION">Per-step concentration factor</option><option value="CUMULATIVE_CONCENTRATION">Cumulative concentration factor</option></select></label>
              <label>Operation<select required value={operations.includes(form.process) ? form.process : ''} onChange={e => changeOperation(e.target.value)}><option value="">Choose operation</option>{operations.map(kind => <option key={kind} value={kind}>{kind}</option>)}</select></label>
              {form.factorSemantics === 'STEP_CONCENTRATION' && (paths.length === 1 ? <>
                <p aria-label="Derived material path">{paths[0].inputStage} → {paths[0].outputStage} · {paths[0].outputFraction}</p>
                {!selectedPath && <button type="button" onClick={() => { setForm(current => ({ ...current, ...paths[0] })); invalidateScientificReview(); }}>Use supported drying material path</button>}
              </> :
                <label>Operation material path<select required value={selectedPath} onChange={e => {
                  const path = paths.find(option => pathKey(option) === e.target.value);
                  if (path) setForm(current => ({ ...current, ...path }));
                }}><option value="">Choose input material and output</option>{paths.map(path => <option key={pathKey(path)} value={pathKey(path)}>{path.inputStage} → {path.outputStage} · {path.outputFraction}</option>)}</select></label>)}
              {form.factorSemantics === 'STEP_CONCENTRATION' && paths.length > 1 && selectedPath && <p aria-label="Selected material path">{form.inputStage} → {form.outputStage} · product {form.outputFraction}</p>}
              {form.factorSemantics === 'CUMULATIVE_CONCENTRATION' && <>
                <p>Cumulative factors retain their reviewed input-stage anchor and analytical bases. They are not per-step factors.</p>
                <label>Cumulative input-stage anchor<select required value={stages.includes(form.inputStage) ? form.inputStage : ''} onChange={e => set('inputStage', e.target.value)}><option value="">Choose reviewed anchor</option>{stages.map(stage => <option key={stage} value={stage}>{stage}</option>)}</select></label>
                <label>Cumulative output material<select required value={materials.some(material => material.stage === form.outputStage && material.product === form.outputFraction) ? `${form.outputStage}:${form.outputFraction}` : ''} onChange={e => {
                  const material = materials.find(option => `${option.stage}:${option.product}` === e.target.value);
                  if (material) setForm(current => ({ ...current, outputStage: material.stage, outputFraction: material.product }));
                }}><option value="">Choose reviewed output</option>{materials.map(material => <option key={`${material.stage}:${material.product}`} value={`${material.stage}:${material.product}`}>{material.stage} · {material.product}</option>)}</select></label>
              </>}
              <label>Input analytical basis<input required value={form.inputBasis} onChange={e => set('inputBasis', e.target.value)} /></label><label>Output analytical basis<input required value={form.outputBasis} onChange={e => set('outputBasis', e.target.value)} /></label>
            </>}
            </div>
          </fieldset>
          <fieldset onChange={invalidateScientificReview}><legend>Scientific rule</legend>
            <div className="ledger-form-grid">
            <p>Concentration unit: mg/kg</p>
            <label>Reviewed outward precision (decimal places)<input required type="number" min="0" max="18" step="1" value={form.decimalPlaces ?? ''} onChange={e => set('decimalPlaces', e.target.value === '' ? null : Number(e.target.value))} /></label>
            {interpretation ? <>
              <label>Interpretation<select required value={form.interpretationKind || ''} onChange={e => changeInterpretation(e.target.value)}><option value="">Choose interpretation</option><option value="NUMERIC_ABSOLUTE_UNCERTAINTY">Numeric result with absolute uncertainty</option><option value="NUMERIC_RELATIVE_UNCERTAINTY">Numeric result with relative uncertainty (%)</option><option value="BELOW_LIMIT">Below analytical limit</option><option value="ND">Not detected (ND)</option></select></label>
              <label>Analytical method<input required value={form.analyticalMethod || ''} onChange={e => set('analyticalMethod', e.target.value || null)} /></label>
              {form.interpretationKind === 'ND' && <label>ND threshold policy<select required value={form.ndThresholdKind || ''} onChange={e => set('ndThresholdKind', e.target.value || null)}><option value="">Choose reviewed threshold policy</option><option value="REPORTING_THRESHOLD">Reporting threshold</option><option value="LOD">Limit of detection (LOD)</option><option value="LOQ">Limit of quantification (LOQ)</option></select></label>}
            </> : <>
              <label>Conditions<select required value={form.conditions === 'UNCONDITIONAL' ? 'UNCONDITIONAL' : conditional ? 'CONDITIONAL' : ''} onChange={e => {
                const conditional = e.target.value === 'CONDITIONAL';
                setConditional(conditional); set('conditions', e.target.value === 'UNCONDITIONAL' ? 'UNCONDITIONAL' : '');
              }}><option value="">Choose reviewed applicability</option><option value="UNCONDITIONAL">No additional conditions</option><option value="CONDITIONAL">Applies only under specified conditions</option></select></label>
              {conditional && <label>Applicable conditions<input required value={form.conditions} onChange={e => set('conditions', e.target.value)} /></label>}
              <label className="checkbox-label"><input type="checkbox" checked={form.homogeneousMixing} onChange={e => set('homogeneousMixing', e.target.checked)} /> Reviewed homogeneous mixing applicability</label>
            </>}
            </div>
          </fieldset>
          </>}
          {interpretation && form.interpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' && <div className="ledger-form-grid" onChange={invalidateScientificReview}>
            <label>Relative uncertainty (%)<input required inputMode="decimal" value={form.relativeUncertaintyPercent || ''} onChange={e => set('relativeUncertaintyPercent', e.target.value || null)} /></label>
            <label className="checkbox-label"><input type="checkbox" checked={form.relativeUncertaintyAtZero || false} onChange={e => set('relativeUncertaintyAtZero', e.target.checked)} /> Reviewed relative uncertainty at numeric zero</label>
          </div>}
          {process && <div className="ledger-form-grid" onChange={invalidateScientificReview}>
            <label className="checkbox-label"><input type="checkbox" checked={factorRange} onChange={event => setFactorRange(event.target.checked)} /> Use range</label>
            <label>{factorRange ? 'Factor lower bound (decimal)' : 'Concentration factor (decimal)'}<input required inputMode="decimal" value={form.factorLower || ''} onChange={e => set('factorLower', e.target.value || null)} /></label>
            {factorRange && <label>Factor upper bound (decimal)<input required inputMode="decimal" value={form.factorUpper || ''} onChange={e => set('factorUpper', e.target.value || null)} /></label>}
          </div>}
          <fieldset><legend>Review &amp; publish</legend>
          <details>
            <summary>Full scientific profile</summary>
            <dl className="residue-rule-detail-grid">{Object.entries(form).filter(([key]) => !['id', 'publishedBy', 'publishedAt', 'reviewedBy', 'reviewAuthority', 'reviewedAt'].includes(key)).map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{key === 'factorUpper' && process && !factorRange ? form.factorLower || 'Not specified' : value === null || value === '' ? 'Not specified' : String(value)}</dd></div>)}</dl>
          </details>
            <div className="ledger-form-grid">
            <label>Effective from<input required type="datetime-local" value={form.effectiveFrom} onChange={e => set('effectiveFrom', e.target.value)} /></label>
            <label>Effective until (optional)<input type="datetime-local" value={form.effectiveTo || ''} onChange={e => set('effectiveTo', e.target.value || null)} /></label>
            <label>Evidence reference<input required value={form.evidenceRef} onChange={e => set('evidenceRef', e.target.value)} /></label>
            <label>Applicability statement<textarea required value={form.applicabilityStatement} onChange={e => set('applicabilityStatement', e.target.value)} /></label>
            <label>Scientific reviewer<input required value={form.reviewedBy} onChange={e => set('reviewedBy', e.target.value)} /></label>
            <label>Review authority<input required value={form.reviewAuthority} onChange={e => set('reviewAuthority', e.target.value)} /></label>
            <label>Reviewed at<input required type="datetime-local" value={form.reviewedAt} onChange={e => set('reviewedAt', e.target.value)} /></label>
            </div>
            <label className="checkbox-label"><input required type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} /> I reviewed the complete scientific profile, evidence and effective period.</label>
            <button disabled={action.busy}>{action.busy ? 'Publishing…' : companyId ? 'Publish reviewed company rule' : 'Publish reviewed rule'}</button>
          </fieldset>
        </fieldset>
      </form>
    </>}
    </ResidueDialog>}
  </section>;
}
async function loadAll<T>(path: string): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; ; page++) {
    const result = await api<{ items: T[]; hasMore: boolean }>(`${path}?page=${page}&pageSize=100`);
    items.push(...result.items);
    if (!result.hasMore) return items;
  }
}
