import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from './api';
import { Feedback, stages, useAction } from './ledger-ui';
import type { Rule } from './ResidueRuleMaintenance';
import { ResidueDialog } from './ResidueDialog';
import { effectiveWindowLabel, groupRuleSeries } from './residueProfiles';

export type MatrixSetting = { id: string; ruleCode: string; mode: string; supersedesId: string | null; effectiveFrom: string; effectiveTo: string | null };
type Item = { id: string; code: string; name: string };
type Draft = { source: Rule; rule: Rule; baseId: string | null; settingBaseId: string | null; adopt: boolean; range: boolean };
const blankReview = () => ({ effectiveFrom: '', effectiveTo: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
const sameScope = (rule: Rule, companyId: string | null) => (rule.companyId || null) === companyId;
const policy = (rule: Rule) => rule.kind === 'PROCESS'
  ? `${rule.factorLower ?? 'Missing'}${rule.factorUpper !== rule.factorLower ? `–${rule.factorUpper ?? 'Missing'}` : ''}× · ${rule.factorSemantics}`
  : rule.interpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' ? `±${rule.relativeUncertaintyPercent ?? 'Missing'}%`
    : rule.interpretationKind === 'ND' ? `ND · ${rule.ndThresholdKind || 'Missing threshold'}` : rule.interpretationKind;
const applicabilityKey = (rule: Rule) => JSON.stringify([rule.substanceId, rule.commodityId, rule.kind, rule.residueDefinition,
  rule.inputStage, rule.outputStage, rule.inputBasis, rule.outputBasis, rule.analyticalMethod, rule.interpretationKind?.startsWith('NUMERIC_') ? 'NUMERIC' : rule.interpretationKind,
  rule.process, rule.outputFraction, rule.conditions]);
const overlaps = (left: Rule, right: Rule) => Date.parse(left.effectiveFrom) < (right.effectiveTo ? Date.parse(right.effectiveTo) : Infinity) &&
  Date.parse(right.effectiveFrom) < (left.effectiveTo ? Date.parse(left.effectiveTo) : Infinity);

export function ResidueMatrix({ rules, substances, commodities, companyId, settings, onPublished, disabled = false }: {
  rules: Rule[]; substances: Item[]; commodities: Item[]; companyId: string | null; settings: MatrixSetting[]; onPublished: () => void; disabled?: boolean;
}) {
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [review, setReview] = useState(blankReview), [confirmed, setConfirmed] = useState(false), [stale, setStale] = useState(false);
  const action = useAction();
  const [open, setOpen] = useState(false), [search, setSearch] = useState(''), [task, setTask] = useState('');
  useEffect(() => { setDrafts({}); setReview(blankReview()); setConfirmed(false); setStale(false); }, [companyId]);
  const current = groupRuleSeries(rules.filter(rule => !rule.companyId || rule.companyId === companyId)).flatMap(group => group.tips);
  const codes = Array.from(new Set(current.map(rule => rule.code))).sort();
  const filteredCodes = codes.filter(code => drafts[code] || current.some(rule => rule.code === code &&
    (!task || (task === 'INTERPRETATION' ? rule.kind === task : rule.process === task)) &&
    `${Object.values(rule).join(' ')} ${substances.find(item => item.id === rule.substanceId)?.name} ${commodities.find(item => item.id === rule.commodityId)?.name}`.toLowerCase().includes(search.trim().toLowerCase())));
  const changed = Object.values(drafts).filter(draft => draft.baseId === null || JSON.stringify(draft.rule) !== JSON.stringify(draft.source));
  const adoptions = Object.values(drafts).filter(draft => draft.adopt);
  const publicationRules = [...changed, ...adoptions.filter(draft => !changed.includes(draft))];
  const pending = changed.length > 0 || adoptions.length > 0;
  function edit(source: Rule) {
    const previous = settings.filter(item => item.ruleCode === source.code && !settings.some(next => next.supersedesId === item.id));
    setDrafts(value => ({ ...value, [source.code]: {
      source, rule: { ...source, companyId }, baseId: sameScope(source, companyId) ? source.id : null,
      settingBaseId: previous.length === 1 ? previous[0].id : null, adopt: false, range: source.factorLower !== source.factorUpper,
    } }));
    setReview(blankReview()); setConfirmed(false);
  }
  function change(code: string, patch: Partial<Rule>) {
    setDrafts(value => ({ ...value, [code]: { ...value[code], rule: { ...value[code].rule, ...patch } } }));
    setReview(value => ({ ...value, reviewedBy: '', reviewedAt: '' })); setConfirmed(false);
  }
  function discard() { setDrafts({}); setReview(blankReview()); setConfirmed(false); setStale(false); action.setError(''); onPublished(); }
  async function publish(event: FormEvent) {
    event.preventDefault();
    if (!confirmed || !pending || stale || disabled) return;
    await action.run(async () => {
      const body = {
        companyId, ...review, effectiveFrom: new Date(review.effectiveFrom).toISOString(),
        effectiveTo: review.effectiveTo ? new Date(review.effectiveTo).toISOString() : null, reviewedAt: new Date(review.reviewedAt).toISOString(),
        rules: publicationRules.map(draft => ({ baseId: draft.baseId, sourceId: draft.source.id, rule: draft.rule })),
        settings: adoptions.map(draft => ({ ruleCode: draft.rule.code, baseId: draft.settingBaseId,
          mode: 'COMPANY_OVERRIDE', evidenceRef: draft.rule.evidenceRef, applicabilityStatement: draft.rule.applicabilityStatement })),
      };
      try { await api('/api/residue-matrix/publish', { method: 'POST', body: JSON.stringify(body) }); }
      catch (cause) {
        if (cause instanceof ApiError && cause.status === 409) { setStale(true); throw new Error('Publication history changed. Draft preserved; nothing published. Discard and load current rules to start a fresh review.'); }
        throw cause;
      }
      setDrafts({}); setReview(blankReview()); setConfirmed(false); onPublished();
    }, 'Matrix rules and explicitly selected adoption settings published together; unchanged scientific versions retained.');
  }
  function profile(code: string) {
      const series = current.filter(rule => rule.code === code), own = series.filter(rule => sameScope(rule, companyId));
      const system = series.filter(rule => !rule.companyId);
      const candidates = own.length ? own : system;
      const source = candidates.length === 1 ? candidates[0] : null;
      const draft = drafts[code];
      const conflict = series.some(rule => current.some(other => other.id !== rule.id && sameScope(other, rule.companyId || null) && applicabilityKey(other) === applicabilityKey(rule) && overlaps(rule, other)));
      const settingTips = settings.filter(item => item.ruleCode === code && !settings.some(next => next.supersedesId === item.id));
      return <section key={code} aria-label={`Matrix profile ${code}`}>
        <strong>{code}</strong>
        {series.map(rule => <p key={rule.id}><span className="badge">{rule.companyId ? 'Company override' : 'System default'} · latest published tip v{rule.versionNo}</span> {substances.find(item => item.id === rule.substanceId)?.name || rule.substanceId} · {policy(rule)} · {effectiveWindowLabel(rule)}</p>)}
        {companyId && <p>{settingTips.length > 1 ? 'Conflicting adoption settings — review history' : settingTips.length ? `Explicit setting: ${settingTips[0].mode}` : 'Not adopted — bounds unresolved'}</p>}
        {(conflict || candidates.length > 1) && <p className="notice pending" role="status">Conflicting applicability — no rule chosen automatically. Review each complete profile.</p>}
        <details><summary>Applicability and evidence · {code}</summary>{series.map(rule => <dl key={rule.id}>
          <dt>Scope</dt><dd>{commodities.find(item => item.id === rule.commodityId)?.name || rule.commodityId} · {rule.residueDefinition} · {rule.analyticalMethod || 'No analytical method'}</dd>
          <dt>Stage / material basis</dt><dd>{rule.inputStage} / {rule.inputBasis} → {rule.outputStage} / {rule.outputBasis}</dd>
          <dt>Processing / concentration</dt><dd>{rule.process || 'Interpretation'} · {rule.outputFraction || 'No fraction'} · {rule.conditions || 'No conditions'} · {rule.unit} · {rule.decimalPlaces} decimal places · mixing: {rule.homogeneousMixing ? 'reviewed' : 'not reviewed'}</dd>
          <dt>Effective period</dt><dd>{rule.effectiveFrom} — {rule.effectiveTo || 'No stated end'}</dd>
          <dt>Review</dt><dd>{rule.reviewedBy} · {rule.reviewAuthority} · {rule.reviewedAt}</dd>
          <dt>Evidence and applicability</dt><dd>{rule.evidenceRef} · {rule.applicabilityStatement}</dd>
        </dl>)}</details>
        {!draft && <button type="button" className="secondary" disabled={!source || stale || action.busy || disabled} onClick={() => source && edit(source)}>
          {companyId ? `Edit company policy · ${code}` : `Edit system policy · ${code}`}
        </button>}
        {draft && <fieldset><legend>Draft · {code}</legend>
          {draft.rule.kind === 'INTERPRETATION' ? <>
            <label>Interpretation · {code}<select value={draft.rule.interpretationKind || ''} onChange={event => change(code, { interpretationKind: event.target.value, ndThresholdKind: null, relativeUncertaintyPercent: null, relativeUncertaintyAtZero: false })}>
              <option value="NUMERIC_ABSOLUTE_UNCERTAINTY">Numeric with report absolute uncertainty</option><option value="NUMERIC_RELATIVE_UNCERTAINTY">Numeric with relative uncertainty (%)</option><option value="BELOW_LIMIT">Below analytical limit</option><option value="ND">Not detected (ND)</option>
            </select></label>
            {draft.rule.interpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' && <>
              <label>Relative uncertainty (%) · {code}<input required inputMode="decimal" value={draft.rule.relativeUncertaintyPercent ?? ''} onChange={event => change(code, { relativeUncertaintyPercent: event.target.value || null })} /></label>
              <label><input type="checkbox" checked={draft.rule.relativeUncertaintyAtZero || false} onChange={event => change(code, { relativeUncertaintyAtZero: event.target.checked })} /> Reviewed relative uncertainty at zero · {code}</label>
            </>}
            {draft.rule.interpretationKind === 'ND' && <label>ND threshold policy · {code}<select required value={draft.rule.ndThresholdKind || ''} onChange={event => change(code, { ndThresholdKind: event.target.value || null })}><option value="">Choose reviewed threshold</option><option value="REPORTING_THRESHOLD">Reporting threshold</option><option value="LOD">Limit of detection (LOD)</option><option value="LOQ">Limit of quantification (LOQ)</option></select></label>}
          </> : <>
            <label><input type="checkbox" checked={draft.range} onChange={event => {
              const range = event.target.checked;
              setDrafts(value => ({ ...value, [code]: { ...value[code], range } }));
              if (!range) change(code, { factorUpper: draft.rule.factorLower });
              setReview(value => ({ ...value, reviewedBy: '', reviewedAt: '' })); setConfirmed(false);
            }} /> Factor range · {code}</label>
            <label>{draft.range ? 'Factor lower' : 'Concentration factor'} · {code}<input required inputMode="decimal" value={draft.rule.factorLower ?? ''} onChange={event => change(code, { factorLower: event.target.value || null, ...(!draft.range ? { factorUpper: event.target.value || null } : {}) })} /></label>
            {draft.range && <label>Factor upper · {code}<input required inputMode="decimal" value={draft.rule.factorUpper ?? ''} onChange={event => change(code, { factorUpper: event.target.value || null })} /></label>}
          </>}
          <details><summary>Advanced applicability · {code}</summary>
            <p>Complete series retained. New substance, commodity or rule kind requires Create rule outside this dialog.</p>
            <label>Residue definition · {code}<input required value={draft.rule.residueDefinition} onChange={event => change(code, { residueDefinition: event.target.value })} /></label>
            <label>Input stage · {code}<select required value={draft.rule.inputStage} onChange={event => change(code, { inputStage: event.target.value, ...(draft.rule.kind === 'INTERPRETATION' ? { outputStage: event.target.value } : {}) })}>{!stages.includes(draft.rule.inputStage) && <option value={draft.rule.inputStage}>{draft.rule.inputStage || 'Choose input stage'}</option>}{stages.map(stage => <option key={stage}>{stage}</option>)}</select></label>
            <label>Input basis · {code}<input required value={draft.rule.inputBasis} onChange={event => change(code, { inputBasis: event.target.value, ...(draft.rule.kind === 'INTERPRETATION' ? { outputBasis: event.target.value } : {}) })} /></label>
            {draft.rule.kind === 'INTERPRETATION' ? <label>Exact analytical method · {code}<input required value={draft.rule.analyticalMethod || ''} onChange={event => change(code, { analyticalMethod: event.target.value })} /></label> : <>
              <label>Output stage · {code}<select required value={draft.rule.outputStage} onChange={event => change(code, { outputStage: event.target.value })}>{!stages.includes(draft.rule.outputStage) && <option value={draft.rule.outputStage}>{draft.rule.outputStage || 'Choose output stage'}</option>}{stages.map(stage => <option key={stage}>{stage}</option>)}</select></label>
              {(['outputBasis', 'outputFraction', 'process', 'conditions'] as const).map(key => <label key={key}>{key} · {code}<input required value={draft.rule[key]} onChange={event => change(code, { [key]: event.target.value })} /></label>)}
              <label>Factor semantics · {code}<select required value={draft.rule.factorSemantics || ''} onChange={event => change(code, { factorSemantics: event.target.value })}><option value="">Choose semantics</option><option value="STEP_CONCENTRATION">Per-step concentration</option><option value="CUMULATIVE_CONCENTRATION">Cumulative concentration</option></select></label>
              <label><input type="checkbox" checked={draft.rule.homogeneousMixing} onChange={event => change(code, { homogeneousMixing: event.target.checked })} /> Reviewed homogeneous mixing · {code}</label>
            </>}
            <label>Outward precision · {code}<input required type="number" min="0" max="18" step="1" value={draft.rule.decimalPlaces} onChange={event => change(code, { decimalPlaces: event.target.value === '' ? -1 : Number(event.target.value) })} /></label>
            <label>Evidence reference · {code}<input required value={draft.rule.evidenceRef} onChange={event => change(code, { evidenceRef: event.target.value })} /></label>
            <label>Applicability statement · {code}<textarea required value={draft.rule.applicabilityStatement} onChange={event => change(code, { applicabilityStatement: event.target.value })} /></label>
          </details>
          {companyId && <label><input type="checkbox" checked={draft.adopt} disabled={settingTips.length > 1} onChange={event => {
            setDrafts(value => ({ ...value, [code]: { ...value[code], adopt: event.target.checked } })); setConfirmed(false);
          }} /> Explicitly adopt this complete company override in same publication · {code}</label>}
          <button type="button" className="secondary" onClick={() => { setDrafts(value => Object.fromEntries(Object.entries(value).filter(([key]) => key !== code))); setConfirmed(false); }}>Cancel matrix edit · {code}</button>
        </fieldset>}
      </section>;
  }
  return <section aria-label="Focused scientific policy editor">
    <button type="button" className="secondary" disabled={disabled || action.busy} onClick={() => setOpen(true)}>{Object.keys(drafts).length ? `Resume selected profile edits (${Object.keys(drafts).length})` : 'Edit selected profiles'}</button>
    {!open && <Feedback {...action} />}
    {open && <ResidueDialog title="Edit selected profiles" busy={action.busy} onClose={() => setOpen(false)}>
    <p>Select exact existing profiles, edit only reviewed values, then publish changed profiles together under one review. No coefficients, new identities or adoption inferred.</p>
    <div className="residue-toolbar">
      <label>Find profile to edit<input type="search" value={search} onChange={event => setSearch(event.target.value)} /></label>
      <label>Editor task<select value={task} onChange={event => setTask(event.target.value)}><option value="">All tasks</option><option value="INTERPRETATION">Interpretation</option>{Array.from(new Set(current.filter(rule => rule.kind === 'PROCESS').map(rule => rule.process))).sort().map(process => <option key={process}>{process}</option>)}</select></label>
    </div>
    <form onSubmit={publish}>
      <fieldset disabled={action.busy || stale || disabled}><legend>{companyId ? 'Own-company scientific policy' : 'Administrator system scientific policy'}</legend>
        <div className="residue-profile-list">{filteredCodes.map(profile)}</div>
        {!codes.length && <p className="empty">No reviewed profiles available. Use Create rule for unmatched scientific applicability.</p>}
        {!!codes.length && !filteredCodes.length && <p className="empty">No profiles match. Change filters or use Create rule for unmatched applicability.</p>}
        {pending && <fieldset><legend>One completed review · {changed.length} changed complete rules · {adoptions.length} explicit adoption settings</legend>
          <p>Unedited scientific profiles stay unchanged, including profiles selected only for adoption. Review complete applicability and evidence for every changed or explicitly adopted profile; publication does not establish operational scientific validity.</p>
          <div className="ledger-form-grid">
            <label>Matrix effective from<input required type="datetime-local" value={review.effectiveFrom} onChange={event => { setReview(value => ({ ...value, effectiveFrom: event.target.value })); setConfirmed(false); }} /></label>
            <label>Matrix effective until (optional)<input type="datetime-local" value={review.effectiveTo} onChange={event => { setReview(value => ({ ...value, effectiveTo: event.target.value })); setConfirmed(false); }} /></label>
            <label>Matrix scientific reviewer<input required value={review.reviewedBy} onChange={event => { setReview(value => ({ ...value, reviewedBy: event.target.value })); setConfirmed(false); }} /></label>
            <label>Matrix review authority<input required value={review.reviewAuthority} onChange={event => { setReview(value => ({ ...value, reviewAuthority: event.target.value })); setConfirmed(false); }} /></label>
            <label>Matrix reviewed at<input required type="datetime-local" value={review.reviewedAt} onChange={event => { setReview(value => ({ ...value, reviewedAt: event.target.value })); setConfirmed(false); }} /></label>
          </div>
          <label><input required type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} /> I reviewed all changed complete profiles, evidence, effective period and explicitly selected adoption settings.</label>
          <button disabled={!confirmed}>{action.busy ? 'Publishing…' : changed.length ? 'Publish changed matrix rules' : 'Publish matrix adoption settings'}</button>
        </fieldset>}
      </fieldset>
    </form>
    {stale && <button type="button" className="secondary" onClick={discard}>Discard matrix draft and load current rules</button>}
    <Feedback {...action} />
    </ResidueDialog>}
  </section>;
}
