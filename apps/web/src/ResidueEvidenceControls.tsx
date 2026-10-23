import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, errorMessage } from './api';
import { Feedback, useAction } from './ledger-ui';

type SourceGroup = { id: string; companyId: string; code: string; versionNo: number; substanceId: string; sourceIds: string[]; productStage: string; productIdentity: string; materialBasis: string; residueDefinition: string; lower: string; upper: string; unit: string; decimalPlaces: number; precedence: string; supportingResultIds: string[]; effectiveFrom: string; effectiveTo: string | null; supersedesId: string | null; evidenceRef: string; applicabilityStatement: string; reviewedBy: string; reviewAuthority: string; reviewedAt: string; publishedBy: string; publishedAt: string };
type ReportSelection = { id: string; companyId: string; sourceId: string; substanceId: string; selectedResultId: string; supersededResultIds: string[]; materialBasis: string; residueDefinition: string; absoluteUncertainty: string | null; reportingThreshold: string | null; effectiveFrom: string; effectiveTo: string | null; supersedesId: string | null; evidenceRef: string; applicabilityStatement: string; reviewedBy: string; reviewAuthority: string; reviewedAt: string; publishedBy: string; publishedAt: string };
type ReportResult = { id: string; substanceId?: string; testLabel: string; originalResultText: string; resultKind: string; method?: string | null; uncertainty?: string | null };
type Report = { id: string; reportNumber: string; status: string; resultSets: Array<{ id: string; sourceLotId: string | null; results: ReportResult[] }> };
type Substance = { id: string; code: string; name: string };
type Preview = { lower: string | null; upper: string | null; unit: string | null; basis: string | null; reasons: string[]; provenance: Array<{ uncertaintyMode?: string | null; relativeUncertaintyPercent?: string | null; absoluteUncertainty?: string | null }>; rules: Array<{ id: string; version: number; kind: string; policyOrigin?: string | null; policySettingId?: string | null; policySettingVersion?: number | null; ruleCode?: string | null; evidenceReference: string }>; decimalPlaces: number };
type ReviewInput = { id: string; resultId: string; sourceId: string; absoluteUncertainty: string | null; reportingThreshold: string | null; supersededResultIds: string[] | null; supersedesId: string | null; note: string | null; previewFingerprint: string | null };
type ReviewChoice = { resultId: string; label: string; selectionId: string | null };
type ReviewRow = { input: ReviewInput; label: string; rawUncertainty: string | null; profile: { code: string; versionNo: number; inputBasis: string; residueDefinition: string; evidenceRef: string; applicabilityStatement: string; reviewedBy: string; reviewAuthority: string; reviewedAt: string; ndThresholdKind: string | null } | null; setting: { mode: string; versionNo: number; reviewedBy: string; reviewAuthority: string; evidenceRef: string } | null; assessment: Preview | null; reasons: string[]; conflictingResults: ReviewChoice[]; existingSelections: ReviewChoice[]; published: boolean };
type ReviewSummary = { reportId: string; effectiveFrom: string; rows: ReviewRow[] };
type GroupForm = Omit<SourceGroup, 'publishedBy' | 'publishedAt' | 'decimalPlaces'> & { decimalPlaces: number | null };
type SelectionForm = Omit<ReportSelection, 'publishedBy' | 'publishedAt'>;
const blankGroup = (companyId: string): GroupForm => ({ id: crypto.randomUUID(), companyId, code: '', versionNo: 1, substanceId: '', sourceIds: [], productStage: '', productIdentity: '', materialBasis: '', residueDefinition: '', lower: '', upper: '', unit: '', decimalPlaces: null, precedence: '', supportingResultIds: [], effectiveFrom: '', effectiveTo: null, supersedesId: null, evidenceRef: '', applicabilityStatement: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
const blankSelection = (companyId: string): SelectionForm => ({ id: crypto.randomUUID(), companyId, sourceId: '', substanceId: '', selectedResultId: '', supersededResultIds: [], materialBasis: '', residueDefinition: '', absoluteUncertainty: null, reportingThreshold: null, effectiveFrom: '', effectiveTo: null, supersedesId: null, evidenceRef: '', applicabilityStatement: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
const isoDate = (value: string) => new Date(value).toISOString();

export function ResidueEvidenceControls({ active, allowed, companyId, report }: { active: boolean; allowed: boolean; companyId: string; report?: Report }) {
  const [groups, setGroups] = useState<SourceGroup[]>([]), [selections, setSelections] = useState<ReportSelection[]>([]), [substances, setSubstances] = useState<Substance[]>([]);
  const [summary, setSummary] = useState<ReviewSummary | null>(null), [reviewInputs, setReviewInputs] = useState<ReviewInput[]>([]), [reviewDirty, setReviewDirty] = useState(false);
  const [reviewError, setReviewError] = useState(''), [reviewBusy, setReviewBusy] = useState(false);
  const reviewToken = useRef(0);
  const [preview, setPreview] = useState<Preview | null>(null), [previewError, setPreviewError] = useState(''), [previewBusy, setPreviewBusy] = useState(false);
  const previewToken = useRef(0);
  const [group, setGroup] = useState<GroupForm>(() => blankGroup(companyId)), [selection, setSelection] = useState<SelectionForm>(() => blankSelection(companyId));
  const [loading, setLoading] = useState(false), [error, setError] = useState('');
  const results = report?.resultSets.flatMap(set => set.results) || [];
  const confirmedReportResults = report?.status === 'CONFIRMED' ? report.resultSets.flatMap(set => set.sourceLotId ? set.results.filter(result => result.id && result.substanceId).map(result => ({ ...result, sourceId: set.sourceLotId! })) : []) : [];
  const action = useAction();
  const exceptionRow = summary?.rows.find(row => row.input.resultId === selection.selectedResultId);
  useEffect(() => {
    if (!active || !allowed) return;
    let current = true;
    previewToken.current++; setPreview(null); setPreviewError(''); setPreviewBusy(false);
    reviewToken.current++; setSummary(null); setReviewInputs([]); setReviewDirty(false); setReviewError(''); setReviewBusy(false);
    setSelection(blankSelection(companyId)); setGroup(blankGroup(companyId));
    setGroups([]); setSelections([]); setLoading(true); setError('');
    void Promise.all([api<{ items: SourceGroup[] }>('/api/residue-source-groups'), api<{ items: ReportSelection[] }>('/api/residue-report-selections'), loadAll<Substance>('/api/active-substances')])
      .then(([groupPage, selectionPage, substanceItems]) => { if (current) { setGroups(groupPage.items.filter(item => item.companyId === companyId)); setSelections(selectionPage.items.filter(item => item.companyId === companyId)); setSubstances(substanceItems); } })
      .catch(cause => { if (current) setError(errorMessage(cause)); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [active, allowed, companyId, report?.id, report?.status]);
  useEffect(() => {
    if (!active || !allowed || report?.status !== 'CONFIRMED') return;
    void prepareReport([]);
    return () => { reviewToken.current++; };
  }, [active, allowed, companyId, report?.id, report?.status]);
  async function prepareReport(inputs = reviewInputs) {
    if (!report) return;
    const token = ++reviewToken.current;
    setReviewBusy(true); setReviewError('');
    try {
      const next = await api<ReviewSummary>(`/api/lab-reports/${report.id}/residue-review/preview`, { method: 'POST', body: JSON.stringify({ companyId, effectiveFrom: inputs.length ? summary?.effectiveFrom : null, rows: inputs.filter(row => row.sourceId && row.sourceId !== '00000000-0000-0000-0000-000000000000') }) });
      if (token !== reviewToken.current) return;
      setSummary(next); setReviewInputs(next.rows.map(row => row.input)); setReviewDirty(false);
    } catch (cause) { if (token === reviewToken.current) setReviewError(errorMessage(cause)); }
    finally { if (token === reviewToken.current) setReviewBusy(false); }
  }
  function changeReview(index: number, patch: Partial<ReviewInput>) {
    reviewToken.current++; setReviewBusy(false); setReviewDirty(true); setReviewError('');
    setReviewInputs(current => current.map((row, i) => i === index ? { ...row, ...patch, previewFingerprint: null } : row));
  }
  async function confirmReport() {
    if (!report || !summary || reviewDirty) return;
    const rows = summary.rows.filter(row => !row.published && row.reasons.length === 0).map(row => row.input);
    if (!rows.length) return;
    await action.run(async () => {
      await api(`/api/lab-reports/${report.id}/residue-review/confirm`, { method: 'POST', body: JSON.stringify({ companyId, effectiveFrom: summary.effectiveFrom, rows }) });
      setSelections((await api<{ items: ReportSelection[] }>('/api/residue-report-selections')).items.filter(item => item.companyId === companyId));
      await prepareReport([]);
    }, 'Report bindings confirmed. Scientific profile review and original report unchanged.');
  }
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
      previewToken.current++; setPreview(null); setPreviewError(''); setPreviewBusy(false);
      await prepareReport([]);
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
    <h2 id="residue-evidence-title">{report ? 'Review report residue evidence' : 'Residue evidence and fallback records'}</h2>
    <p>Reuse adopted scientific profiles. Confirming a report binding does not make you its original scientific reviewer or alter laboratory results.</p>
    {loading && <p role="status">Loading published residue evidence…</p>}{error && <p role="alert" className="notice error">{error}</p>}<Feedback {...action} />
    {report && <section aria-label="Report residue review">
      {report.status !== 'CONFIRMED' ? <p role="status">Confirm report identity and transcription before residue review.</p> : <>
        {reviewBusy && <p role="status">Matching report results…</p>}{reviewError && <p role="alert" className="notice error">{reviewError}</p>}
        {summary && summary.rows.map((row, index) => {
          const input = reviewInputs[index] ?? row.input;
          return <article key={`${row.input.resultId}-${row.input.sourceId}`} className="notice">
            <h3>{row.label}</h3><p>Reported uncertainty: {row.rawUncertainty || 'Not reported'}</p>
            {row.profile && <><p><strong>{row.profile.code} v{row.profile.versionNo}</strong> · {row.setting?.mode === 'COMPANY_OVERRIDE' ? 'company override' : 'adopted system profile'} · company setting v{row.setting?.versionNo}</p>
              <p>{row.profile.inputBasis} · {row.profile.residueDefinition}</p>
              <details><summary>Scientific review and applicability</summary><p>{row.profile.applicabilityStatement}</p><p>Scientific reviewer: {row.profile.reviewedBy} · {row.profile.reviewAuthority} · {row.profile.reviewedAt}</p><p>Company policy reviewer: {row.setting?.reviewedBy} · {row.setting?.reviewAuthority}</p><p>{row.profile.evidenceRef} · {row.setting?.evidenceRef}</p></details></>}
            {!reviewDirty && (row.reasons.length > 0 ? <ul aria-label="Unresolved prerequisites">{row.reasons.map(reason => <li key={reason}>{reason}</li>)}</ul> : <p>{row.published ? 'Binding confirmed' : 'Ready for binding confirmation'} · {row.assessment?.lower}–{row.assessment?.upper} {row.assessment?.unit}</p>)}
            {(row.rawUncertainty || row.profile?.ndThresholdKind === 'REPORTING_THRESHOLD' || row.conflictingResults.length > 0 || row.existingSelections.length > 0) && <details><summary>Review uncertainty, threshold or report conflicts</summary>
              {row.rawUncertainty && <label>Reviewed absolute uncertainty for {row.label}<input inputMode="decimal" value={input.absoluteUncertainty || ''} onChange={event => changeReview(index, { absoluteUncertainty: event.target.value || null })} /></label>}
              {row.profile?.ndThresholdKind === 'REPORTING_THRESHOLD' && <label>Reviewed reporting threshold for {row.label}<input inputMode="decimal" value={input.reportingThreshold || ''} onChange={event => changeReview(index, { reportingThreshold: event.target.value || null })} /></label>}
              {row.conflictingResults.length > 0 && <label>Explicitly superseded reports for {row.label}<select multiple value={input.supersededResultIds ?? []} onChange={event => changeReview(index, { supersededResultIds: Array.from(event.target.selectedOptions, option => option.value) })}>{row.conflictingResults.map(choice => <option key={choice.resultId} value={choice.resultId}>{choice.label}</option>)}</select></label>}
              {row.existingSelections.length > 0 && <label>Replace existing binding for {row.label}<select value={input.supersedesId || ''} onChange={event => changeReview(index, { supersedesId: event.target.value || null })}><option value="">Do not replace existing binding</option>{row.existingSelections.map(choice => <option key={choice.selectionId} value={choice.selectionId || ''}>{choice.label}</option>)}</select></label>}
              <label>Binding review note for {row.label}<textarea value={input.note || ''} onChange={event => changeReview(index, { note: event.target.value || null })} /></label>
              <p>Current actor confirms this normalization or supersession. Original scientific review remains attached to profile. Never substitute a percentage for laboratory-reported uncertainty.</p>
            </details>}
          </article>;
        })}
        {reviewDirty && <p role="status">Inputs changed. Refresh result matches before confirming.</p>}
        <div className="admin-form-actions"><button type="button" disabled={!allowed || reviewBusy || action.busy} onClick={() => void prepareReport()}>Refresh result matches</button><button type="button" disabled={!allowed || reviewBusy || action.busy || reviewDirty || !summary?.rows.some(row => !row.published && row.reasons.length === 0)} onClick={() => void confirmReport()}>{action.busy ? 'Confirming…' : 'Review and confirm compatible results'}</button></div>
      </>}
    </section>}
    {report && <details><summary>Expert result evidence and unmatched cases</summary><form onSubmit={publishSelection}><fieldset disabled={!allowed || action.busy}><legend>Record reviewed report-result evidence</legend>
      {report.status !== 'CONFIRMED' && <p role="status">Report is {report.status}. Only confirmed reports can provide a selectable result.</p>}
      <label>Confirmed report result<select required value={selection.selectedResultId} onChange={e => {
        const result = confirmedReportResults.find(item => item.id === e.target.value);
        previewToken.current++; setPreview(null); setPreviewError(''); setPreviewBusy(false);
        setSelection(current => ({ ...current, selectedResultId: result?.id || '', sourceId: result?.sourceId || '', substanceId: result?.substanceId || '' }));
      }}><option value="">Choose confirmed result</option>{confirmedReportResults.map(result => <option key={result.id} value={result.id}>{result.testLabel} · {result.originalResultText} · {result.resultKind} · {result.sourceId} · {result.substanceId} · {result.id}</option>)}</select></label>
      {selection.selectedResultId && <p role="status">Bound to source {selection.sourceId} and substance {selection.substanceId}.</p>}
      {selection.selectedResultId && <p>Reported uncertainty: {confirmedReportResults.find(result => result.id === selection.selectedResultId)?.uncertainty || 'Not reported'}. Normalize laboratory uncertainty explicitly; percentage profiles cannot silently replace it.</p>}
      <label>Explicitly superseded report results<select multiple value={selection.supersededResultIds} onChange={e => setSelectionValue('supersededResultIds', Array.from(e.target.selectedOptions, option => option.value))}>{exceptionRow?.conflictingResults.map(choice => <option key={choice.resultId} value={choice.resultId}>{choice.label}</option>)}</select></label>
      <label>Material basis<input required value={selection.materialBasis} onChange={e => setSelectionValue('materialBasis', e.target.value)} /></label><label>Residue definition<input required value={selection.residueDefinition} onChange={e => setSelectionValue('residueDefinition', e.target.value)} /></label>
      <label>Reviewed absolute uncertainty (optional exact decimal; authoritative when supplied)<input inputMode="decimal" value={selection.absoluteUncertainty || ''} onChange={e => setSelectionValue('absoluteUncertainty', e.target.value || null)} /></label><label>Reviewed reporting threshold (optional exact decimal)<input inputMode="decimal" value={selection.reportingThreshold || ''} onChange={e => setSelectionValue('reportingThreshold', e.target.value || null)} /></label>
      <label>Effective from<input required type="datetime-local" value={selection.effectiveFrom} onChange={e => setSelectionValue('effectiveFrom', e.target.value)} /></label><label>Effective until<input type="datetime-local" value={selection.effectiveTo || ''} onChange={e => setSelectionValue('effectiveTo', e.target.value || null)} /></label><label>Supersedes reviewed selection<select value={selection.supersedesId || ''} onChange={e => setSelectionValue('supersedesId', e.target.value || null)}><option value="">No selection supersession</option>{exceptionRow?.existingSelections.map(choice => <option key={choice.selectionId} value={choice.selectionId || ''}>{choice.label}</option>)}</select></label>
      <label>Evidence reference<input required value={selection.evidenceRef} onChange={e => setSelectionValue('evidenceRef', e.target.value)} /></label><label>Applicability statement<textarea required value={selection.applicabilityStatement} onChange={e => setSelectionValue('applicabilityStatement', e.target.value)} /></label><label>Scientific reviewer<input required value={selection.reviewedBy} onChange={e => setSelectionValue('reviewedBy', e.target.value)} /></label><label>Review authority<input required value={selection.reviewAuthority} onChange={e => setSelectionValue('reviewAuthority', e.target.value)} /></label><label>Reviewed at<input required type="datetime-local" value={selection.reviewedAt} onChange={e => setSelectionValue('reviewedAt', e.target.value)} /></label>
      <div className="admin-form-actions"><button type="button" disabled={previewBusy || report.status !== 'CONFIRMED' || !selection.selectedResultId || !selection.effectiveFrom} onClick={() => void previewSelection()}>Preview server assessment</button><button disabled={action.busy || report.status !== 'CONFIRMED' || !selection.selectedResultId}>{action.busy ? 'Publishing…' : 'Publish reviewed selection'}</button></div>
      {previewBusy && <p role="status">Requesting non-mutating server preview…</p>}{previewError && <p role="alert" className="notice error">{previewError}</p>}{preview && <section className="notice" aria-label="Server residue preview"><h3>Server preview · no selection published</h3><p>Lab-reported result/uncertainty text: {confirmedReportResults.find(result => result.id === selection.selectedResultId)?.originalResultText || 'unavailable'} · reviewer-normalized absolute uncertainty: {selection.absoluteUncertainty || 'not supplied'} · server interval {preview.lower ?? '—'}–{preview.upper ?? '—'} {preview.unit || 'unit unavailable'} · {preview.basis || 'basis not specified'}</p>{preview.reasons.length > 0 && <ul>{preview.reasons.map((reason, i) => <li key={`${i}-${reason}`}>{reason}</li>)}</ul>}{preview.rules.length > 0 && <ul>{preview.rules.map((rule, i) => <li key={`${rule.id}-${i}`}>{rule.policyOrigin || 'origin unavailable'} · {rule.ruleCode || rule.id} v{rule.version}{rule.policySettingVersion ? ` · setting v${rule.policySettingVersion}` : ''} · {rule.evidenceReference}</li>)}</ul>}</section>}
      {preview?.provenance.filter(item => item.uncertaintyMode).map((item, index) => <p key={index}>Applied uncertainty: {item.uncertaintyMode}{item.relativeUncertaintyPercent != null ? ` · ±${item.relativeUncertaintyPercent}% of reported value` : ''} · absolute uncertainty {item.absoluteUncertainty ?? 'unavailable'} {preview.unit}</p>)}
    </fieldset></form></details>}
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
