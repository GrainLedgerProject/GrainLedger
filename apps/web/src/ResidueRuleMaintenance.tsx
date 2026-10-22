import { useEffect, useState, type FormEvent } from 'react';
import { api, errorMessage } from './api';
import { Feedback, useAction } from './ledger-ui';

type Rule = {
  id: string; code: string; versionNo: number; kind: string; substanceId: string; commodityId: string; residueDefinition: string;
  inputStage: string; outputStage: string; inputBasis: string; outputBasis: string; outputFraction: string; process: string; conditions: string;
  analyticalMethod?: string | null; unit: string; decimalPlaces: number; interpretationKind: string | null; ndThresholdKind: string | null;
  factorLower: string | null; factorUpper: string | null; factorSemantics: string | null; homogeneousMixing: boolean;
  effectiveFrom: string; effectiveTo: string | null; supersedesId: string | null; evidenceRef: string; applicabilityStatement: string;
  reviewedBy: string; reviewAuthority: string; reviewedAt: string; publishedBy: string; publishedAt: string;
};
type RuleForm = Omit<Rule, 'publishedBy' | 'publishedAt' | 'decimalPlaces'> & { decimalPlaces: number | null };
type Substance = { id: string; code: string; name: string };
type Commodity = { id: string; code: string; name: string };
const blank = (): RuleForm => ({ id: crypto.randomUUID(), code: '', versionNo: 1, kind: '', substanceId: '', commodityId: '', residueDefinition: '', inputStage: '', outputStage: '', inputBasis: '', outputBasis: '', outputFraction: '', process: '', conditions: '', analyticalMethod: null, unit: '', decimalPlaces: null, interpretationKind: null, ndThresholdKind: null, factorLower: null, factorUpper: null, factorSemantics: null, homogeneousMixing: false, effectiveFrom: '', effectiveTo: null, supersedesId: null, evidenceRef: '', applicabilityStatement: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
const dateValue = (value: string) => value ? new Date(value).toISOString() : '';

export function ResidueRuleMaintenance({ active, allowed }: { active: boolean; allowed: boolean }) {
  const [rules, setRules] = useState<Rule[]>([]), [substances, setSubstances] = useState<Substance[]>([]), [commodities, setCommodities] = useState<Commodity[]>([]);
  const [form, setForm] = useState<RuleForm>(blank), [error, setError] = useState(''), [loading, setLoading] = useState(false);
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
  }, [active, allowed]);

  async function publish(event: FormEvent) {
    event.preventDefault();
    if (!allowed) return;
    await action.run(async () => {
      if (form.decimalPlaces === null) throw new Error('Set reviewed outward precision before publishing.');
      const body = { ...form, analyticalMethod: form.analyticalMethod || '', effectiveFrom: dateValue(form.effectiveFrom), effectiveTo: form.effectiveTo ? dateValue(form.effectiveTo) : null, reviewedAt: dateValue(form.reviewedAt) };
      await api('/api/residue-rules', { method: 'POST', body: JSON.stringify(body) });
      setRules((await api<{ items: Rule[] }>('/api/residue-rules')).items);
      setForm(blank());
    }, 'Immutable residue rule version published.');
  }
  const set = <K extends keyof RuleForm>(key: K, value: RuleForm[K]) => setForm(current => ({ ...current, [key]: value }));
  return <section className="panel" aria-labelledby="residue-rules-title">
    <h2 id="residue-rules-title">F12 residue rules</h2>
    <p>Versioned, reviewed interpretation and process rules. Numeric values have no defaults; publishing does not certify scientific validity.</p>
    {loading && <p role="status">Loading published residue rules…</p>}
    {error && <p className="notice error" role="alert">{error}</p>}
    {rules.length > 0 && <details><summary>Published rules ({rules.length})</summary><ul>{rules.map(rule => <li key={rule.id}>{rule.code} v{rule.versionNo} · {rule.kind} · {rule.substanceId} · effective {rule.effectiveFrom}{rule.effectiveTo ? ` to ${rule.effectiveTo}` : ' onward'} · {rule.reviewAuthority}</li>)}</ul></details>}
    <Feedback {...action} />
    <form onSubmit={publish}>
      <fieldset disabled={action.busy}><legend>Publish reviewed rule version</legend>
        <label>Rule code<input required value={form.code} onChange={e => set('code', e.target.value)} /></label>
        <label>Version number<input required type="number" min="1" step="1" value={form.versionNo} onChange={e => set('versionNo', Number(e.target.value))} /></label>
        <label>Rule kind<select required value={form.kind} onChange={e => set('kind', e.target.value)}><option value="">Choose kind</option><option>INTERPRETATION</option><option>PROCESS</option></select></label>
        <label>Substance<select required value={form.substanceId} onChange={e => set('substanceId', e.target.value)}><option value="">Choose substance</option>{substances.map(s => <option key={s.id} value={s.id}>{s.code} · {s.name}</option>)}</select></label>
        <label>Commodity<select required value={form.commodityId} onChange={e => set('commodityId', e.target.value)}><option value="">Choose commodity</option>{commodities.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label>
        <label>Residue definition<input required value={form.residueDefinition} onChange={e => set('residueDefinition', e.target.value)} /></label>
        <label>Interpretation<select required={form.kind === 'INTERPRETATION'} value={form.interpretationKind || ''} onChange={e => set('interpretationKind', e.target.value || null)}><option value="">Choose interpretation when applicable</option><option>NUMERIC_ABSOLUTE_UNCERTAINTY</option><option>BELOW_LIMIT</option><option>ND</option></select></label>
        <label>Analytical method (required for interpretation)<input required={form.kind === 'INTERPRETATION'} value={form.analyticalMethod || ''} onChange={e => set('analyticalMethod', e.target.value || null)} /></label>
        <label>ND threshold basis<select required={form.interpretationKind === 'ND'} value={form.ndThresholdKind || ''} onChange={e => set('ndThresholdKind', e.target.value || null)}><option value="">Choose only when applicable</option><option>REPORTING_THRESHOLD</option><option>LOD</option><option>LOQ</option></select></label>
        <label>Input stage<input required value={form.inputStage} onChange={e => set('inputStage', e.target.value)} /></label><label>Output stage<input required value={form.outputStage} onChange={e => set('outputStage', e.target.value)} /></label>
        <label>Input basis<input required value={form.inputBasis} onChange={e => set('inputBasis', e.target.value)} /></label><label>Output basis<input required value={form.outputBasis} onChange={e => set('outputBasis', e.target.value)} /></label>
        <label>Output fraction<input required={form.kind === 'PROCESS'} value={form.outputFraction} onChange={e => set('outputFraction', e.target.value)} /></label><label>Process<input required={form.kind === 'PROCESS'} value={form.process} onChange={e => set('process', e.target.value)} /></label>
        <label>Conditions<input required={form.kind === 'PROCESS'} value={form.conditions} onChange={e => set('conditions', e.target.value)} /></label>
        <label>Factor lower bound (decimal)<input required={form.kind === 'PROCESS'} inputMode="decimal" value={form.factorLower || ''} onChange={e => set('factorLower', e.target.value || null)} /></label>
        <label>Factor upper bound (decimal)<input required={form.kind === 'PROCESS'} inputMode="decimal" value={form.factorUpper || ''} onChange={e => set('factorUpper', e.target.value || null)} /></label>
        <label>Factor semantics<select required={form.kind === 'PROCESS'} value={form.factorSemantics || ''} onChange={e => set('factorSemantics', e.target.value || null)}><option value="">Choose only for process rule</option><option>STEP_CONCENTRATION</option><option>CUMULATIVE_CONCENTRATION</option></select></label>
        <label>Unit<select required value={form.unit} onChange={e => set('unit', e.target.value)}><option value="">Choose concentration unit</option><option>mg/kg</option></select></label>
        <label>Outward display decimal places<input required type="number" min="0" max="18" step="1" value={form.decimalPlaces ?? ''} onChange={e => set('decimalPlaces', e.target.value === '' ? null : Number(e.target.value))} /></label>
        <label><input type="checkbox" checked={form.homogeneousMixing} onChange={e => set('homogeneousMixing', e.target.checked)} /> Reviewed homogeneous mixing applicability</label>
        <label>Effective from<input required type="datetime-local" value={form.effectiveFrom} onChange={e => set('effectiveFrom', e.target.value)} /></label>
        <label>Effective until (optional)<input type="datetime-local" value={form.effectiveTo || ''} onChange={e => set('effectiveTo', e.target.value || null)} /></label>
        <label>Superseded rule ID (optional)<input value={form.supersedesId || ''} onChange={e => set('supersedesId', e.target.value || null)} /></label>
        <label>Evidence reference<input required value={form.evidenceRef} onChange={e => set('evidenceRef', e.target.value)} /></label>
        <label>Applicability statement<textarea required value={form.applicabilityStatement} onChange={e => set('applicabilityStatement', e.target.value)} /></label>
        <label>Scientific reviewer<input required value={form.reviewedBy} onChange={e => set('reviewedBy', e.target.value)} /></label>
        <label>Review authority<input required value={form.reviewAuthority} onChange={e => set('reviewAuthority', e.target.value)} /></label>
        <label>Reviewed at<input required type="datetime-local" value={form.reviewedAt} onChange={e => set('reviewedAt', e.target.value)} /></label>
        <button disabled={action.busy}>{action.busy ? 'Publishing…' : 'Publish immutable rule version'}</button>
      </fieldset>
    </form>
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
