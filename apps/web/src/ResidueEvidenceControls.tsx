import { useEffect, useState, type FormEvent } from 'react';
import { api, errorMessage } from './api';
import { Feedback, useAction } from './ledger-ui';

type SourceGroup = { id: string; companyId: string; code: string; versionNo: number; substanceId: string; sourceIds: string[]; productStage: string; productIdentity: string; materialBasis: string; residueDefinition: string; lower: string; upper: string; unit: string; decimalPlaces: number; precedence: string; supportingResultIds: string[]; effectiveFrom: string; effectiveTo: string | null; supersedesId: string | null; evidenceRef: string; applicabilityStatement: string; reviewedBy: string; reviewAuthority: string; reviewedAt: string; publishedBy: string; publishedAt: string };
type ReportSelection = { id: string; companyId: string; sourceId: string; substanceId: string; selectedResultId: string; supersededResultIds: string[]; materialBasis: string; residueDefinition: string; absoluteUncertainty: string | null; reportingThreshold: string | null; effectiveFrom: string; effectiveTo: string | null; supersedesId: string | null; evidenceRef: string; applicabilityStatement: string; reviewedBy: string; reviewAuthority: string; reviewedAt: string; publishedBy: string; publishedAt: string };
type ReportResult = { id: string; substanceId?: string; testLabel: string; originalResultText: string; resultKind: string };
type Report = { id: string; reportNumber: string; status: string; resultSets: Array<{ id: string; sourceLotId: string | null; results: ReportResult[] }> };
type Substance = { id: string; code: string; name: string };
type GroupForm = Omit<SourceGroup, 'publishedBy' | 'publishedAt' | 'decimalPlaces'> & { decimalPlaces: number | null };
type SelectionForm = Omit<ReportSelection, 'publishedBy' | 'publishedAt'>;
const blankGroup = (companyId: string): GroupForm => ({ id: crypto.randomUUID(), companyId, code: '', versionNo: 1, substanceId: '', sourceIds: [], productStage: '', productIdentity: '', materialBasis: '', residueDefinition: '', lower: '', upper: '', unit: '', decimalPlaces: null, precedence: '', supportingResultIds: [], effectiveFrom: '', effectiveTo: null, supersedesId: null, evidenceRef: '', applicabilityStatement: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
const blankSelection = (companyId: string): SelectionForm => ({ id: crypto.randomUUID(), companyId, sourceId: '', substanceId: '', selectedResultId: '', supersededResultIds: [], materialBasis: '', residueDefinition: '', absoluteUncertainty: null, reportingThreshold: null, effectiveFrom: '', effectiveTo: null, supersedesId: null, evidenceRef: '', applicabilityStatement: '', reviewedBy: '', reviewAuthority: '', reviewedAt: '' });
const isoDate = (value: string) => new Date(value).toISOString();

export function ResidueEvidenceControls({ active, allowed, companyId, report }: { active: boolean; allowed: boolean; companyId: string; report?: Report }) {
  const [groups, setGroups] = useState<SourceGroup[]>([]), [selections, setSelections] = useState<ReportSelection[]>([]), [substances, setSubstances] = useState<Substance[]>([]);
  const [group, setGroup] = useState<GroupForm>(() => blankGroup(companyId)), [selection, setSelection] = useState<SelectionForm>(() => blankSelection(companyId));
  const [loading, setLoading] = useState(false), [error, setError] = useState('');
  const action = useAction();
  const results = report?.resultSets.flatMap(set => set.results) || [];
  useEffect(() => {
    if (!active || !allowed) return;
    let current = true;
    setLoading(true); setError('');
    void Promise.all([api<{ items: SourceGroup[] }>('/api/residue-source-groups'), api<{ items: ReportSelection[] }>('/api/residue-report-selections'), loadAll<Substance>('/api/active-substances')])
      .then(([groupPage, selectionPage, substanceItems]) => { if (current) { setGroups(groupPage.items); setSelections(selectionPage.items); setSubstances(substanceItems); } })
      .catch(cause => { if (current) setError(errorMessage(cause)); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [active, allowed, companyId]);
  async function publishGroup(event: FormEvent) {
    event.preventDefault();
    if (!allowed || group.decimalPlaces == null) return;
    await action.run(async () => {
      await api('/api/residue-source-groups', { method: 'POST', body: JSON.stringify({ ...group, sourceIds: group.sourceIds.map(value => value.trim()).filter(Boolean), effectiveFrom: isoDate(group.effectiveFrom), effectiveTo: group.effectiveTo ? isoDate(group.effectiveTo) : null, reviewedAt: isoDate(group.reviewedAt) }) });
      setGroups((await api<{ items: SourceGroup[] }>('/api/residue-source-groups')).items);
      setGroup(blankGroup(companyId));
    }, 'Reviewed fallback evidence version published.');
  }
  async function publishSelection(event: FormEvent) {
    event.preventDefault();
    if (!allowed) return;
    await action.run(async () => {
      await api('/api/residue-report-selections', { method: 'POST', body: JSON.stringify({ ...selection, supersededResultIds: selection.supersededResultIds.map(value => value.trim()).filter(Boolean), effectiveFrom: isoDate(selection.effectiveFrom), effectiveTo: selection.effectiveTo ? isoDate(selection.effectiveTo) : null, reviewedAt: isoDate(selection.reviewedAt) }) });
      setSelections((await api<{ items: ReportSelection[] }>('/api/residue-report-selections')).items);
      setSelection(blankSelection(companyId));
    }, 'Reviewed report-result selection published.');
  }
  const availableResults = (report?.resultSets || []).filter(set => set.sourceLotId === selection.sourceId).flatMap(set => set.results).filter(result => result.id && result.substanceId === selection.substanceId);
  const setGroupValue = <K extends keyof GroupForm>(key: K, value: GroupForm[K]) => setGroup(current => ({ ...current, [key]: value }));
  const setSelectionValue = <K extends keyof SelectionForm>(key: K, value: SelectionForm[K]) => setSelection(current => ({ ...current, [key]: value }));
  return <section className="panel" aria-labelledby="residue-evidence-title">
    <h2 id="residue-evidence-title">F12 fallback and report evidence</h2>
    <p>Only confirmed report results can be selected. These immutable reviewed records do not change original laboratory facts or VN comparison.</p>
    {loading && <p role="status">Loading published F12 evidence…</p>}{error && <p role="alert" className="notice error">{error}</p>}<Feedback {...action} />
    <details><summary>Published source-group fallback versions ({groups.length})</summary>{groups.length ? <ul>{groups.map(item => <li key={item.id}>{item.code} v{item.versionNo} · {item.productStage} / {item.productIdentity} · {item.lower}–{item.upper} {item.unit} · {item.precedence} · {item.reviewAuthority}</li>)}</ul> : <p>No source-group fallback versions.</p>}</details>
    <details><summary>Reviewed report selections ({selections.length})</summary>{selections.length ? <ul>{selections.map(item => <li key={item.id}>{item.sourceId} · {item.substanceId} · result {item.selectedResultId} · {item.reviewAuthority}</li>)}</ul> : <p>No reviewed report selections.</p>}</details>
    <form onSubmit={publishGroup}><fieldset disabled={!allowed || action.busy}><legend>Publish source-group fallback</legend>
      <label>Group code<input required value={group.code} onChange={e => setGroupValue('code', e.target.value)} /></label><label>Version<input required type="number" min="1" step="1" value={group.versionNo} onChange={e => setGroupValue('versionNo', Number(e.target.value))} /></label>
      <label>Substance<select required value={group.substanceId} onChange={e => { setGroupValue('substanceId', e.target.value); setGroupValue('supportingResultIds', []); }}><option value="">Choose substance</option>{substances.map(s => <option key={s.id} value={s.id}>{s.code} · {s.name}</option>)}</select></label>
      <label>Explicit source IDs (comma-separated)<textarea required value={group.sourceIds.join(',')} onChange={e => setGroupValue('sourceIds', e.target.value.split(','))} /></label>
      {report && <p>Source IDs linked to this report: {Array.from(new Set(report.resultSets.map(set => set.sourceLotId).filter((value): value is string => !!value))).join(', ') || 'none'}</p>}
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
    {report && <form onSubmit={publishSelection}><fieldset disabled={!allowed || action.busy}><legend>Publish explicit report-result selection</legend>
      <label>Capture source ID<select required value={selection.sourceId} onChange={e => { setSelectionValue('sourceId', e.target.value); setSelectionValue('selectedResultId', ''); }}><option value="">Choose source linked to report</option>{Array.from(new Set(report.resultSets.map(set => set.sourceLotId).filter((value): value is string => !!value))).map(value => <option key={value} value={value}>{value}</option>)}</select></label>
      <label>Substance<select required value={selection.substanceId} onChange={e => { setSelectionValue('substanceId', e.target.value); setSelectionValue('selectedResultId', ''); }}><option value="">Choose substance</option>{substances.map(s => <option key={s.id} value={s.id}>{s.code} · {s.name}</option>)}</select></label>
      <label>Confirmed report result<select required value={selection.selectedResultId} onChange={e => setSelectionValue('selectedResultId', e.target.value)}><option value="">Choose result from this report</option>{report.status === 'CONFIRMED' && availableResults.map(result => <option key={result.id} value={result.id}>{result.testLabel} · {result.originalResultText} · {result.resultKind} · {result.id}</option>)}</select></label>
      <label>Superseded result IDs (comma-separated)<textarea value={selection.supersededResultIds.join(',')} onChange={e => setSelectionValue('supersededResultIds', e.target.value.split(','))} /></label>
      <label>Material basis<input required value={selection.materialBasis} onChange={e => setSelectionValue('materialBasis', e.target.value)} /></label><label>Residue definition<input required value={selection.residueDefinition} onChange={e => setSelectionValue('residueDefinition', e.target.value)} /></label>
      <label>Reviewed absolute uncertainty (optional exact decimal)<input inputMode="decimal" value={selection.absoluteUncertainty || ''} onChange={e => setSelectionValue('absoluteUncertainty', e.target.value || null)} /></label><label>Reviewed reporting threshold (optional exact decimal)<input inputMode="decimal" value={selection.reportingThreshold || ''} onChange={e => setSelectionValue('reportingThreshold', e.target.value || null)} /></label>
      <label>Effective from<input required type="datetime-local" value={selection.effectiveFrom} onChange={e => setSelectionValue('effectiveFrom', e.target.value)} /></label><label>Effective until<input type="datetime-local" value={selection.effectiveTo || ''} onChange={e => setSelectionValue('effectiveTo', e.target.value || null)} /></label><label>Supersedes selection ID<input value={selection.supersedesId || ''} onChange={e => setSelectionValue('supersedesId', e.target.value || null)} /></label>
      <label>Evidence reference<input required value={selection.evidenceRef} onChange={e => setSelectionValue('evidenceRef', e.target.value)} /></label><label>Applicability statement<textarea required value={selection.applicabilityStatement} onChange={e => setSelectionValue('applicabilityStatement', e.target.value)} /></label><label>Scientific reviewer<input required value={selection.reviewedBy} onChange={e => setSelectionValue('reviewedBy', e.target.value)} /></label><label>Review authority<input required value={selection.reviewAuthority} onChange={e => setSelectionValue('reviewAuthority', e.target.value)} /></label><label>Reviewed at<input required type="datetime-local" value={selection.reviewedAt} onChange={e => setSelectionValue('reviewedAt', e.target.value)} /></label>
      <button disabled={action.busy || report.status !== 'CONFIRMED' || !selection.selectedResultId}>{action.busy ? 'Publishing…' : 'Publish reviewed selection'}</button>
    </fieldset></form>}
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
