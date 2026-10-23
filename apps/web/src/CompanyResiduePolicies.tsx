import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { Feedback, useAction } from './ledger-ui';
import { ResidueRuleMaintenance } from './ResidueRuleMaintenance';
type Rule = { id: string; code: string; versionNo: number; companyId?: string | null; substanceId: string; kind: string; interpretationKind: string | null; inputStage: string; inputBasis: string; residueDefinition: string };
type Setting = { id: string; companyId: string; ruleCode: string; versionNo: number; mode: 'SYSTEM_DEFAULT' | 'COMPANY_OVERRIDE'; effectiveFrom: string; effectiveTo: string | null; supersedesId: string | null; evidenceRef: string; applicabilityStatement: string; reviewedBy: string; reviewAuthority: string; reviewedAt: string; publishedBy?: string; publishedAt?: string };
type Form = Omit<Setting, 'publishedBy' | 'publishedAt'>;
const blank = (companyId: string): Form => ({ id: crypto.randomUUID(), companyId, ruleCode: '', versionNo: 1, mode: 'SYSTEM_DEFAULT', effectiveFrom: '', effectiveTo: null, supersedesId: null, evidenceRef: '', applicabilityStatement: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
const dateValue = (value: string) => new Date(value).toISOString();

export function CompanyResiduePolicies({ active, allowed, companyId }: { active: boolean; allowed: boolean; companyId: string }) {
  const [settings, setSettings] = useState<Setting[]>([]), [rules, setRules] = useState<Rule[]>([]), [form, setForm] = useState<Form>(() => blank(companyId));
  const [loading, setLoading] = useState(false), [error, setError] = useState(''), [reload, setReload] = useState(0);
  const action = useAction();
  useEffect(() => {
    if (!active || !allowed || !companyId) return;
    let current = true;
    setSettings([]); setRules([]); setLoading(true); setError('');
    void Promise.all([api<{ items: Setting[] }>('/api/residue-policy-settings'), api<{ items: Rule[] }>('/api/residue-rules')])
      .then(([settingPage, rulePage]) => { if (current) { setSettings(settingPage.items.filter(item => item.companyId === companyId)); setRules(rulePage.items); } })
      .catch(cause => { if (current) setError(errorMessage(cause)); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [active, allowed, companyId, reload]);
  const eligible = rules.filter(rule => !rule.companyId || rule.companyId === companyId);
  const systemProfileExists = eligible.some(rule => !rule.companyId && rule.code === form.ruleCode);
  const companyOverrideExists = eligible.some(rule => rule.companyId === companyId && rule.code === form.ruleCode);
  function chooseCode(code: string) {
    const prior = settings.filter(item => item.ruleCode === code);
    setForm(current => ({ ...current, id: crypto.randomUUID(), ruleCode: code, versionNo: prior.length ? Math.max(...prior.map(item => item.versionNo)) + 1 : 1, supersedesId: prior.find(item => item.versionNo === Math.max(...prior.map(value => value.versionNo)))?.id || null, effectiveFrom: '', effectiveTo: null, reviewedBy: '', reviewAuthority: '', reviewedAt: '' }));
  }
  async function publish(event: FormEvent) {
    event.preventDefault();
    if (!allowed || !form.ruleCode) return;
    await action.run(async () => {
      const body = { ...form, effectiveFrom: dateValue(form.effectiveFrom), effectiveTo: form.effectiveTo ? dateValue(form.effectiveTo) : null, reviewedAt: dateValue(form.reviewedAt) };
      try { await api('/api/residue-policy-settings', { method: 'POST', body: JSON.stringify(body) }); }
      catch (cause) {
        if (cause instanceof ApiError && cause.status === 409) {
          try {
            const latest = (await api<{ items: Setting[] }>('/api/residue-policy-settings')).items.filter(item => item.companyId === companyId);
            setSettings(latest);
          } catch (refreshCause) { throw new Error(`Policy history changed. Draft remains unchanged; latest history unavailable: ${errorMessage(refreshCause)}`); }
          throw new Error('Policy history changed. Your draft is unchanged. Choose the profile again or start a new setting revision, then review before publishing.');
        }
        throw cause;
      }
      setSettings((await api<{ items: Setting[] }>('/api/residue-policy-settings')).items.filter(item => item.companyId === companyId));
      setForm(blank(companyId));
    }, 'Reviewed company policy setting published.');
  }
  if (!allowed) return <div className="notice error" role="alert">Your account cannot manage company residue policies.</div>;
  return <div className="residue-company-policies">
    <header className="page-heading"><span className="eyebrow">COMPANY RESIDUE POLICY</span><h1>Residue profiles</h1><p className="muted">Adopt reviewed system profiles, publish explicit returns to system defaults, or select company-reviewed overrides. No profile is adopted until a setting is published.</p></header>
    <section className="panel" aria-labelledby="policy-settings-title">
      <h2 id="policy-settings-title">Adoption and override history</h2>
      {loading && <p role="status">Loading residue profiles…</p>}{error && <p className="notice error" role="alert">{error}</p>}
      {settings.length ? <ul>{settings.map(item => <li key={item.id}>{item.ruleCode} · {item.mode === 'SYSTEM_DEFAULT' ? 'System default' : 'Company override'} · v{item.versionNo} · effective {item.effectiveFrom}{item.effectiveTo ? ` to ${item.effectiveTo}` : ' onward'} · reviewed by {item.reviewAuthority} · {item.evidenceRef}</li>)}</ul> : !loading && <p className="empty">No policy settings published. System profiles are not implicitly adopted.</p>}
      <form onSubmit={publish}>
        <fieldset disabled={action.busy}><legend>Publish reviewed profile setting</legend><div className="ledger-form-grid">
          <label>Residue profile<select required value={form.ruleCode} onChange={event => chooseCode(event.target.value)}><option value="">Choose profile</option>{Array.from(new Set(eligible.map(rule => rule.code))).map(code => { const rule = eligible.find(value => value.code === code && !value.companyId) || eligible.find(value => value.code === code)!; return <option key={code} value={code}>{code} · {rule.kind} · {rule.inputStage} · {rule.inputBasis} · {rule.residueDefinition}</option>; })}</select></label>
          <label>Setting mode<select required value={form.mode} onChange={event => setForm(current => ({ ...current, mode: event.target.value as Form['mode'] }))}><option value="SYSTEM_DEFAULT" disabled={!systemProfileExists}>Adopt / return to system default</option><option value="COMPANY_OVERRIDE" disabled={!companyOverrideExists}>Use reviewed company override</option></select></label>
          <button type="button" className="secondary" disabled={!form.ruleCode || action.busy} onClick={() => chooseCode(form.ruleCode)}>Start new setting revision</button>
          {form.mode === 'COMPANY_OVERRIDE' && !companyOverrideExists && <p className="notice pending" role="status">Publish company rule with same Code first. This setting cannot fall back to system rule.</p>}
          {form.mode === 'SYSTEM_DEFAULT' && !systemProfileExists && <p className="notice pending" role="status">System profile unavailable for this Code. Publish as explicit company override instead.</p>}
          <label>Effective from<input required type="datetime-local" value={form.effectiveFrom} onChange={event => setForm(current => ({ ...current, effectiveFrom: event.target.value }))} /></label>
          <label>Effective until (optional)<input type="datetime-local" value={form.effectiveTo || ''} onChange={event => setForm(current => ({ ...current, effectiveTo: event.target.value || null }))} /></label>
          <label>Evidence reference<input required value={form.evidenceRef} onChange={event => setForm(current => ({ ...current, evidenceRef: event.target.value }))} /></label>
          <label>Applicability statement<textarea required value={form.applicabilityStatement} onChange={event => setForm(current => ({ ...current, applicabilityStatement: event.target.value }))} /></label>
          <label>Scientific reviewer<input required value={form.reviewedBy} onChange={event => setForm(current => ({ ...current, reviewedBy: event.target.value }))} /></label>
          <label>Review authority<input required value={form.reviewAuthority} onChange={event => setForm(current => ({ ...current, reviewAuthority: event.target.value }))} /></label>
          <label>Reviewed at<input required type="datetime-local" value={form.reviewedAt} onChange={event => setForm(current => ({ ...current, reviewedAt: event.target.value }))} /></label>
        </div><button disabled={action.busy || !form.ruleCode || (form.mode === 'COMPANY_OVERRIDE' ? !companyOverrideExists : !systemProfileExists)}>{action.busy ? 'Publishing…' : 'Publish reviewed policy setting'}</button></fieldset>
      </form><Feedback {...action} />
    </section>
    <ResidueRuleMaintenance active={active} allowed={allowed} companyId={companyId} onPublished={() => setReload(value => value + 1)} />
  </div>;
}
