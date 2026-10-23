import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, errorMessage } from './api';
import { Feedback, useAction } from './ledger-ui';

type SourceGroup = { id: string; companyId: string; code: string; versionNo: number; substanceId: string; sourceIds: string[]; productStage: string; productIdentity: string; materialBasis: string; residueDefinition: string; lower: string; upper: string; unit: string; decimalPlaces: number; precedence: string; supportingResultIds: string[]; effectiveFrom: string; effectiveTo: string | null; supersedesId: string | null; evidenceRef: string; applicabilityStatement: string; reviewedBy: string; reviewAuthority: string; reviewedAt: string; publishedBy: string; publishedAt: string };
type ReportSelection = { id: string; companyId: string; sourceId: string; substanceId: string; selectedResultId: string; supersededResultIds: string[]; materialBasis: string; residueDefinition: string; absoluteUncertainty: string | null; reportingThreshold: string | null; effectiveFrom: string; effectiveTo: string | null; supersedesId: string | null; evidenceRef: string; applicabilityStatement: string; reviewedBy: string; reviewAuthority: string; reviewedAt: string; publishedBy: string; publishedAt: string };
type ReportResult = { id: string; substanceId?: string; testLabel: string; originalResultText: string; resultKind: string; method?: string | null; uncertainty?: string | null };
type Report = { id: string; reportNumber: string; status: string; resultSets: Array<{ id: string; sourceLotId: string | null; results: ReportResult[] }> };
type Substance = { id: string; code: string; name: string };
type PolicyRule = { id: string; code: string; versionNo: number; companyId?: string | null; substanceId: string; supersedesId?: string | null; kind: string; interpretationKind: string | null; analyticalMethod?: string | null; inputStage: string; inputBasis: string; residueDefinition: string; evidenceRef: string; applicabilityStatement: string; unit: string; relativeUncertaintyPercent?: string | null; relativeUncertaintyAtZero?: boolean };
type PolicySetting = { id: string; companyId?: string; ruleCode: string; versionNo: number; mode: 'SYSTEM_DEFAULT' | 'COMPANY_OVERRIDE'; effectiveFrom: string; effectiveTo: string | null; supersedesId: string | null; evidenceRef: string; applicabilityStatement: string };
type Preview = { lower: string | null; upper: string | null; unit: string | null; basis: string | null; reasons: string[]; provenance: Array<{ uncertaintyMode?: string | null; relativeUncertaintyPercent?: string | null; absoluteUncertainty?: string | null }>; rules: Array<{ id: string; version: number; kind: string; policyOrigin?: string | null; policySettingId?: string | null; policySettingVersion?: number | null; ruleCode?: string | null; evidenceReference: string }>; decimalPlaces: number };
type GroupForm = Omit<SourceGroup, 'publishedBy' | 'publishedAt' | 'decimalPlaces'> & { decimalPlaces: number | null };
type SelectionForm = Omit<ReportSelection, 'publishedBy' | 'publishedAt'>;
const blankGroup = (companyId: string): GroupForm => ({ id: crypto.randomUUID(), companyId, code: '', versionNo: 1, substanceId: '', sourceIds: [], productStage: '', productIdentity: '', materialBasis: '', residueDefinition: '', lower: '', upper: '', unit: '', decimalPlaces: null, precedence: '', supportingResultIds: [], effectiveFrom: '', effectiveTo: null, supersedesId: null, evidenceRef: '', applicabilityStatement: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
const blankSelection = (companyId: string): SelectionForm => ({ id: crypto.randomUUID(), companyId, sourceId: '', substanceId: '', selectedResultId: '', supersededResultIds: [], materialBasis: '', residueDefinition: '', absoluteUncertainty: null, reportingThreshold: null, effectiveFrom: '', effectiveTo: null, supersedesId: null, evidenceRef: '', applicabilityStatement: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
const isoDate = (value: string) => new Date(value).toISOString();

export function ResidueEvidenceControls({ active, allowed, companyId, report }: { active: boolean; allowed: boolean; companyId: string; report?: Report }) {
  const [groups, setGroups] = useState<SourceGroup[]>([]), [selections, setSelections] = useState<ReportSelection[]>([]), [substances, setSubstances] = useState<Substance[]>([]);
  const [rules, setRules] = useState<PolicyRule[]>([]), [settings, setSettings] = useState<PolicySetting[]>([]), [profileId, setProfileId] = useState('');
  const [preview, setPreview] = useState<Preview | null>(null), [previewError, setPreviewError] = useState(''), [previewBusy, setPreviewBusy] = useState(false);
  const previewToken = useRef(0);
  const [group, setGroup] = useState<GroupForm>(() => blankGroup(companyId)), [selection, setSelection] = useState<SelectionForm>(() => blankSelection(companyId));
  const [loading, setLoading] = useState(false), [error, setError] = useState('');
  const results = report?.resultSets.flatMap(set => set.results) || [];
  const confirmedReportResults = report?.status === 'CONFIRMED' ? report.resultSets.flatMap(set => set.sourceLotId ? set.results.filter(result => result.id && result.substanceId).map(result => ({ ...result, sourceId: set.sourceLotId! })) : []) : [];
  const action = useAction();
  const currentSettings = settings.filter(item => item.companyId === companyId && !settings.some(next => next.supersedesId === item.id) && Date.parse(item.effectiveFrom) <= Date.now() && (!item.effectiveTo || Date.parse(item.effectiveTo) > Date.now()));
  const currentRules = rules.filter(rule => (!rule.companyId || rule.companyId === companyId) && !rules.some(next => next.companyId === rule.companyId && next.code === rule.code && next.supersedesId === rule.id));
  useEffect(() => {
    if (!active || !allowed) return;
    let current = true;
    previewToken.current++; setPreview(null); setPreviewError(''); setPreviewBusy(false); setProfileId('');
    setSelection(blankSelection(companyId)); setGroup(blankGroup(companyId));
    setGroups([]); setSelections([]); setRules([]); setSettings([]); setLoading(true); setError('');
    void Promise.all([api<{ items: SourceGroup[] }>('/api/residue-source-groups'), api<{ items: ReportSelection[] }>('/api/residue-report-selections'), loadAll<Substance>('/api/active-substances'), api<{ items: PolicyRule[] }>('/api/residue-rules'), api<{ items: PolicySetting[] }>('/api/residue-policy-settings')])
      .then(([groupPage, selectionPage, substanceItems, rulePage, settingPage]) => { if (current) { setGroups(groupPage.items.filter(item => item.companyId === companyId)); setSelections(selectionPage.items.filter(item => item.companyId === companyId)); setSubstances(substanceItems); setRules(rulePage.items.filter(item => !item.companyId || item.companyId === companyId)); setSettings(settingPage.items.filter(item => item.companyId === companyId)); } })
      .catch(cause => { if (current) setError(errorMessage(cause)); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [active, allowed, companyId, report?.id]);
  async function publishGroup(event: FormEvent) {
    event.preventDefault();
    if (!allowed || group.decimalPlaces == null) return;
    await action.run(async () => {
      await api('/api/residue-source-groups', { method: 'POST', body: JSON.stringify({ ...group, sourceIds: group.sourceIds.map(value => value.trim()).filter(Boolean), effectiveFrom: isoDate(group.effectiveFrom), effectiveTo: group.effectiveTo ? isoDate(group.effectiveTo) : null, reviewedAt: isoDate(group.reviewedAt) }) });
      setGroups((await api<{ items: SourceGroup[] }>('/api/residue-source-groups')).items.filter(item => item.companyId === companyId));
      setGroup(blankGroup(companyId));
    }, 'Reviewed fallback evidence version published.');
  }
  async function publishSelection(event: FormEvent) {
    event.preventDefault();
    if (!allowed) return;
    await action.run(async () => {
      await api('/api/residue-report-selections', { method: 'POST', body: JSON.stringify({ ...selection, supersededResultIds: selection.supersededResultIds.map(value => value.trim()).filter(Boolean), effectiveFrom: isoDate(selection.effectiveFrom), effectiveTo: selection.effectiveTo ? isoDate(selection.effectiveTo) : null, reviewedAt: isoDate(selection.reviewedAt) }) });
      setSelections((await api<{ items: ReportSelection[] }>('/api/residue-report-selections')).items.filter(item => item.companyId === companyId));
      setSelection(blankSelection(companyId));
      previewToken.current++; setPreview(null); setPreviewError(''); setPreviewBusy(false); setProfileId('');
    }, 'Reviewed report-result selection published.');
  }
  const setGroupValue = <K extends keyof GroupForm>(key: K, value: GroupForm[K]) => setGroup(current => ({ ...current, [key]: value }));
  const setSelectionValue = <K extends keyof SelectionForm>(key: K, value: SelectionForm[K]) => { previewToken.current++; setPreview(null); setPreviewError(''); setPreviewBusy(false); setSelection(current => ({ ...current, [key]: value })); };
  async function previewSelection() {
    const request = ++previewToken.current;
    setPreview(null); setPreviewError(''); setPreviewBusy(true);
    try {
      const result = await api<Preview>('/api/residue-report-selections/preview', { method: 'POST', body: JSON.stringify({ ...selection, supersededResultIds: selection.supersededResultIds.map(value => value.trim()).filter(Boolean), effectiveFrom: selection.effectiveFrom ? isoDate(selection.effectiveFrom) : '', effectiveTo: selection.effectiveTo ? isoDate(selection.effectiveTo) : null, reviewedAt: selection.reviewedAt ? isoDate(selection.reviewedAt) : undefined }) });
      if (request === previewToken.current) setPreview(result);
    } catch (cause) { if (request === previewToken.current) setPreviewError(errorMessage(cause)); }
    finally { if (request === previewToken.current) setPreviewBusy(false); }
  }
  return <section className="panel" aria-labelledby="residue-evidence-title">
    <h2 id="residue-evidence-title">Residue evidence and fallback records</h2>
    <p>Reviewed evidence supports residue assessment without changing original laboratory results or the VN comparison.</p>
    {loading && <p role="status">Loading published residue evidence…</p>}{error && <p role="alert" className="notice error">{error}</p>}<Feedback {...action} />
    {report && <form onSubmit={publishSelection}><fieldset disabled={!allowed || action.busy}><legend>Record reviewed report-result evidence</legend>
      {report.status !== 'CONFIRMED' && <p role="status">Report is {report.status}. Only confirmed reports can provide a selectable result.</p>}
      <label>Confirmed report result<select required value={selection.selectedResultId} onChange={e => {
        const result = confirmedReportResults.find(item => item.id === e.target.value);
        previewToken.current++; setPreview(null); setPreviewError(''); setPreviewBusy(false); setProfileId('');
        setSelection(current => ({ ...current, selectedResultId: result?.id || '', sourceId: result?.sourceId || '', substanceId: result?.substanceId || '' }));
      }}><option value="">Choose confirmed result</option>{confirmedReportResults.map(result => <option key={result.id} value={result.id}>{result.testLabel} · {result.originalResultText} · {result.resultKind} · {result.sourceId} · {result.substanceId} · {result.id}</option>)}</select></label>
      {selection.selectedResultId && <p role="status">Bound to source {selection.sourceId} and substance {selection.substanceId}.</p>}
      <label>Adopted residue profile<select value={profileId} onChange={event => {
        setProfileId(event.target.value); previewToken.current++; setPreview(null); setPreviewError(''); setPreviewBusy(false);
        const chosen = currentRules.find(rule => rule.id === event.target.value);
        const setting = chosen && currentSettings.filter(item => item.ruleCode === chosen.code && item.mode === (chosen.companyId ? 'COMPANY_OVERRIDE' : 'SYSTEM_DEFAULT')).sort((a, b) => b.versionNo - a.versionNo)[0];
        if (!chosen || !setting) return;
        setSelection(current => ({ ...current, materialBasis: chosen.inputBasis, residueDefinition: chosen.residueDefinition, evidenceRef: `${chosen.evidenceRef}; policy setting ${setting.evidenceRef}`, applicabilityStatement: `Profile ${chosen.code} v${setting.versionNo}: ${chosen.applicabilityStatement}; setting applicability: ${setting.applicabilityStatement}` }));
      }}><option value="">Choose adopted profile</option>{currentRules.filter(rule => rule.kind === 'INTERPRETATION' && rule.substanceId === selection.substanceId && currentSettings.some(setting => setting.ruleCode === rule.code && setting.mode === (rule.companyId ? 'COMPANY_OVERRIDE' : 'SYSTEM_DEFAULT'))).map(rule => <option key={rule.id} value={rule.id}>{rule.code} v{rule.versionNo} · {rule.companyId ? 'company override' : 'system default'} · {rule.inputStage} · {rule.inputBasis} · {rule.residueDefinition}</option>)}</select></label>
      <p className="muted">Profile selection pre-fills reviewed basis, definition and evidence references only. Reviewer and review date remain explicit; server preview decides matching and conflicts.</p>
      {selection.selectedResultId && <p>Reported uncertainty: {confirmedReportResults.find(result => result.id === selection.selectedResultId)?.uncertainty || 'Not reported'}. Normalize laboratory uncertainty explicitly; percentage profiles cannot silently replace it.</p>}
      <label>Superseded result IDs (comma-separated)<textarea value={selection.supersededResultIds.join(',')} onChange={e => setSelectionValue('supersededResultIds', e.target.value.split(','))} /></label>
      <label>Material basis<input required value={selection.materialBasis} onChange={e => setSelectionValue('materialBasis', e.target.value)} /></label><label>Residue definition<input required value={selection.residueDefinition} onChange={e => setSelectionValue('residueDefinition', e.target.value)} /></label>
      <label>Reviewed absolute uncertainty (optional exact decimal; authoritative when supplied)<input inputMode="decimal" value={selection.absoluteUncertainty || ''} onChange={e => setSelectionValue('absoluteUncertainty', e.target.value || null)} /></label><label>Reviewed reporting threshold (optional exact decimal)<input inputMode="decimal" value={selection.reportingThreshold || ''} onChange={e => setSelectionValue('reportingThreshold', e.target.value || null)} /></label>
      <label>Effective from<input required type="datetime-local" value={selection.effectiveFrom} onChange={e => setSelectionValue('effectiveFrom', e.target.value)} /></label><label>Effective until<input type="datetime-local" value={selection.effectiveTo || ''} onChange={e => setSelectionValue('effectiveTo', e.target.value || null)} /></label><label>Supersedes selection ID<input value={selection.supersedesId || ''} onChange={e => setSelectionValue('supersedesId', e.target.value || null)} /></label>
      <label>Evidence reference<input required value={selection.evidenceRef} onChange={e => setSelectionValue('evidenceRef', e.target.value)} /></label><label>Applicability statement<textarea required value={selection.applicabilityStatement} onChange={e => setSelectionValue('applicabilityStatement', e.target.value)} /></label><label>Scientific reviewer<input required value={selection.reviewedBy} onChange={e => setSelectionValue('reviewedBy', e.target.value)} /></label><label>Review authority<input required value={selection.reviewAuthority} onChange={e => setSelectionValue('reviewAuthority', e.target.value)} /></label><label>Reviewed at<input required type="datetime-local" value={selection.reviewedAt} onChange={e => setSelectionValue('reviewedAt', e.target.value)} /></label>
      <div className="admin-form-actions"><button type="button" disabled={previewBusy || report.status !== 'CONFIRMED' || !selection.selectedResultId || !selection.effectiveFrom} onClick={() => void previewSelection()}>Preview server assessment</button><button disabled={action.busy || report.status !== 'CONFIRMED' || !selection.selectedResultId}>{action.busy ? 'Publishing…' : 'Publish reviewed selection'}</button></div>
      {previewBusy && <p role="status">Requesting non-mutating server preview…</p>}{previewError && <p role="alert" className="notice error">{previewError}</p>}{preview && <section className="notice" aria-label="Server residue preview"><h3>Server preview · no selection published</h3><p>Lab-reported result/uncertainty text: {confirmedReportResults.find(result => result.id === selection.selectedResultId)?.originalResultText || 'unavailable'} · reviewer-normalized absolute uncertainty: {selection.absoluteUncertainty || 'not supplied'} · server interval {preview.lower ?? '—'}–{preview.upper ?? '—'} {preview.unit || 'unit unavailable'} · {preview.basis || 'basis not specified'}</p>{preview.reasons.length > 0 && <ul>{preview.reasons.map((reason, i) => <li key={`${i}-${reason}`}>{reason}</li>)}</ul>}{preview.rules.length > 0 && <ul>{preview.rules.map((rule, i) => <li key={`${rule.id}-${i}`}>{rule.policyOrigin || 'origin unavailable'} · {rule.ruleCode || rule.id} v{rule.version}{rule.policySettingVersion ? ` · setting v${rule.policySettingVersion}` : ''} · {rule.evidenceReference}</li>)}</ul>}</section>}
      {preview?.provenance.filter(item => item.uncertaintyMode).map((item, index) => <p key={index}>Applied uncertainty: {item.uncertaintyMode}{item.relativeUncertaintyPercent != null ? ` · ±${item.relativeUncertaintyPercent}% of reported value` : ''} · absolute uncertainty {item.absoluteUncertainty ?? 'unavailable'} {preview.unit}</p>)}
    </fieldset></form>}
    <details><summary>Fallback evidence maintenance</summary>{fallbackContent()}</details>
    {selections.length > 0 && <details><summary>Published reviewed report-result evidence ({selections.length})</summary><ul>{selections.map(item => <li key={item.id}>{item.sourceId} · {item.substanceId} · result {item.selectedResultId} · {item.reviewAuthority}</li>)}</ul></details>}
  </section>;
  function fallbackContent() {
    return <>
      <details><summary>Published source-group fallback versions ({groups.length})</summary>{groups.length ? <ul>{groups.map(item => <li key={item.id}>{item.code} v{item.versionNo} · {item.productStage} / {item.productIdentity} · {item.lower}–{item.upper} {item.unit} · {item.precedence} · {item.reviewAuthority}</li>)}</ul> : <p>No published source-group fallback versions.</p>}</details>
      <form onSubmit={publishGroup}><fieldset disabled={!allowed || action.busy}><legend>Publish source-group fallback version</legend>
        <label>Group code<input required value={group.code} onChange={e => setGroupValue('code', e.target.value)} /></label><label>Version<input required type="number" min="1" step="1" value={group.versionNo} onChange={e => setGroupValue('versionNo', Number(e.target.value))} /></label>
        <label>Substance<select required value={group.substanceId} onChange={e => { setGroupValue('substanceId', e.target.value); setGroupValue('supportingResultIds', []); }}><option value="">Choose substance</option>{substances.map(s => <option key={s.id} value={s.id}>{s.code} · {s.name}</option>)}</select></label>
        <label>Explicit source IDs (comma-separated)<textarea required value={group.sourceIds.join(',')} onChange={e => setGroupValue('sourceIds', e.target.value.split(','))} /></label>
        {report && <p>Sources linked to this report: {Array.from(new Set(report.resultSets.map(set => set.sourceLotId).filter((value): value is string => !!value))).join(', ') || 'none'}</p>}
        <label>Product stage<input required value={group.productStage} onChange={e => setGroupValue('productStage', e.target.value)} /></label><label>Product identity<input required value={group.productIdentity} onChange={e => setGroupValue('productIdentity', e.target.value)} /></label>
        <label>Material basis<input required value={group.materialBasis} onChange={e => setGroupValue('materialBasis', e.target.value)} /></label><label>Residue definition<input required value={group.residueDefinition} onChange={e => setGroupValue('residueDefinition', e.target.value)} /></label>
        <label>Fallback lower endpoint (exact decimal)<input required inputMode="decimal" value={group.lower} onChange={e => setGroupValue('lower', e.target.value)} /></label><label>Fallback upper endpoint (exact decimal)<input required inputMode="decimal" value={group.upper} onChange={e => setGroupValue('upper', e.target.value)} /></label>
        <label>Unit<select required value={group.unit} onChange={e => setGroupValue('unit', e.target.value)}><option value="">Choose concentration unit</option><option>mg/kg</option></select></label><label>Reviewed outward precision<input required type="number" min="0" max="18" step="1" value={group.decimalPlaces ?? ''} onChange={e => setGroupValue('decimalPlaces', e.target.value === '' ? null : Number(e.target.value))} /></label>
        <label>Direct/fallback precedence<select required value={group.precedence} onChange={e => setGroupValue('precedence', e.target.value)}><option value="">Choose reviewed precedence</option><option>DIRECT_FIRST</option><option>GROUP_FIRST</option><option>UNTESTED_ONLY</option></select></label>
        <label>Supporting confirmed result IDs (optional when external evidence reference supports fallback)<select multiple size={5} value={group.supportingResultIds} onChange={e => setGroupValue('supportingResultIds', Array.from(e.target.selectedOptions, option => option.value))}>{report?.status === 'CONFIRMED' && results.filter(result => result.id && (!group.substanceId || result.substanceId === group.substanceId)).map(result => <option key={result.id} value={result.id}>{result.testLabel} · {result.originalResultText} · {result.resultKind} · {result.id}</option>)}</select></label>
        <label>Effective from<input required type="datetime-local" value={group.effectiveFrom} onChange={e => setGroupValue('effectiveFrom', e.target.value)} /></label><label>Effective until<input type="datetime-local" value={group.effectiveTo || ''} onChange={e => setGroupValue('effectiveTo', e.target.value || null)} /></label>
        <label>Supersedes group version ID<input value={group.supersedesId || ''} onChange={e => setGroupValue('supersedesId', e.target.value || null)} /></label><label>External evidence reference<input required value={group.evidenceRef} onChange={e => setGroupValue('evidenceRef', e.target.value)} /></label>
        <label>Applicability statement<textarea required value={group.applicabilityStatement} onChange={e => setGroupValue('applicabilityStatement', e.target.value)} /></label><label>Scientific reviewer<input required value={group.reviewedBy} onChange={e => setGroupValue('reviewedBy', e.target.value)} /></label><label>Review authority<input required value={group.reviewAuthority} onChange={e => setGroupValue('reviewAuthority', e.target.value)} /></label><label>Reviewed at<input required type="datetime-local" value={group.reviewedAt} onChange={e => setGroupValue('reviewedAt', e.target.value)} /></label>
        <button disabled={action.busy || group.decimalPlaces == null || group.sourceIds.length === 0}>{action.busy ? 'Publishing…' : 'Publish fallback version'}</button>
      </fieldset></form>
    </>;
  }
}
async function loadAll<T>(path: string): Promise<T[]> {
  const items: T[] = [];
  for (let page = 1; ; page++) {
    const result = await api<{ items: T[]; hasMore: boolean }>(`${path}?page=${page}&pageSize=100`);
    items.push(...result.items);
    if (!result.hasMore) return items;
  }
}
