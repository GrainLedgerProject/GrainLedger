import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { Feedback, useAction } from './ledger-ui';
import { ResidueRuleMaintenance, type Rule } from './ResidueRuleMaintenance';
import { effectiveWindowLabel } from './residueProfiles';

type Setting = { id: string; companyId: string; ruleCode: string; versionNo: number; mode: 'SYSTEM_DEFAULT' | 'COMPANY_OVERRIDE'; effectiveFrom: string; effectiveTo: string | null; supersedesId: string | null; evidenceRef: string; applicabilityStatement: string; reviewedBy: string; reviewAuthority: string; reviewedAt: string; publishedBy?: string; publishedAt?: string };
type Draft = Pick<Setting, 'id' | 'ruleCode' | 'versionNo' | 'supersedesId' | 'evidenceRef' | 'applicabilityStatement'> & { sourceRuleId: string | null };
type CatalogItem = { id: string; code: string; name: string };
const blankReview = () => ({ effectiveFrom: '', effectiveTo: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
const dateValue = (value: string) => new Date(value).toISOString();

export function CompanyResiduePolicies({ active, allowed, companyId }: { active: boolean; allowed: boolean; companyId: string }) {
  const [settings, setSettings] = useState<Setting[]>([]), [rules, setRules] = useState<Rule[]>([]);
  const [substances, setSubstances] = useState<CatalogItem[]>([]), [commodities, setCommodities] = useState<CatalogItem[]>([]);
  const [drafts, setDrafts] = useState<Draft[]>([]), [mode, setMode] = useState<Setting['mode']>('SYSTEM_DEFAULT');
  const [review, setReview] = useState(blankReview), [confirmed, setConfirmed] = useState(false);
  const [loading, setLoading] = useState(false), [error, setError] = useState(''), [reload, setReload] = useState(0);
  const action = useAction();
  const [profileSearch, setProfileSearch] = useState(''), [profileTask, setProfileTask] = useState(''), [profileStatus, setProfileStatus] = useState('');
  const [historySearch, setHistorySearch] = useState(''), [historyMode, setHistoryMode] = useState(''), [historyStatus, setHistoryStatus] = useState(''), [historySort, setHistorySort] = useState('code');
  useEffect(() => { setDrafts([]); setReview(blankReview()); setConfirmed(false); }, [companyId]);
  useEffect(() => {
    if (!active || !allowed || !companyId) return;
    let current = true;
    setLoading(true); setError('');
    void Promise.all([api<{ items: Setting[] }>('/api/residue-policy-settings'), api<{ items: Rule[] }>('/api/residue-rules'), loadCatalog('/api/active-substances'), loadCatalog('/api/commodities')])
      .then(([settingPage, rulePage, substanceItems, commodityItems]) => { if (current) { setSettings(settingPage.items.filter(item => item.companyId === companyId)); setRules(rulePage.items); setSubstances(substanceItems); setCommodities(commodityItems); setConfirmed(false); } })
      .catch(cause => { if (current) setError(errorMessage(cause)); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [active, allowed, companyId, reload]);
  const eligible = rules.filter(rule => !rule.companyId || rule.companyId === companyId);
  const codes = Array.from(new Set(eligible.map(rule => rule.code)));
  const source = (code: string, selectedMode = mode) => {
    const series = eligible.filter(rule => rule.code === code && (selectedMode === 'SYSTEM_DEFAULT' ? !rule.companyId : rule.companyId === companyId));
    const current = series.filter(rule => !series.some(next => next.supersedesId === rule.id));
    return current.length === 1 ? current[0] : undefined;
  };
  const sourceChanged = drafts.some(item => item.sourceRuleId !== (source(item.ruleCode)?.id || null));
  const name = (items: CatalogItem[], id: string) => { const item = items.find(value => value.id === id); return item ? `${item.code} · ${item.name}` : id; };
  const label = (code: string) => {
    const rule = source(code) || eligible.find(item => item.code === code);
    if (!rule) return code;
    return `${name(substances, rule.substanceId)} · ${name(commodities, rule.commodityId)} · ${rule.kind === 'INTERPRETATION' ? rule.interpretationKind : rule.process} · ${rule.residueDefinition} · ${rule.inputStage}/${rule.inputBasis} → ${rule.outputStage}/${rule.outputBasis} · ${rule.outputFraction || 'No output fraction'} · ${rule.analyticalMethod || 'No analytical method'} · ${rule.conditions || 'No conditions'} · ${rule.factorSemantics || ''} · ${code}`;
  };
  const filteredCodes = codes.filter(code => {
    const rule = source(code) || eligible.find(item => item.code === code)!;
    const tips = settings.filter(item => item.ruleCode === code && !settings.some(next => next.supersedesId === item.id));
    return (!profileSearch.trim() || label(code).toLowerCase().includes(profileSearch.trim().toLowerCase())) &&
      (!profileTask || (profileTask === 'INTERPRETATION' ? rule.kind === profileTask : rule.process === profileTask)) &&
      (!profileStatus || (profileStatus === 'selected' ? drafts.some(item => item.ruleCode === code) : profileStatus === 'unavailable' ? !source(code) : profileStatus === 'unadopted' ? tips.length === 0 : tips.some(item => item.mode === profileStatus)));
  }).sort((a, b) => label(a).localeCompare(label(b)));
  const settingGroups = Array.from(new Set(settings.map(item => item.ruleCode))).map(code => {
    const versions = settings.filter(item => item.ruleCode === code).sort((a, b) => b.versionNo - a.versionNo);
    const predecessors = new Set(versions.map(item => item.supersedesId));
    return { code, tips: versions.filter(item => !predecessors.has(item.id)), history: versions.filter(item => predecessors.has(item.id)) };
  });
  const historyMatches = (item: Setting) => (!historyMode || item.mode === historyMode) &&
    (!historyStatus || effectiveWindowLabel(item) === historyStatus) &&
    `${label(item.ruleCode)} ${Object.values(item).join(' ')}`.toLowerCase().includes(historySearch.trim().toLowerCase());
  const filteredHistory = settingGroups.filter(group => [...group.tips, ...group.history].some(historyMatches)).sort((a, b) => historySort === 'effective'
    ? Math.max(...b.tips.map(item => Date.parse(item.effectiveFrom))) - Math.max(...a.tips.map(item => Date.parse(item.effectiveFrom)))
    : a.code.localeCompare(b.code));
  function settingRecord(item: Setting) {
    return <li key={item.id}><strong>v{item.versionNo} · {item.mode === 'SYSTEM_DEFAULT' ? 'System default' : 'Company override'}</strong> · {effectiveWindowLabel(item)} · effective {item.effectiveFrom}{item.effectiveTo ? ` to ${item.effectiveTo}` : ' onward'} · adoption reviewed by {item.reviewedBy} · {item.reviewAuthority} · {item.reviewedAt} · {item.evidenceRef} · {item.applicabilityStatement}{item.publishedAt && <> · published {item.publishedAt} by {item.publishedBy}</>}</li>;
  }
  function draftFor(code: string, selectedMode = mode): Draft {
    const previous = settings.filter(item => item.ruleCode === code).sort((a, b) => b.versionNo - a.versionNo)[0];
    const rule = source(code, selectedMode);
    return { id: crypto.randomUUID(), ruleCode: code, versionNo: previous ? previous.versionNo + 1 : 1, supersedesId: previous?.id || null, sourceRuleId: rule?.id || null, evidenceRef: rule?.evidenceRef || '', applicabilityStatement: rule?.applicabilityStatement || '' };
  }
  function chooseCodes(selected: string[]) {
    setDrafts(selected.map(code => drafts.find(item => item.ruleCode === code) || draftFor(code)));
    setConfirmed(false);
  }
  function changeMode(value: Setting['mode']) {
    setMode(value); setDrafts(drafts.map(item => draftFor(item.ruleCode, value))); setConfirmed(false);
  }
  function refreshSources() {
    setDrafts(current => current.map(draft => {
      const previous = rules.find(rule => rule.id === draft.sourceRuleId), next = source(draft.ruleCode);
      return { ...draft, sourceRuleId: next?.id || null,
        evidenceRef: previous && draft.evidenceRef === previous.evidenceRef ? next?.evidenceRef || '' : draft.evidenceRef,
        applicabilityStatement: previous && draft.applicabilityStatement === previous.applicabilityStatement ? next?.applicabilityStatement || '' : draft.applicabilityStatement };
    }));
    setReview(blankReview()); setConfirmed(false);
  }
  async function publish(event: FormEvent) {
    event.preventDefault();
    if (!allowed || !drafts.length || !confirmed) return;
    await action.run(async () => {
      if (drafts.some(item => !source(item.ruleCode))) throw new Error('Selected reviewed profile is unavailable for this setting mode. Publish scientific profile first; no fallback is allowed.');
      if (sourceChanged) throw new Error('Selected scientific profile changed. Review updated sources before confirming company adoption.');
      const items = drafts.map(({ sourceRuleId: _sourceRuleId, ...item }) => ({ ...item, ...review, companyId, mode, effectiveFrom: dateValue(review.effectiveFrom), effectiveTo: review.effectiveTo ? dateValue(review.effectiveTo) : null, reviewedAt: dateValue(review.reviewedAt) }));
      try { await api('/api/residue-policy-settings/batch', { method: 'POST', body: JSON.stringify({ items }) }); }
      catch (cause) {
        if (cause instanceof ApiError && cause.status === 409) {
          try { setSettings((await api<{ items: Setting[] }>('/api/residue-policy-settings')).items.filter(item => item.companyId === companyId)); }
          catch (refreshCause) { throw new Error(`Policy history changed. Draft remains unchanged; latest history unavailable: ${errorMessage(refreshCause)}`); }
          throw new Error('Policy history changed. Draft remains unchanged. Start new setting revisions, then review before publishing.');
        }
        throw cause;
      }
      setSettings((await api<{ items: Setting[] }>('/api/residue-policy-settings')).items.filter(item => item.companyId === companyId));
      setDrafts([]); setReview(blankReview()); setConfirmed(false);
    }, 'Reviewed company policy settings published together.');
  }
  if (!allowed) return <div className="notice error" role="alert">Your account cannot manage company residue policies.</div>;
  return <div className="residue-company-policies">
    <header className="page-heading"><span className="eyebrow">COMPANY RESIDUE POLICY</span><h1>Residue profiles</h1><p className="muted">Reuse reviewed scientific profiles. Review company adoption once for selected profiles; no implicit adoption or partial publication.</p></header>
    <ResidueRuleMaintenance active={active} allowed={allowed} companyId={companyId} policySettings={settings} onPublished={() => setReload(value => value + 1)} />
    <section className="panel" aria-labelledby="policy-settings-title">
      <h2 id="policy-settings-title">Use reviewed profiles</h2>
      {loading && <p role="status">Loading residue profiles…</p>}{error && <div className="notice error" role="alert">{error}<button type="button" className="secondary" disabled={loading} onClick={() => setReload(value => value + 1)}>Retry loading adoption settings</button></div>}
      {!settings.length && !loading && <p className="empty">No policy settings published. System profiles are not implicitly adopted.</p>}
      {!codes.length && !loading && !error && <p className="notice pending">No reviewed profile available. Use Create rule for expert setup; missing policy leaves bounds unresolved.</p>}
      {codes.length > 0 && <form onSubmit={publish} onChange={() => setConfirmed(false)}>
        <fieldset disabled={action.busy || loading || !!error}><legend>Choose reusable profiles</legend>
          <label>Setting mode<select value={mode} onChange={event => changeMode(event.target.value as Setting['mode'])}><option value="SYSTEM_DEFAULT">Adopt / return to system default</option><option value="COMPANY_OVERRIDE">Use reviewed company override</option></select></label>
          <div className="residue-toolbar">
            <label>Find profiles to adopt<input type="search" value={profileSearch} onChange={event => setProfileSearch(event.target.value)} /></label>
            <label>Adoption task<select value={profileTask} onChange={event => setProfileTask(event.target.value)}><option value="">All tasks</option><option value="INTERPRETATION">Interpretation</option>{Array.from(new Set(eligible.filter(rule => rule.kind === 'PROCESS').map(rule => rule.process))).sort().map(task => <option key={task}>{task}</option>)}</select></label>
            <label>Adoption status<select value={profileStatus} onChange={event => setProfileStatus(event.target.value)}><option value="">All statuses</option><option value="selected">Selected for review</option><option value="unadopted">No adoption setting</option><option value="SYSTEM_DEFAULT">System default setting</option><option value="COMPANY_OVERRIDE">Company override setting</option><option value="unavailable">Unavailable / conflicting source</option></select></label>
          </div>
          <p>{filteredCodes.length} of {codes.length} profile series shown · {drafts.length} explicitly selected (including selections outside filters). Filters never select or discard profiles.</p>
          {!filteredCodes.length && <p className="empty">No profiles match these filters. Clear search or change task/status.</p>}
          <fieldset aria-label="Residue profiles"><legend>Residue profiles</legend><div className="residue-profile-choices">{filteredCodes.map(code => {
            const selectedSource = source(code), rule = selectedSource || eligible.find(item => item.code === code)!;
            const substance = substances.find(item => item.id === rule.substanceId), commodity = commodities.find(item => item.id === rule.commodityId);
            const stage = (rule.inputStage === rule.outputStage ? [rule.inputStage] : [rule.inputStage, rule.outputStage]).map(value => value.replaceAll('_', ' ').toLowerCase()).join(' → ');
            let approach: string;
            if (rule.kind === 'PROCESS') {
              approach = rule.factorLower === null || rule.factorUpper === null ? `${rule.process} · factor unspecified`
                : `${rule.process} · ${rule.factorLower}${rule.factorUpper === rule.factorLower ? '' : `–${rule.factorUpper}`}×`;
            } else {
              switch (rule.interpretationKind) {
                case 'NUMERIC_RELATIVE_UNCERTAINTY': approach = rule.relativeUncertaintyPercent == null ? 'Numeric · relative uncertainty unspecified' : `Numeric ±${rule.relativeUncertaintyPercent}%`; break;
                case 'NUMERIC_ABSOLUTE_UNCERTAINTY': approach = 'Numeric · absolute uncertainty'; break;
                case 'BELOW_LIMIT': approach = 'Below analytical limit'; break;
                case 'ND': approach = `ND · ${rule.ndThresholdKind || 'threshold policy unspecified'}`; break;
                default: approach = rule.interpretationKind || 'Interpretation unspecified';
              }
            }
            return <label key={code} className="residue-profile-choice">
              <input type="checkbox" checked={drafts.some(item => item.ruleCode === code)} disabled={!selectedSource} onChange={event => chooseCodes(event.target.checked ? [...drafts.map(item => item.ruleCode), code] : drafts.filter(item => item.ruleCode !== code).map(item => item.ruleCode))} />
              <span className="residue-profile-choice-text">
                <strong className="residue-profile-choice-primary">{substance?.name || rule.substanceId} · {stage} · {approach}</strong>{' '}
                <small className="muted residue-profile-choice-secondary">{commodity?.name || rule.commodityId} · {rule.residueDefinition} · {rule.analyticalMethod || 'No analytical method'} · {rule.inputBasis} → {rule.outputBasis} · product {rule.outputFraction || 'not applicable'} · {rule.conditions || 'No conditions'} · {rule.factorSemantics || 'Interpretation'} · {code} · {selectedSource ? `latest published tip v${rule.versionNo} · ${effectiveWindowLabel(rule)}` : 'Reviewed profile unavailable in this mode; metadata is not a selected source'}</small>
              </span>
            </label>;
          })}</div></fieldset>
          <p>Select one or more profiles. Adoption applies to each whole series, not a copied or pinned scientific rule.</p>
          {codes.filter(code => !source(code)).length > 0 && <details><summary>Unavailable or conflicting profile sources</summary>{codes.filter(code => !source(code)).map(code => <section key={code}><h3>{code}</h3>{eligible.filter(rule => rule.code === code && (mode === 'SYSTEM_DEFAULT' ? !rule.companyId : rule.companyId === companyId) && !eligible.some(next => next.supersedesId === rule.id && (next.companyId || null) === (rule.companyId || null))).map(rule => <p key={rule.id}>Conflicting applicability / unavailable unique source · {rule.code} v{rule.versionNo} · {rule.inputStage}/{rule.inputBasis} → {rule.outputStage}/{rule.outputBasis} · {rule.outputFraction || rule.analyticalMethod} · {rule.evidenceRef}</p>)}</section>)}</details>}
          {drafts.length > 0 && <>
            <button type="button" className="secondary" disabled={sourceChanged} onClick={() => { setDrafts(drafts.map(item => draftFor(item.ruleCode))); setReview(blankReview()); setConfirmed(false); }}>Start new setting revisions</button>
            {sourceChanged && <div className="notice pending" role="status">
              <p>Selected scientific profile changed. Draft text is preserved; review updated sources before adopting. Refresh replaces inherited evidence/applicability only where unchanged and keeps your custom text. Adoption review must be completed again.</p>
              <button type="button" className="secondary" disabled={drafts.some(item => !source(item.ruleCode))} onClick={refreshSources}>Review updated profile sources</button>
            </div>}
            {drafts.map(draft => {
              const rule = source(draft.ruleCode);
              return <section key={draft.id} aria-label={`Selected profile ${draft.ruleCode}`}>
                <h3>{label(draft.ruleCode)}</h3>
                {!rule ? <p className="notice pending">Reviewed profile unavailable. Publish same-code scientific profile in this scope first. No system fallback.</p> : <>
                  <dl className="residue-rule-detail-grid">
                    <div><dt>Reviewed scientific version</dt><dd>{rule.code} v{rule.versionNo} · {rule.reviewedBy} · {rule.reviewAuthority} · {rule.reviewedAt}</dd></div>
                    <div><dt>Applicability</dt><dd>{rule.residueDefinition} · {rule.inputStage} / {rule.inputBasis} → {rule.outputStage} / {rule.outputBasis} · {rule.analyticalMethod || 'No analytical method'} · {rule.unit} · {rule.decimalPlaces} decimal places</dd></div>
                    <div><dt>Scientific policy</dt><dd>{rule.kind === 'INTERPRETATION' ? `${rule.interpretationKind} · ${rule.ndThresholdKind || 'No ND threshold'}${rule.relativeUncertaintyPercent == null ? '' : ` · ±${rule.relativeUncertaintyPercent}% · reviewed at zero: ${rule.relativeUncertaintyAtZero ? 'Yes' : 'No'}`}` : `${rule.factorLower}–${rule.factorUpper}× · ${rule.factorSemantics} · ${rule.process} · ${rule.conditions} · ${rule.outputFraction} · homogeneous mixing: ${rule.homogeneousMixing ? 'reviewed' : 'not reviewed'}`}</dd></div>
                    <div><dt>Scientific effective period</dt><dd>{rule.effectiveFrom} — {rule.effectiveTo || 'No stated end'}</dd></div>
                    <div><dt>Evidence / applicability statement</dt><dd>{draft.evidenceRef} · {draft.applicabilityStatement}</dd></div>
                    <div><dt>Setting revision</dt><dd>v{draft.versionNo} · {draft.supersedesId ? 'Replaces previous setting; history retained' : 'First explicit adoption'}</dd></div>
                  </dl>
                  <details><summary>Customize adoption evidence or applicability</summary>
                    <label>Evidence reference · {draft.ruleCode}<input required value={draft.evidenceRef} onChange={event => setDrafts(current => current.map(item => item.id === draft.id ? { ...item, evidenceRef: event.target.value } : item))} /></label>
                    <label>Applicability statement · {draft.ruleCode}<textarea required value={draft.applicabilityStatement} onChange={event => setDrafts(current => current.map(item => item.id === draft.id ? { ...item, applicabilityStatement: event.target.value } : item))} /></label>
                  </details>
                </>}
              </section>;
            })}
            <fieldset><legend>Company adoption review · all {drafts.length} selected profiles</legend>
              <p>Scientific review above is retained, not impersonated. Record completed company adoption review below; publisher identity is recorded from your account.</p>
              <div className="ledger-form-grid">
                <label>Effective from<input required type="datetime-local" value={review.effectiveFrom} onChange={event => setReview(current => ({ ...current, effectiveFrom: event.target.value }))} /></label>
                <label>Effective until (optional)<input type="datetime-local" value={review.effectiveTo} onChange={event => setReview(current => ({ ...current, effectiveTo: event.target.value }))} /></label>
                <label>Adoption reviewer<input required value={review.reviewedBy} onChange={event => setReview(current => ({ ...current, reviewedBy: event.target.value }))} /></label>
                <label>Review authority<input required value={review.reviewAuthority} onChange={event => setReview(current => ({ ...current, reviewAuthority: event.target.value }))} /></label>
                <label>Reviewed at<input required type="datetime-local" value={review.reviewedAt} onChange={event => setReview(current => ({ ...current, reviewedAt: event.target.value }))} /></label>
              </div>
              <label><input type="checkbox" required disabled={sourceChanged} checked={confirmed} onChange={event => { event.stopPropagation(); setConfirmed(event.target.checked); }} /> I reviewed every selected profile and confirm this company adoption and effective period.</label>
            </fieldset>
            <button disabled={action.busy || !confirmed || sourceChanged || drafts.some(item => !source(item.ruleCode))}>{action.busy ? 'Publishing…' : 'Publish reviewed policy settings'}</button>
          </>}
        </fieldset>
      </form>}
      <Feedback {...action} />
      {settings.length > 0 && <section aria-labelledby="adoption-history-title">
        <h3 id="adoption-history-title">Adoption and override history</h3>
        <p className="residue-counts">{settingGroups.length} current setting series · {settingGroups.reduce((sum, group) => sum + group.tips.length, 0)} unsuperseded settings · {settingGroups.reduce((sum, group) => sum + group.history.length, 0)} earlier versions. Latest published setting is not necessarily effective now.</p>
        <div className="residue-toolbar">
          <label>Search adoption history<input type="search" value={historySearch} onChange={event => setHistorySearch(event.target.value)} /></label>
          <label>History setting mode<select value={historyMode} onChange={event => setHistoryMode(event.target.value)}><option value="">All modes</option><option value="SYSTEM_DEFAULT">System default</option><option value="COMPANY_OVERRIDE">Company override</option></select></label>
          <label>History effective status<select value={historyStatus} onChange={event => setHistoryStatus(event.target.value)}><option value="">All statuses</option>{['Within effective window now', 'Scheduled', 'Outside effective window'].map(status => <option key={status}>{status}</option>)}</select></label>
          <label>Sort adoption history<select value={historySort} onChange={event => setHistorySort(event.target.value)}><option value="code">Series code</option><option value="effective">Latest effective start</option></select></label>
        </div>
        {!filteredHistory.length && <p className="empty">No adoption history matches these filters.</p>}
        {filteredHistory.map(group => <section className="residue-history-series" key={group.code} aria-label={`Adoption history ${group.code}`}>
          <h4>{label(group.code)}</h4>
          {group.tips.length > 1 && <p className="notice pending">Conflicting published setting tips — no automatic selection.</p>}
          <p>Latest published settings</p><ul>{group.tips.map(settingRecord)}</ul>
          {group.history.length > 0 && <details open={group.history.some(historyMatches) && !group.tips.some(historyMatches) ? true : undefined}><summary>Earlier adoption versions · {group.code} · {group.history.length}</summary><ul>{group.history.map(settingRecord)}</ul></details>}
        </section>)}
      </section>}
    </section>
  </div>;
}
async function loadCatalog(path: string): Promise<CatalogItem[]> {
  const items: CatalogItem[] = [];
  for (let page = 1; ; page++) {
    const result = await api<{ items: CatalogItem[]; hasMore: boolean }>(`${path}?page=${page}&pageSize=100`);
    items.push(...result.items);
    if (!result.hasMore) return items;
  }
}
