import { Fragment, useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { Feedback, kinds, stages, useAction } from './ledger-ui';

type Rule = {
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
  return { id, code: `RES-${id}`, versionNo: 1, companyId, kind, substanceId: '', commodityId: '', residueDefinition: '', inputStage: '', outputStage: '', inputBasis: '', outputBasis: '', outputFraction: '', process: '', conditions: '', analyticalMethod: null, unit: '', decimalPlaces: null, interpretationKind: null, ndThresholdKind: null, factorLower: null, factorUpper: null, factorSemantics: null, homogeneousMixing: false, relativeUncertaintyPercent: null, relativeUncertaintyAtZero: false, effectiveFrom: '', effectiveTo: null, supersedesId: null, evidenceRef: '', applicabilityStatement: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' };
};
const dateValue = (value: string) => value ? new Date(value).toISOString() : '';
const taskNames: Record<string, string> = { INTERPRETATION: 'Interpret laboratory results', PROCESS: 'Define processing factors' };

export function ResidueRuleMaintenance({ active, allowed, companyId = null, onPublished }: { active: boolean; allowed: boolean; companyId?: string | null; onPublished?: () => void }) {
  const [rules, setRules] = useState<Rule[]>([]), [substances, setSubstances] = useState<Substance[]>([]), [commodities, setCommodities] = useState<Commodity[]>([]);
  const [form, setForm] = useState<RuleForm>(blank('', companyId)), [error, setError] = useState(''), [loading, setLoading] = useState(false), [expandedRuleId, setExpandedRuleId] = useState<string | null>(null);
  const action = useAction();
  useEffect(() => {
    if (!active || !allowed) return;
    let current = true;
    setLoading(true); setError('');
    void Promise.all([api<{ items: Rule[] }>('/api/residue-rules'), loadAll<Substance>('/api/active-substances'), loadAll<Commodity>('/api/commodities')])
      .then(([rulePage, substanceItems, commodityItems]) => { if (current) { setRules(rulePage.items); setSubstances(substanceItems); setCommodities(commodityItems); } })
      .catch(cause => { if (current) setError(errorMessage(cause)); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [active, allowed, companyId]);

  async function publish(event: FormEvent) {
    event.preventDefault();
    if (!allowed) return;
    await action.run(async () => {
      if (!stages.includes(form.inputStage) || !stages.includes(form.outputStage)) throw new Error('Choose supported input and output stages before publishing.');
      if (form.decimalPlaces === null) throw new Error('Set reviewed outward precision before publishing.');
      const interpretation = form.kind === 'INTERPRETATION';
      const isNd = interpretation && form.interpretationKind === 'ND';
      const process = form.kind === 'PROCESS';
      const body = {
        ...form,
        analyticalMethod: interpretation ? form.analyticalMethod || '' : null,
        interpretationKind: interpretation ? form.interpretationKind : null,
        ndThresholdKind: isNd ? form.ndThresholdKind : null,
        factorLower: process ? form.factorLower : null,
        factorUpper: process ? form.factorUpper : null,
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
          throw new Error('Published rule history changed. Your draft is unchanged. Review the latest published rule in history and explicitly choose Revise rule to start a fresh version.');
        }
        throw cause;
      }
      setRules((await api<{ items: Rule[] }>('/api/residue-rules')).items);
      setForm(blank('', companyId));
      onPublished?.();
    }, 'Reviewed residue rule published.');
  }
  function set<K extends keyof RuleForm>(key: K, value: RuleForm[K]) { setForm(current => ({ ...current, [key]: value })); }
  function changeKind(kind: string) {
    setForm(current => {
      const stageMismatch = current.inputStage !== current.outputStage, basisMismatch = current.inputBasis !== current.outputBasis;
      return { ...current, kind, interpretationKind: null, ndThresholdKind: null, analyticalMethod: null,
        outputFraction: '', process: '', conditions: '', factorLower: null, factorUpper: null, factorSemantics: null, homogeneousMixing: false,
        inputStage: kind === 'INTERPRETATION' && stageMismatch ? '' : current.inputStage,
        outputStage: kind === 'INTERPRETATION' && stageMismatch ? '' : current.outputStage,
        inputBasis: kind === 'INTERPRETATION' && basisMismatch ? '' : current.inputBasis,
        outputBasis: kind === 'INTERPRETATION' && basisMismatch ? '' : current.outputBasis };
    });
  }
  function changeInterpretation(kind: string) {
    setForm(current => ({ ...current, interpretationKind: kind || null, ndThresholdKind: null }));
  }
  function startRevision(rule: Rule) {
    const series = rules.filter(item => item.companyId === (companyId || null) && item.code === rule.code);
    const nextVersion = Math.max(...series.map(item => item.versionNo)) + 1;
    action.setError('');
    action.setStatus('');
    setForm({ ...rule, id: crypto.randomUUID(), companyId, versionNo: nextVersion, supersedesId: rule.id, effectiveFrom: '', effectiveTo: null, reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
  }
  function cloneSystemRule(rule: Rule) {
    action.setError('');
    action.setStatus('');
    setForm({ ...rule, id: crypto.randomUUID(), companyId, versionNo: 1, supersedesId: null, effectiveFrom: '', effectiveTo: null, reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
  }
  const substanceName = (id: string) => { const item = substances.find(value => value.id === id); return item ? `${item.code} · ${item.name}` : id; };
  const commodityName = (id: string) => { const item = commodities.find(value => value.id === id); return item ? `${item.code} · ${item.name}` : id; };
  const interpretation = form.kind === 'INTERPRETATION', process = form.kind === 'PROCESS';
  const revising = form.supersedesId !== null;
  const visibleRules = rules.filter(rule => rule.companyId === (companyId || null) || (companyId && !rule.companyId));
  const successors = new Map(rules.filter(rule => rule.supersedesId).map(rule => [`${rule.companyId || ''}:${rule.supersedesId}`, rule] as const));
  const companyCodes = new Set(rules.filter(rule => rule.companyId === companyId).map(rule => rule.code));
  return <section className="panel" aria-labelledby="residue-rules-title">
    <h2 id="residue-rules-title">{companyId ? 'Company residue rules' : 'System residue rules'}</h2>
    <p>{companyId ? 'Company rules use same-Code system series, retain company scope and require fresh review.' : 'Versioned, reviewed interpretation and process rules. Numeric values have no defaults; publishing does not certify scientific validity.'}</p>
    {loading && <p role="status">Loading published residue rules…</p>}
    {error && <p className="notice error" role="alert">{error}</p>}
    {visibleRules.length > 0 && <div className="table-scroll residue-rule-table-scroll" role="region" aria-label="Published residue rules" tabIndex={0}>
      <table className="residue-rule-table">
        <caption>Published rules ({visibleRules.length})</caption>
        <thead><tr><th scope="col">Rule</th><th scope="col">Applies to</th><th scope="col">Policy</th><th scope="col">Effective period</th><th scope="col">Version status</th><th scope="col">Actions</th></tr></thead>
        <tbody>{visibleRules.map(rule => {
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
          const status = successor ? 'Superseded' : 'Latest version';
          const detailId = `residue-rule-details-${rule.id}`;
          return <Fragment key={rule.id}>
            <tr>
              <td><div className="residue-rule-badges"><span className="badge">{rule.companyId ? 'Company' : 'System'}</span><span className="badge">v{rule.versionNo}</span></div><strong className="residue-rule-kind">{rule.kind === 'INTERPRETATION' ? 'Interpretation' : 'Processing'}</strong><span className="table-subtitle residue-rule-code">{rule.code}</span></td>
              <td className="residue-rule-applies">{substanceName(rule.substanceId)}<span className="table-subtitle">{commodityName(rule.commodityId)} · {rule.inputStage}{rule.outputStage !== rule.inputStage ? ` → ${rule.outputStage}` : ''}</span></td>
              <td>{policy}<span className="table-subtitle">{policyDetail}</span></td>
              <td><time dateTime={rule.effectiveFrom}>{new Date(rule.effectiveFrom).toLocaleDateString(undefined, { timeZone: 'UTC', dateStyle: 'medium' })}</time><span className="table-subtitle">{rule.effectiveTo ? <>Until <time dateTime={rule.effectiveTo}>{new Date(rule.effectiveTo).toLocaleDateString(undefined, { timeZone: 'UTC', dateStyle: 'medium' })}</time></> : 'No stated end'}</span></td>
              <td><span className={`badge${successor ? ' inactive' : ''}`}>{status}</span>{successor && <span id={`superseded-${rule.id}`} className="table-subtitle">Already superseded by {successor.code} v{successor.versionNo}; revise latest version instead.</span>}</td>
              <td className="admin-row-actions"><div className="residue-rule-actions">
                <button type="button" className="secondary" aria-expanded={expandedRuleId === rule.id} aria-controls={detailId} aria-label={`Details for ${rule.code} v${rule.versionNo} · ${rule.companyId ? 'Company' : 'System'}`} onClick={() => setExpandedRuleId(current => current === rule.id ? null : rule.id)}>Details</button>
                {companyId && !rule.companyId && !companyCodes.has(rule.code) && <button type="button" className="secondary" disabled={action.busy} aria-label={`Clone as company rule · ${substanceName(rule.substanceId)} · ${commodityName(rule.commodityId)} · ${rule.code} v${rule.versionNo}`} onClick={() => cloneSystemRule(rule)}>Clone as company rule</button>}
                {rule.companyId === (companyId || null) && <button type="button" className="secondary" disabled={action.busy || successor !== undefined} aria-label={`Revise rule · ${taskNames[rule.kind]} · ${substanceName(rule.substanceId)} · ${commodityName(rule.commodityId)} · ${summary} · v${rule.versionNo}`} aria-describedby={successor ? `superseded-${rule.id}` : undefined} onClick={() => startRevision(rule)}>Revise rule</button>}
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
    {!loading && !error && visibleRules.length === 0 && <p className="empty">No published residue rules in this scope.</p>}
    <Feedback {...action} />
    {!form.kind ? <div aria-label="Choose residue rule task">
      <h3>New rule</h3>
      <div className="ledger-actions">
        <button type="button" onClick={() => setForm(blank('INTERPRETATION', companyId))}>Interpret laboratory results</button>
        <button type="button" onClick={() => setForm(blank('PROCESS', companyId))}>Define processing factors</button>
      </div>
    </div> : <>
      {revising && <p role="status">Revising {taskNames[form.kind]} for {substanceName(form.substanceId)} · {commodityName(form.commodityId)}. Previous publication remains unchanged until new rule is reviewed and published.</p>}
      <button type="button" onClick={() => { action.setError(''); action.setStatus(''); setForm(blank('', companyId)); }}>{revising ? 'Cancel revision' : 'Create new rule'}</button>
      <form onSubmit={publish}>
        <datalist id="residue-rule-processes">{kinds.map(kind => <option key={kind} value={kind} />)}</datalist>
        <datalist id="residue-rule-conditions"><option value="UNCONDITIONAL" label="Always applies; no additional conditions" /></datalist>
        <fieldset disabled={action.busy}><legend>{taskNames[form.kind]}</legend>
          <fieldset><legend>Applicability</legend>
            <div className="ledger-form-grid">
            {revising ? <p>Series: {taskNames[form.kind]} · {substanceName(form.substanceId)} · {commodityName(form.commodityId)}. Create new rule to change series identity.</p> : <>
              <label>Substance<select required value={form.substanceId} onChange={e => set('substanceId', e.target.value)}><option value="">Choose substance</option>{substances.map(s => <option key={s.id} value={s.id}>{s.code} · {s.name}</option>)}</select></label>
              <label>Commodity<select required value={form.commodityId} onChange={e => set('commodityId', e.target.value)}><option value="">Choose commodity</option>{commodities.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label>
            </>}
            <label>Residue definition<input required value={form.residueDefinition} onChange={e => set('residueDefinition', e.target.value)} /></label>
            {interpretation ? <>
              <label>Material stage<select required value={stages.includes(form.inputStage) && form.inputStage === form.outputStage ? form.inputStage : ''} onChange={e => setForm(current => ({ ...current, inputStage: e.target.value, outputStage: e.target.value }))}><option value="">Choose material stage</option>{stages.map(stage => <option key={stage} value={stage}>{stage}</option>)}</select></label>
              <label>Analytical basis<input required value={form.inputBasis} onChange={e => setForm(current => ({ ...current, inputBasis: e.target.value, outputBasis: e.target.value }))} /></label>
            </> : <>
              <label>Input stage<select required value={stages.includes(form.inputStage) ? form.inputStage : ''} onChange={e => set('inputStage', e.target.value)}><option value="">Choose input stage</option>{stages.map(stage => <option key={stage} value={stage}>{stage}</option>)}</select></label><label>Output stage<select required value={stages.includes(form.outputStage) ? form.outputStage : ''} onChange={e => set('outputStage', e.target.value)}><option value="">Choose output stage</option>{stages.map(stage => <option key={stage} value={stage}>{stage}</option>)}</select></label>
              <label>Input analytical basis<input required value={form.inputBasis} onChange={e => set('inputBasis', e.target.value)} /></label><label>Output analytical basis<input required value={form.outputBasis} onChange={e => set('outputBasis', e.target.value)} /></label>
            </>}
            </div>
          </fieldset>
          <fieldset><legend>Scientific rule</legend>
            <div className="ledger-form-grid">
            <label>Concentration unit<select required value={form.unit} onChange={e => set('unit', e.target.value)}><option value="">Choose concentration unit</option><option>mg/kg</option></select></label>
            <label>Reviewed outward precision (decimal places)<input required type="number" min="0" max="18" step="1" value={form.decimalPlaces ?? ''} onChange={e => set('decimalPlaces', e.target.value === '' ? null : Number(e.target.value))} /></label>
            {interpretation ? <>
              <label>Interpretation<select required value={form.interpretationKind || ''} onChange={e => changeInterpretation(e.target.value)}><option value="">Choose interpretation</option><option value="NUMERIC_ABSOLUTE_UNCERTAINTY">Numeric result with absolute uncertainty</option><option value="NUMERIC_RELATIVE_UNCERTAINTY">Numeric result with relative uncertainty (%)</option><option value="BELOW_LIMIT">Below analytical limit</option><option value="ND">Not detected (ND)</option></select></label>
              <label>Analytical method<input required value={form.analyticalMethod || ''} onChange={e => set('analyticalMethod', e.target.value || null)} /></label>
              {form.interpretationKind === 'ND' && <label>ND threshold policy<select required value={form.ndThresholdKind || ''} onChange={e => set('ndThresholdKind', e.target.value || null)}><option value="">Choose reviewed threshold policy</option><option value="REPORTING_THRESHOLD">Reporting threshold</option><option value="LOD">Limit of detection (LOD)</option><option value="LOQ">Limit of quantification (LOQ)</option></select></label>}
              {form.interpretationKind === 'NUMERIC_RELATIVE_UNCERTAINTY' && <>
                <label>Relative uncertainty (%)<input required inputMode="decimal" value={form.relativeUncertaintyPercent || ''} onChange={e => set('relativeUncertaintyPercent', e.target.value || null)} /></label>
                <label><input type="checkbox" checked={form.relativeUncertaintyAtZero || false} onChange={e => set('relativeUncertaintyAtZero', e.target.checked)} /> Reviewed relative uncertainty at numeric zero</label>
              </>}
            </> : <>
              <label>Output fraction / product identity<input required value={form.outputFraction} onChange={e => set('outputFraction', e.target.value)} /></label>
              <label>Process<input required list="residue-rule-processes" value={form.process} onChange={e => set('process', e.target.value)} /></label>
              <label>Conditions or UNCONDITIONAL applicability<input required list="residue-rule-conditions" value={form.conditions} onChange={e => set('conditions', e.target.value)} /></label>
              <label>Factor lower bound (decimal)<input required inputMode="decimal" value={form.factorLower || ''} onChange={e => set('factorLower', e.target.value || null)} /></label>
              <label>Factor upper bound (decimal)<input required inputMode="decimal" value={form.factorUpper || ''} onChange={e => set('factorUpper', e.target.value || null)} /></label>
              <label>Factor semantics<select required value={form.factorSemantics || ''} onChange={e => set('factorSemantics', e.target.value || null)}><option value="">Choose factor semantics</option><option value="STEP_CONCENTRATION">Per-step concentration factor</option><option value="CUMULATIVE_CONCENTRATION">Cumulative concentration factor</option></select></label>
              <label><input type="checkbox" checked={form.homogeneousMixing} onChange={e => set('homogeneousMixing', e.target.checked)} /> Reviewed homogeneous mixing applicability</label>
            </>}
            </div>
          </fieldset>
          <fieldset><legend>Review</legend>
            <div className="ledger-form-grid">
            <label>Effective from<input required type="datetime-local" value={form.effectiveFrom} onChange={e => set('effectiveFrom', e.target.value)} /></label>
            <label>Effective until (optional)<input type="datetime-local" value={form.effectiveTo || ''} onChange={e => set('effectiveTo', e.target.value || null)} /></label>
            <label>Evidence reference<input required value={form.evidenceRef} onChange={e => set('evidenceRef', e.target.value)} /></label>
            <label>Applicability statement<textarea required value={form.applicabilityStatement} onChange={e => set('applicabilityStatement', e.target.value)} /></label>
            <label>Scientific reviewer<input required value={form.reviewedBy} onChange={e => set('reviewedBy', e.target.value)} /></label>
            <label>Review authority<input required value={form.reviewAuthority} onChange={e => set('reviewAuthority', e.target.value)} /></label>
            <label>Reviewed at<input required type="datetime-local" value={form.reviewedAt} onChange={e => set('reviewedAt', e.target.value)} /></label>
            </div>
          </fieldset>
          <button disabled={action.busy}>{action.busy ? 'Publishing…' : companyId ? 'Publish reviewed company rule' : 'Publish reviewed rule'}</button>
        </fieldset>
      </form>
    </>}
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
