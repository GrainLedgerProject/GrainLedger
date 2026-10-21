import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, errorMessage } from './api';
import { newRequest, type Envelope, wasRejected } from './captureWrite';
import { labCsvHeaders, parseLabCsv, type LabCsvPreview, type LabCsvRow } from './labCsv';
import type { Page } from './procurementTypes';

type ReportKind = 'PARTIAL' | 'FINAL' | 'SUPPLEMENTARY' | 'AMENDMENT' | 'RETEST';
type AssessmentStatus = 'NOT_ASSESSED' | 'WITHIN_ASSESSED_LIMITS' | 'EXCEEDS_LIMIT' | 'INSUFFICIENT_EVIDENCE' | 'NO_MRL_IN_VN';
type ResultSet = { id: string; sampleId: string | null; confirmedSampleId: string | null; sourceLotId: string | null; laboratorySampleReference: string | null; statedSampleCode: string | null; statedSealCode: string | null; sectionReference: string | null; identityStatus?: string; results: LabCsvRow[] };
export type Report = { id: string; laboratoryName: string; reportNumber: string; issuedOn: string; receivedAt: string; reportKind: ReportKind; status: 'UNRESOLVED' | 'PENDING_REVIEW' | 'CONFIRMED'; fileName: string; contentType: string; fileSize: number; sha256: string; recordedBy: string; capturedAt: string; recordedAt: string; confirmedBy: string | null; confirmedAt: string | null; resultSets: ResultSet[]; laboratoryConclusion?: string | null; statedStandard?: string | null; assessmentStatus: AssessmentStatus; suggestedMarketCode: string; assessmentReasons: string[]; relatedReportId?: string | null; relationshipReason?: string | null; relatedReports?: Array<{ id: string; reportKind: ReportKind; relationshipReason: string | null }> };
type SampleOption = { id: string; code: string; sealCode: string; sourceLotId: string; materialDescription?: string | null };
type ConfirmPayload = { identityChecked: true; transcriptionChecked: true; matches: Array<{ resultSetId: string; sampleId: string; identityBasis: 'SAMPLE_CODE' | 'SEAL_CODE' | 'LAB_REFERENCE'; evidenceRef?: string }> };
type IntakePayload = { entityId: string; laboratoryName: string; reportNumber: string; issuedOn: string; receivedAt: string; reportKind: ReportKind; relatedReportId?: string; relationshipReason?: string; fileName: string; contentType: string; contentBase64: string; companyOnlyConfirmed: true; laboratoryConclusion?: string; statedStandard?: string; resultSets: Array<{ entityId: string; sampleId?: string; laboratorySampleReference?: string; statedSampleCode?: string; statedSealCode?: string; sectionReference?: string; results: LabCsvRow[] }> };

const resultKinds = ['NUMERIC', 'BELOW_LIMIT', 'NOT_DETECTED', 'QUALITATIVE', 'OTHER'] as const;
const blankRow = (): LabCsvRow => ({ testLabel: '', originalResultText: '', resultKind: 'OTHER' });

function dateTime(value: string) { const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16); }
function encodeBase64(file: File) { return new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onerror = () => reject(new Error('Could not read selected report file.')); reader.onload = () => resolve(String(reader.result).split(',')[1] ?? ''); reader.readAsDataURL(file); }); }

function assessText(report: Report) {
  const market = report.suggestedMarketCode || 'suggested market';
  switch (report.assessmentStatus) {
    case 'EXCEEDS_LIMIT': return `Exceeds assessed ${market} limit`;
    case 'WITHIN_ASSESSED_LIMITS': return `Within assessed ${market} limits`;
    case 'NO_MRL_IN_VN': return `No applicable VN MRL found for ${market}`;
    case 'INSUFFICIENT_EVIDENCE': return `Insufficient evidence for ${market}`;
    default: return 'Not assessed';
  }
}

function suggestedIdentityBasis(set: ResultSet, sample?: SampleOption): ConfirmPayload['matches'][number]['identityBasis'] {
  if (sample && set.statedSampleCode?.trim() && sample.code.trim() === set.statedSampleCode.trim()) return 'SAMPLE_CODE';
  if (sample && set.statedSealCode?.trim() && sample.sealCode.trim() === set.statedSealCode.trim()) return 'SEAL_CODE';
  if (set.laboratorySampleReference?.trim()) return 'LAB_REFERENCE';
  return 'SAMPLE_CODE';
}

function identityComparisons(set: ResultSet, sample?: SampleOption) {
  if (!sample) return [];
  return [
    ['Sample code', set.statedSampleCode, sample.code],
    ['Seal code', set.statedSealCode, sample.sealCode],
  ].filter(([, printed]) => !!String(printed ?? '').trim()).map(([label, printed, actual]) => ({
    label: String(label),
    printed: String(printed).trim(),
    actual: String(actual),
    matches: String(printed).trim() === String(actual).trim(),
  }));
}


function statusPillClass(status: Report['status']) {
  if (status === 'CONFIRMED') return 'lab-pill ok';
  if (status === 'PENDING_REVIEW') return 'lab-pill warn';
  return 'lab-pill danger';
}

function assessmentPillClass(status: AssessmentStatus) {
  if (status === 'WITHIN_ASSESSED_LIMITS') return 'lab-pill ok';
  if (status === 'EXCEEDS_LIMIT') return 'lab-pill danger';
  if (status === 'NO_MRL_IN_VN' || status === 'INSUFFICIENT_EVIDENCE') return 'lab-pill warn';
  return 'lab-pill neutral';
}

export function LabReports({ canWrite, sampleId, sourceLotId, onOpenSample, onOpenSourceLot }: { canWrite: boolean; sampleId?: string; sourceLotId?: string; onOpenSample?: (id: string) => void; onOpenSourceLot?: (id: string) => void }) {
  const [page, setPage] = useState(1), [retry, setRetry] = useState(0), [list, setList] = useState<Page<Report> | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState(''), [selected, setSelected] = useState<string | null>(null), [creating, setCreating] = useState(false);
  const [statusFilter, setStatusFilter] = useState<'ALL' | Report['status']>('ALL'), [searchQuery, setSearchQuery] = useState('');
  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    setLoading(true); setError('');
    const params = new URLSearchParams({ page: String(page), pageSize: '20' });
    if (sampleId) params.set('sampleId', sampleId);
    if (sourceLotId) params.set('sourceLotId', sourceLotId);
    void api<Page<Report>>(`/api/lab-reports?${params}`, { signal: controller.signal })
      .then(value => { if (active) setList(value); })
      .catch(cause => { if (active) setError(errorMessage(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [page, retry, sampleId, sourceLotId]);

  if (selected) return <ReportDetail key={selected} id={selected} canWrite={canWrite} onBack={() => { setSelected(null); setRetry(value => value + 1); }} onOpenReport={setSelected} onOpenSample={onOpenSample} onOpenSourceLot={onOpenSourceLot} />;

  const items = list?.items ?? [];
  const confirmedCount = items.filter(r => r.status === 'CONFIRMED').length;
  const pendingCount = items.filter(r => r.status !== 'CONFIRMED').length;
  const assessedCount = items.filter(r => r.assessmentStatus === 'WITHIN_ASSESSED_LIMITS').length;
  const filteredItems = items.filter(r => {
    if (statusFilter !== 'ALL' && r.status !== statusFilter) return false;
    if (!searchQuery.trim()) return true;
    const q = searchQuery.toLocaleLowerCase();
    return `${r.reportNumber} ${r.laboratoryName} ${r.issuedOn} ${r.reportKind}`.toLocaleLowerCase().includes(q);
  });

  return <div className="lab-workspace">
    <header className="lab-hero">
      <div>
        <span className="eyebrow light">LABORATORY REPORT REGISTER</span>
        <h1>Laboratory reports</h1>
        <p>Preserve original certificates and transcribe sample-specific residue panels. Assessment status and reasons come from the server.</p>
      </div>
      <div className="lab-hero-actions">
        {canWrite && <button type="button" onClick={() => setCreating(true)}>+ Receive report</button>}
      </div>
    </header>

    <div className="lab-kpi-strip" aria-label="Laboratory register summary">
      <div className="lab-kpi-card"><span>Loaded reports</span><strong>{items.length}</strong><small>Page {list?.page ?? 1} register</small></div>
      <div className="lab-kpi-card"><span>Confirmed evidence</span><strong>{confirmedCount}</strong><small>Bound to source lots</small></div>
      <div className="lab-kpi-card"><span>Awaiting review</span><strong>{pendingCount}</strong><small>Pending or unresolved identity</small></div>
      <div className="lab-kpi-card"><span>Within assessed limits</span><strong>{assessedCount}</strong><small>Backend assessment status</small></div>
    </div>

    <section className="panel capture-section" style={{ maxWidth: '100%', marginTop: 0 }}>
      <div className="section-toolbar">
        <div>
          <h2>{sampleId ? 'Reports for sample' : sourceLotId ? 'Reports for source lot' : 'Report register'}</h2>
          <p className="muted">Unresolved identity stays separate from confirmed source-lot evidence.</p>
        </div>
      </div>
      {items.length > 0 && <div className="lab-filter-bar">
        <div className="lab-filter-pills" role="group" aria-label="Filter reports by review status">
          {(['ALL', 'CONFIRMED', 'PENDING_REVIEW', 'UNRESOLVED'] as const).map(st => (
            <button key={st} type="button" className={`lab-filter-pill ${statusFilter === st ? 'active' : ''}`} onClick={() => setStatusFilter(st)}>
              {st === 'ALL' ? `All (${items.length})` : st === 'CONFIRMED' ? `Confirmed (${confirmedCount})` : st === 'PENDING_REVIEW' ? 'Pending review' : 'Unresolved'}
            </button>
          ))}
        </div>
        <input className="lab-search-input" type="search" value={searchQuery} onChange={e => setSearchQuery(e.target.value)} placeholder="Filter loaded reports by number or lab…" aria-label="Filter loaded reports" />
      </div>}
      {loading && <p role="status">Loading reports…</p>}
      {error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(n => n + 1)}>Try again</button></div>}
      {list && (filteredItems.length ? <div className="table-scroll"><table><thead><tr><th>Report</th><th>Laboratory</th><th>Issued</th><th>Type</th><th>Samples / Analytes</th><th>Review</th><th>Assessment</th></tr></thead><tbody>{filteredItems.map(report => {
        const totalRows = (report.resultSets ?? []).reduce((acc, s) => acc + (s.results?.length ?? 0), 0);
        const sampleLabel = (report.resultSets ?? []).map(s => s.statedSampleCode?.trim()).filter(Boolean).join(', ') || `${report.resultSets?.length ?? 0} section(s)`;
        return <tr key={report.id}>
          <td><button className="text-button" onClick={() => setSelected(report.id)}>{report.reportNumber} ↗</button></td>
          <td>{report.laboratoryName}</td>
          <td className="mono-cell">{report.issuedOn}</td>
          <td><span className="badge">{report.reportKind}</span></td>
          <td><span className="mono-cell">{sampleLabel}</span> <span className="muted">({totalRows} rows)</span></td>
          <td><span className={statusPillClass(report.status)}>{report.status}</span></td>
          <td><span className={assessmentPillClass(report.assessmentStatus)}>{assessText(report)}</span></td>
        </tr>;
      })}</tbody></table></div> : <p className="empty">No laboratory reports found.</p>)}
      {list && <div className="farmer-pagination"><span>Page {list.page}</span><button className="secondary" disabled={page === 1 || loading} onClick={() => setPage(p => p - 1)}>Previous</button><button className="secondary" disabled={!list.hasMore || loading} onClick={() => setPage(p => p + 1)}>Next</button></div>}
    </section>
    {creating && <ReportIntake canWrite={canWrite} initialSampleId={sampleId} onCancel={() => setCreating(false)} onCreated={id => { setCreating(false); setSelected(id); }} />}
  </div>;
}

function ReportChooser({ value, onChange, disabled }: { value: string; onChange: (id: string) => void; disabled: boolean }) {
  const [page, setPage] = useState(1), [items, setItems] = useState<Report[]>([]), [hasMore, setHasMore] = useState(false), [loading, setLoading] = useState(false), [error, setError] = useState(''), [query, setQuery] = useState('');
  useEffect(() => { let active = true; const controller = new AbortController(); setLoading(true); setError(''); void api<Page<Report>>(`/api/lab-reports?page=${page}&pageSize=20`, { signal: controller.signal }).then(result => { if (active) { setItems(current => [...current.filter(item => !result.items.some(next => next.id === item.id)), ...result.items]); setHasMore(result.hasMore); } }).catch(cause => { if (active) setError(errorMessage(cause)); }).finally(() => { if (active) setLoading(false); }); return () => { active = false; controller.abort(); }; }, [page]);
  useEffect(() => { if (!value || items.some(item => item.id === value)) return; let active = true; void api<Report>(`/api/lab-reports/${encodeURIComponent(value)}`).then(report => { if (active) setItems(current => current.some(item => item.id === report.id) ? current : [report, ...current]); }).catch(cause => { if (active) setError(errorMessage(cause)); }); return () => { active = false; }; }, [value, items]);
  const visible = items.filter(report => report.id === value || `${report.reportNumber} ${report.laboratoryName} ${report.issuedOn} ${report.reportKind}`.toLocaleLowerCase().includes(query.toLocaleLowerCase()));
  return <div className="sample-chooser"><label>Search loaded reports<input type="search" value={query} onChange={event => setQuery(event.target.value)} disabled={disabled} placeholder="Search loaded reports" /></label><select aria-label="Related report" value={value} onChange={event => onChange(event.target.value)} disabled={disabled}><option value="">Choose report</option>{visible.map(report => <option key={report.id} value={report.id}>{report.reportNumber} · {report.laboratoryName} · {report.issuedOn} · {report.reportKind}</option>)}</select>{loading && <p role="status">Loading reports…</p>}{error && <p className="field-validation" role="alert">{error}</p>}{hasMore && <button className="secondary" type="button" disabled={loading || disabled} onClick={() => setPage(current => current + 1)}>Load more reports</button>}</div>;
}

function SampleChooser({ value, onChange, onSelectSample, onLoadSelectedSample, disabled, label }: { value: string; onChange: (id: string) => void; onSelectSample?: (sample: SampleOption | undefined) => void; onLoadSelectedSample?: (sample: SampleOption) => void; disabled?: boolean; label: string }) {
  const onLoadSelectedSampleRef = useRef(onLoadSelectedSample);
  onLoadSelectedSampleRef.current = onLoadSelectedSample;
  const [page, setPage] = useState(1), [items, setItems] = useState<SampleOption[]>([]), [hasMore, setHasMore] = useState(false), [loading, setLoading] = useState(false), [error, setError] = useState(''), [query, setQuery] = useState('');
  useEffect(() => {
    let active = true;
    setLoading(true); setError('');
    void api<Page<SampleOption>>(`/api/samples?page=${page}&pageSize=20`).then(result => {
      if (active) { setItems(current => [...current.filter(item => !result.items.some(next => next.id === item.id)), ...result.items]); setHasMore(result.hasMore); }
    }).catch(cause => { if (active) setError(errorMessage(cause)); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [page]);
  useEffect(() => {
    if (!value) return;
    const loaded = items.find(item => item.id === value);
    if (loaded) {
      onLoadSelectedSampleRef.current?.(loaded);
      return;
    }
    let active = true;
    void api<SampleOption>(`/api/samples/${encodeURIComponent(value)}`).then(item => {
      if (active) {
        setItems(current => current.some(existing => existing.id === item.id) ? current : [item, ...current]);
        onLoadSelectedSampleRef.current?.(item);
      }
    }).catch(cause => { if (active) setError(errorMessage(cause)); });
    return () => { active = false; };
  }, [value, items]);
  const visible = items.filter(item => item.id === value || `${item.code} ${item.sealCode} ${item.materialDescription ?? ''}`.toLocaleLowerCase().includes(query.toLocaleLowerCase()));
  return <div className="sample-chooser"><label>{label}<input type="search" value={query} onChange={event => setQuery(event.target.value)} disabled={disabled} placeholder="Search loaded samples" /></label><select aria-label={`${label} selection`} value={value} onChange={event => { const nextId = event.target.value; onChange(nextId); onSelectSample?.(items.find(i => i.id === nextId)); }} disabled={disabled}><option value="">Unresolved — select only when identity is established</option>{visible.map(item => <option value={item.id} key={item.id}>{item.code} · seal {item.sealCode}</option>)}</select>{loading && <p role="status">Loading samples…</p>}{error && <p className="field-validation" role="alert">{error}</p>}{hasMore && <button className="secondary" type="button" disabled={loading || disabled} onClick={() => setPage(current => current + 1)}>Load more samples</button>}</div>;
}

function ReportIntake({ canWrite, initialSampleId, initialLaboratory, relatedReportId, initialKind, onCancel, onCreated }: { canWrite: boolean; initialSampleId?: string; initialLaboratory?: string; relatedReportId?: string; initialKind?: ReportKind; onCancel: () => void; onCreated: (id: string) => void }) {
  const [sets, setSets] = useState([{ entityId: crypto.randomUUID(), sampleId: initialSampleId ?? '', laboratorySampleReference: '', statedSampleCode: '', statedSealCode: '', sectionReference: '', results: [blankRow()] }]);
  const [laboratoryName, setLaboratoryName] = useState(initialLaboratory ?? ''), [reportNumber, setReportNumber] = useState(''), [issuedOn, setIssuedOn] = useState(''), [receivedAt, setReceivedAt] = useState(dateTime(new Date().toISOString())), [reportKind, setReportKind] = useState<ReportKind>(initialKind ?? 'FINAL'), [relationshipReason, setRelationshipReason] = useState(''), [relatedId, setRelatedId] = useState(relatedReportId ?? ''), [linkEarlier, setLinkEarlier] = useState(!!relatedReportId || ['SUPPLEMENTARY','AMENDMENT','RETEST'].includes(initialKind ?? 'FINAL')), [laboratoryConclusion, setConclusion] = useState(''), [statedStandard, setStandard] = useState(''), [file, setFile] = useState<File | null>(null), [companyOnlyConfirmed, setCompanyOnly] = useState(false), [paste, setPaste] = useState(''), [previewSet, setPreviewSet] = useState(''), [preview, setPreview] = useState<LabCsvPreview | null>(null), [pending, setPending] = useState<Envelope<IntakePayload> | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState('');
  const updateSet = (index: number, patch: Partial<(typeof sets)[number]>) => setSets(current => current.map((item, at) => at === index ? { ...item, ...patch } : item));
  const updateRow = (setIndex: number, rowIndex: number, patch: Partial<LabCsvRow>) => setSets(current => current.map((set, at) => at === setIndex ? { ...set, results: set.results.map((row, ri) => ri === rowIndex ? { ...row, ...patch } : row) } : set));
  function importPreview() { setPreview(parseLabCsv(paste)); }
  async function save(event: FormEvent) {
    event.preventDefault();
    if (busy || !canWrite) return;
    const linkedRequired = ['SUPPLEMENTARY', 'AMENDMENT', 'RETEST'].includes(reportKind);
    const relatedReportIdValue = (linkedRequired || linkEarlier) ? relatedId.trim() : '';
    if ((linkedRequired || linkEarlier) && !relatedReportIdValue) { setError('Choose a related report before saving.'); return; }
    if (relatedReportIdValue && !relationshipReason.trim()) { setError('Enter relationship reason for linked report.'); return; }
    if (!companyOnlyConfirmed) { setError('Confirm report file contains this company’s data only. Split or redact mixed-company reports before upload.'); return; }
    if (!file) { setError('Select original laboratory report file.'); return; }
    if (file.size === 0 || file.size > 10 * 1024 * 1024) { setError('Original report must be between 1 byte and 10 MiB.'); return; }
    if ((['SUPPLEMENTARY', 'AMENDMENT', 'RETEST'].includes(reportKind) || relatedReportIdValue) && (!relatedReportIdValue || !relationshipReason.trim())) { setError('Select related report ID and enter reason for supplement, amendment, retest, or linked report.'); return; }
    if (sets.some(set => !set.results.length || set.results.some(row => !row.testLabel.trim() || !row.originalResultText.trim() || ['NUMERIC', 'BELOW_LIMIT'].includes(row.resultKind) && !row.numericValue || [row.numericValue, row.lod, row.loq].some(value => value !== undefined && value !== '' && !/^[0-9]{1,12}(?:\.[0-9]{1,12})?$/.test(value))))) { setError('Every section needs result rows with test labels and original text; numeric values, LOD, and LOQ must use exact nonnegative decimal strings.'); return; }
    setBusy(true); setError('');
    try {
      const uuidRe = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
      let codeToId: Record<string, string> | null = null;
      if (!pending && sets.some(s => s.results.some(r => r.substanceId && !uuidRe.test(r.substanceId.trim())))) {
        const catalog = await api<Page<{ id: string; code: string; name: string }>>('/api/active-substances?page=1&pageSize=100');
        codeToId = {};
        for (const item of catalog.items) {
          codeToId[item.code.toUpperCase()] = item.id;
          codeToId[item.name.toUpperCase()] = item.id;
        }
      }
      const contentBase64 = pending?.payload.contentBase64 ?? await encodeBase64(file);
      const payload: IntakePayload = pending?.payload ?? {
        entityId: crypto.randomUUID(),
        laboratoryName: laboratoryName.trim(),
        reportNumber: reportNumber.trim(),
        issuedOn,
        receivedAt: new Date(receivedAt).toISOString(),
        reportKind,
        ...(relatedReportIdValue ? { relatedReportId: relatedReportIdValue } : {}),
        ...(relationshipReason.trim() ? { relationshipReason: relationshipReason.trim() } : {}),
        fileName: file.name,
        contentType: file.type || 'application/octet-stream',
        contentBase64,
        companyOnlyConfirmed: true,
        resultSets: sets.map(set => ({
          entityId: set.entityId,
          ...(set.sampleId ? { sampleId: set.sampleId } : {}),
          ...(set.laboratorySampleReference.trim() ? { laboratorySampleReference: set.laboratorySampleReference.trim() } : {}),
          ...(set.statedSampleCode.trim() ? { statedSampleCode: set.statedSampleCode.trim() } : {}),
          ...(set.statedSealCode.trim() ? { statedSealCode: set.statedSealCode.trim() } : {}),
          ...(set.sectionReference.trim() ? { sectionReference: set.sectionReference.trim() } : {}),
          results: set.results.map(r => {
            const raw = r.substanceId?.trim();
            if (!raw) return r;
            const resolved = uuidRe.test(raw) ? raw : codeToId?.[raw.toUpperCase()];
            if (!resolved) throw new Error(`Unknown substance code or ID: ${raw}`);
            return { ...r, substanceId: resolved };
          }),
        })),
      };
      if (!pending && laboratoryConclusion.trim()) payload.laboratoryConclusion = laboratoryConclusion.trim();
      if (!pending && statedStandard.trim()) payload.statedStandard = statedStandard.trim();
      const request = pending ?? newRequest(payload);
      setPending(request);
      const result = await api<{ id: string }>('/api/lab-reports', { method: 'POST', body: JSON.stringify(request) });
      onCreated(result.id);
    } catch (cause) {
      if (wasRejected(cause)) setPending(null);
      setError(errorMessage(cause));
    } finally { setBusy(false); }
  }
  return <div className="farmer-modal-backdrop"><section className="farmer-modal capture-modal-wide lab-dialog" role="dialog" aria-modal="true" aria-labelledby="lab-intake-title">
    <div className="farmer-modal-heading"><h2 id="lab-intake-title">{initialKind ? `${initialKind[0]}${initialKind.slice(1).toLowerCase()} report` : 'Receive laboratory report'}</h2><button className="farmer-modal-close" type="button" aria-label="Close report intake" disabled={busy} onClick={onCancel}>×</button></div>
    <p className="notice">A report containing another company’s data must be split or redacted first. Original bytes are retained as evidence. Upload does not change stock or HOLDs.</p>
    <form onSubmit={save}>
      <div className="form-columns"><label>Laboratory<input value={laboratoryName} onChange={e => setLaboratoryName(e.target.value)} required disabled={busy || !!pending} /></label><label>Report number<input value={reportNumber} onChange={e => setReportNumber(e.target.value)} required disabled={busy || !!pending} /></label><label>Issue date<input type="date" value={issuedOn} onChange={e => setIssuedOn(e.target.value)} required disabled={busy || !!pending} /></label><label>Received at<input type="datetime-local" value={receivedAt} onChange={e => setReceivedAt(e.target.value)} required disabled={busy || !!pending} /></label><label>Report kind<select value={reportKind} onChange={e => { const kind = e.target.value as ReportKind; setReportKind(kind); if (['SUPPLEMENTARY','AMENDMENT','RETEST'].includes(kind)) setLinkEarlier(true); }} disabled={busy || !!pending}>{['PARTIAL','FINAL','SUPPLEMENTARY','AMENDMENT','RETEST'].map(kind => <option key={kind}>{kind}</option>)}</select></label></div>
      {['FINAL','PARTIAL'].includes(reportKind) && <label className="checkbox-label"><input type="checkbox" checked={linkEarlier} onChange={e => { setLinkEarlier(e.target.checked); if (!e.target.checked) { setRelatedId(''); setRelationshipReason(''); } }} disabled={busy || !!pending} /> Link to an earlier report</label>}
      {(linkEarlier || ['SUPPLEMENTARY','AMENDMENT','RETEST'].includes(reportKind)) && <><ReportChooser value={relatedId} onChange={setRelatedId} disabled={busy || !!pending} /><label>Relationship reason<textarea value={relationshipReason} onChange={e => setRelationshipReason(e.target.value)} required disabled={busy || !!pending} /></label></>}
      <label>Original report file<input type="file" required disabled={busy || !!pending} onChange={e => setFile(e.target.files?.[0] ?? null)} /></label><label className="checkbox-label"><input type="checkbox" checked={companyOnlyConfirmed} onChange={e => setCompanyOnly(e.target.checked)} disabled={busy || !!pending} /> I checked: this original file contains this company’s data only.</label>
      <details><summary>Optional report details</summary><div className="form-columns"><label>Laboratory conclusion <span className="optional">(as stated)</span><textarea value={laboratoryConclusion} onChange={e => setConclusion(e.target.value)} disabled={busy || !!pending} /></label><label>Stated standard/version <span className="optional">(as stated)</span><textarea value={statedStandard} onChange={e => setStandard(e.target.value)} disabled={busy || !!pending} /></label></div></details>
      <h3>Sample-specific sections</h3>{sets.map((set, index) => <fieldset className="lab-result-set" key={set.entityId}><legend>Section {index + 1}</legend><div className="form-columns"><SampleChooser label={`Registered sample for section ${index + 1}`} value={set.sampleId} onChange={sampleId => updateSet(index, { sampleId })} onSelectSample={smp => { if (smp) updateSet(index, { sampleId: smp.id, statedSampleCode: set.statedSampleCode.trim() || smp.code, statedSealCode: set.statedSealCode.trim() || smp.sealCode }); }} disabled={busy || !!pending} /><label>Lab sample reference<input value={set.laboratorySampleReference} onChange={e => updateSet(index, { laboratorySampleReference: e.target.value })} onBlur={e => updateSet(index, { laboratorySampleReference: e.target.value.trim() })} disabled={busy || !!pending} /></label><label>Stated sample code<input value={set.statedSampleCode} onChange={e => updateSet(index, { statedSampleCode: e.target.value })} onBlur={e => updateSet(index, { statedSampleCode: e.target.value.trim() })} disabled={busy || !!pending} /></label><label>Stated seal code<input value={set.statedSealCode} onChange={e => updateSet(index, { statedSealCode: e.target.value })} onBlur={e => updateSet(index, { statedSealCode: e.target.value.trim() })} disabled={busy || !!pending} /></label><label>Report section/page reference<input value={set.sectionReference} onChange={e => updateSet(index, { sectionReference: e.target.value })} onBlur={e => updateSet(index, { sectionReference: e.target.value.trim() })} disabled={busy || !!pending} /></label></div>
        <div className="admin-form-actions"><button className="secondary" type="button" disabled={busy || !!pending} onClick={() => { setPreviewSet(set.entityId); setPreview(null); }}>Import CSV / paste TSV</button></div><div className="table-scroll"><table><thead><tr><th>Test label</th><th>Substance ID</th><th>Original result</th><th>Qualifier</th><th>Exact numeric string</th><th>Unit</th><th>LOD</th><th>LOQ</th><th>Method</th><th>Uncertainty</th><th>Reference</th><th></th></tr></thead><tbody>{set.results.map((row, ri) => <tr key={ri}>{(['testLabel','substanceId','originalResultText'] as const).map(key => <td key={key}><input aria-label={`${key} section ${index + 1} row ${ri + 1}`} value={row[key] ?? ''} onChange={e => updateRow(index, ri, { [key]: key === 'substanceId' ? e.target.value || undefined : e.target.value })} disabled={busy || !!pending} /></td>)}<td><select aria-label={`Result kind section ${index + 1} row ${ri + 1}`} value={row.resultKind} onChange={e => updateRow(index, ri, { resultKind: e.target.value as LabCsvRow['resultKind'] })} disabled={busy || !!pending}>{resultKinds.map(kind => <option key={kind}>{kind}</option>)}</select></td>{(['numericValue','unit','lod','loq','method','uncertainty','reference'] as const).map(key => <td key={key}><input aria-label={`${key} section ${index + 1} row ${ri + 1}`} value={row[key] ?? ''} onChange={e => updateRow(index, ri, { [key]: e.target.value || undefined })} disabled={busy || !!pending} /></td>)}<td><button className="secondary" type="button" aria-label={`Remove result row ${ri + 1}`} disabled={busy || set.results.length === 1 || !!pending} onClick={() => updateSet(index, { results: set.results.filter((_, at) => at !== ri) })}>Remove</button></td></tr>)}</tbody></table></div>
        <div className="admin-form-actions"><button className="secondary" type="button" disabled={busy || !!pending} onClick={() => updateSet(index, { results: [...set.results, blankRow()] })}>+ Add result row</button>{sets.length > 1 && <button className="secondary" type="button" disabled={busy || !!pending} onClick={() => { if (previewSet === set.entityId) { setPreviewSet(''); setPreview(null); } setSets(current => current.filter(item => item.entityId !== set.entityId)); }}>Remove section</button>}</div>
      </fieldset>)}<button className="secondary" type="button" disabled={busy || !!pending} onClick={() => setSets(current => [...current, { entityId: crypto.randomUUID(), sampleId: '', laboratorySampleReference: '', statedSampleCode: '', statedSealCode: '', sectionReference: '', results: [blankRow()] }])}>+ Add sample section</button>
      {previewSet !== '' && <section className="lab-import"><h3>CSV / TSV import preview</h3><p className="field-help">Headers: {labCsvHeaders.join(', ')}. CSV supports quoted fields and escaped quotes; tab-separated paste uses same headers. Numeric values stay text.</p><textarea aria-label="Paste laboratory result CSV or TSV" value={paste} onChange={e => setPaste(e.target.value)} rows={6} disabled={busy || !!pending} /><label>Or choose CSV file<input type="file" accept=".csv,.tsv,text/csv,text/tab-separated-values" onChange={async e => { const csvFile = e.target.files?.[0]; if (csvFile) setPaste(await csvFile.text()); }} disabled={busy || !!pending} /></label><div className="admin-form-actions"><button type="button" className="secondary" onClick={importPreview}>Preview rows</button><button type="button" className="secondary" onClick={() => { setPreview(null); setPreviewSet(''); }}>Close import</button></div>{preview && <>{preview.errors.length > 0 && <div className="notice error" role="alert"><ul>{preview.errors.map((item, i) => <li key={i}>{item}</li>)}</ul></div>}<p>{preview.rows.length} rows previewed; {preview.errors.length ? 'fix errors before adding' : 'ready to add'}.</p>{preview.rows.length > 0 && <div className="table-scroll"><table><thead><tr>{labCsvHeaders.map(header => <th key={header}>{header}</th>)}</tr></thead><tbody>{preview.rows.slice(0, 30).map((row, i) => <tr key={i}>{labCsvHeaders.map(header => <td key={header}>{String((row as unknown as Record<string, unknown>)[header] ?? '')}</td>)}</tr>)}</tbody></table></div>}{preview.rows.length > 30 && <p>Showing 30 of {preview.rows.length} rows.</p>}<button type="button" disabled={!sets.some(set => set.entityId === previewSet) || !!preview.errors.length || !preview.rows.length || !!pending} onClick={() => { const parsed = preview.rows; setSets(current => current.map(set => set.entityId === previewSet ? { ...set, results: [...set.results.filter(row => row.testLabel || row.originalResultText), ...parsed] } : set)); setPreviewSet(''); }}>Add previewed rows</button></>}</section>}
      {pending && <div className="notice pending">Submission outcome uncertain. Retry sends exact same metadata and report bytes from this open form.</div>}{error && <div className="notice error" role="alert">{error}</div>}<div className="form-footer"><button className="secondary" type="button" disabled={busy} onClick={onCancel}>Cancel</button><button disabled={busy || !laboratoryName.trim() || !reportNumber.trim() || !issuedOn || !companyOnlyConfirmed}>{busy ? 'Uploading…' : pending ? 'Retry exact submission' : 'Save report evidence'}</button></div>
    </form></section></div>;
}

function ReportDetail({ id, canWrite, onBack, onOpenReport, onOpenSample, onOpenSourceLot }: { id: string; canWrite: boolean; onBack: () => void; onOpenReport: (id: string) => void; onOpenSample?: (id: string) => void; onOpenSourceLot?: (id: string) => void }) {
  const [report, setReport] = useState<Report | null>(null), [error, setError] = useState(''), [retry, setRetry] = useState(0), [confirmOpen, setConfirmOpen] = useState(false), [identityChecked, setIdentity] = useState(false), [transcriptionChecked, setTranscription] = useState(false), [matches, setMatches] = useState<Record<string, { sampleId: string; identityBasis: string; evidenceRef: string }>>({}), [selectedSamples, setSelectedSamples] = useState<Record<string, SampleOption>>({}), [substances, setSubstances] = useState<Record<string, { code: string; name: string }>>({}), [confirmPending, setConfirmPending] = useState<Envelope<ConfirmPayload> | null>(null), [busy, setBusy] = useState(false), [mutationError, setMutationError] = useState(''), [relationship, setRelationship] = useState<ReportKind | null>(null);
  useEffect(() => {
    let active = true;
    void (async () => {
      const catalog: Record<string, { code: string; name: string }> = {};
      try {
        let page = 1, hasMore = true;
        while (hasMore) {
          const result = await api<Page<{ id: string; code: string; name: string }>>(`/api/active-substances?page=${page}&pageSize=100`);
          for (const item of result.items) {
            const info = { code: item.code, name: item.name };
            catalog[item.id] = info;
            catalog[item.code.toUpperCase()] = info;
            catalog[item.name.toUpperCase()] = info;
          }
          hasMore = result.hasMore;
          page += 1;
        }
        if (active) setSubstances(catalog);
      } catch {
        if (active) setSubstances({});
      }
    })();
    return () => { active = false; };
  }, [id]);
  useEffect(() => {
    let active = true;
    void api<Report>(`/api/lab-reports/${encodeURIComponent(id)}`)
      .then(value => { if (active) setReport(value); })
      .catch(cause => { if (active) setError(errorMessage(cause)); });
    return () => { active = false; };
  }, [id, retry]);

  async function confirm(event: FormEvent) {
    event.preventDefault();
    if (!report || !identityChecked || !transcriptionChecked) return;
    if (report.resultSets.some(set => !(matches[set.id]?.sampleId || set.sampleId))) {
      setMutationError('Select a registered sample for every result section before confirmation.');
      return;
    }
    setBusy(true); setMutationError('');
    const payload = confirmPending?.payload ?? {
      identityChecked: true as const,
      transcriptionChecked: true as const,
      matches: report.resultSets.map(set => ({
        resultSetId: set.id,
        sampleId: matches[set.id]?.sampleId || set.sampleId || '',
        identityBasis: (matches[set.id]?.identityBasis || 'SAMPLE_CODE') as ConfirmPayload['matches'][number]['identityBasis'],
        ...(matches[set.id]?.evidenceRef?.trim() ? { evidenceRef: matches[set.id].evidenceRef.trim() } : {}),
      })),
    };
    const request = confirmPending ?? newRequest(payload);
    setConfirmPending(request);
    try {
      await api(`/api/lab-reports/${id}/confirm`, { method: 'POST', body: JSON.stringify(request) });
      setConfirmPending(null);
      setConfirmOpen(false);
      setRetry(n => n + 1);
    } catch (cause) {
      if (wasRejected(cause)) setConfirmPending(null);
      setMutationError(errorMessage(cause));
    } finally { setBusy(false); }
  }

  async function download() {
    try {
      const response = await fetch(`/api/lab-reports/${encodeURIComponent(id)}/file`, { credentials: 'include' });
      if (!response.ok) throw new Error('Original report download failed.');
      const blob = await response.blob(), link = document.createElement('a');
      link.href = URL.createObjectURL(blob);
      link.download = report?.fileName || 'laboratory-report';
      link.click();
      URL.revokeObjectURL(link.href);
    } catch (cause) { setMutationError(errorMessage(cause)); }
  }

  if (!report) return <><button className="text-button" onClick={onBack}>← Back to reports</button>{error ? <div role="alert" className="notice error">{error} <button className="text-button" onClick={() => setRetry(n => n + 1)}>Retry</button></div> : <p role="status">Loading report…</p>}</>;

  const effectiveSampleIds = report.resultSets.map(set => set.confirmedSampleId ?? set.sampleId);
  const initialSampleId = effectiveSampleIds.length > 0 && effectiveSampleIds.every((sampleId): sampleId is string => !!sampleId) && new Set(effectiveSampleIds).size === 1 ? effectiveSampleIds[0] : undefined;
  const assessmentStatus = report.assessmentStatus;
  const assessmentReasons = report.assessmentReasons ?? [];
  const bannerClass = assessmentStatus === 'EXCEEDS_LIMIT'
    ? 'lab-assessment-banner danger'
    : assessmentStatus === 'WITHIN_ASSESSED_LIMITS'
      ? 'lab-assessment-banner'
      : 'lab-assessment-banner warn';
  const hasUntrimmedCodes = report.resultSets.some(s =>
    (s.statedSampleCode && s.statedSampleCode !== s.statedSampleCode.trim()) ||
    (s.statedSealCode && s.statedSealCode !== s.statedSealCode.trim())
  );

  return <div className="lab-workspace">
    <div><button className="text-button" onClick={onBack}>← Back to reports</button></div>

    <header className="lab-hero">
      <div>
        <span className="eyebrow light">LABORATORY DOSSIER · {report.reportKind}</span>
        <h1>{report.reportNumber}</h1>
        <p>{report.laboratoryName} · Issued {report.issuedOn} · Received {new Date(report.receivedAt).toLocaleString()}</p>
      </div>
      <div className="lab-hero-actions">
        <span className={statusPillClass(report.status)}>{report.status}</span>
        <button className="secondary" onClick={() => void download()}>Download original · {report.fileName}</button>
        {canWrite && report.status !== 'CONFIRMED' && <button onClick={() => { setMutationError(''); setConfirmOpen(true); }}>Review and confirm</button>}
        {canWrite && (['SUPPLEMENTARY','AMENDMENT','RETEST'] as const).map(kind => <button className="secondary" key={kind} onClick={() => setRelationship(kind)}>Create linked {kind.toLowerCase()}</button>)}
      </div>
    </header>
    <section className={bannerClass} aria-label="Server-provided assessment summary">
      <div className="lab-assessment-header">
        <div>
          <strong>{assessText(report)} — suggested market {report.suggestedMarketCode}.</strong>
          <p style={{ margin: '4px 0 0', fontSize: '12px' }}>
            {assessmentStatus === 'NOT_ASSESSED'
              ? 'No assessment is available. Report facts are shown without a compliance conclusion.'
              : 'Assessment is supplied by the server. It does not change stock or HOLD state.'}
          </p>
        </div>
        <span className={assessmentPillClass(assessmentStatus)}>{assessmentStatus}</span>
      </div>
      {assessmentReasons.length > 0 && (
        <ul className="lab-assessment-reasons">
          {assessmentReasons.map((reason, idx) => <li key={idx}>{reason}</li>)}
        </ul>
      )}
    </section>

    {hasUntrimmedCodes && report.status !== 'CONFIRMED' && (
      <div className="notice pending">
        <strong>Whitespace detected in printed sample/seal code.</strong> Legacy padded codes may differ from displayed trimmed values; review both exact report and registered sample before confirmation.
      </div>
    )}

    {mutationError && !confirmOpen && <div role="alert" className="notice error">{mutationError}</div>}

    <section className="panel capture-detail" style={{ maxWidth: '100%', marginTop: 0 }}>
      <div className="lab-meta-grid">
        <div className="lab-meta-card">
          <h3>Certificate &amp; Custody</h3>
          <dl>
            <dt>Issue date</dt><dd>{report.issuedOn}</dd>
            <dt>Received</dt><dd>{new Date(report.receivedAt).toLocaleString()}</dd>
            <dt>Confirmed by</dt><dd>{report.confirmedBy || 'Not confirmed'}</dd>
            <dt>Confirmed at</dt><dd>{report.confirmedAt ? new Date(report.confirmedAt).toLocaleString() : 'Not confirmed'}</dd>
            <dt>Relationship</dt><dd>{report.relatedReportId ? `${report.reportKind} of ${report.relatedReportId}: ${report.relationshipReason || 'No reason recorded'}` : 'Original report'}</dd>
          </dl>
        </div>
        <div className="lab-meta-card">
          <h3>File Integrity &amp; Scope</h3>
          <dl>
            <dt>Original file size</dt><dd>{report.fileSize} bytes ({report.contentType})</dd>
            <dt>Original file SHA-256</dt><dd className="mono-cell">{report.sha256}</dd>
            <dt>Original conclusion</dt><dd>{report.laboratoryConclusion || 'Not stated'}</dd>
            <dt>Stated standard/version</dt><dd>{report.statedStandard || 'Not stated'}</dd>
            <dt>Assessment</dt><dd>{assessText(report)}. {assessmentReasons.join(' · ') || 'No assessment reasons returned.'}</dd>
          </dl>
        </div>
      </div>
      <details style={{ marginTop: '8px' }}>
        <summary className="muted">Audit metadata (Recorded by &amp; capture timestamps)</summary>
        <dl style={{ marginTop: '8px' }}>
          <dt>Original file type</dt><dd>{report.contentType}</dd>
          <dt>Recorded by</dt><dd className="mono-cell">{report.recordedBy}</dd>
          <dt>Captured at</dt><dd>{new Date(report.capturedAt).toLocaleString()}</dd>
          <dt>Recorded at</dt><dd>{new Date(report.recordedAt).toLocaleString()}</dd>
        </dl>
      </details>

      {report.resultSets.map((set, index) => (
        <section key={set.id} className="lab-result-set">
          <div className="lab-result-set-header">
            <div>
              <h2 style={{ margin: 0 }}>Section {index + 1} · {set.sectionReference || 'Primary Analytical Panel'}</h2>
              <div className="lab-identity-chips">
                <span className="lab-identity-chip">Printed sample: <strong>{set.statedSampleCode?.trim() || '—'}</strong></span>
                <span className="lab-identity-chip">Printed seal: <strong>{set.statedSealCode?.trim() || '—'}</strong></span>
                <span className="lab-identity-chip">Lab ref: <strong>{set.laboratorySampleReference?.trim() || '—'}</strong></span>
              </div>
            </div>
            <div>
              {set.confirmedSampleId ? (
                <button className="secondary" type="button" onClick={() => onOpenSample?.(set.confirmedSampleId!)}>
                  ✓ Confirmed sample {set.statedSampleCode?.trim() || set.confirmedSampleId} ↗
                </button>
              ) : (
                <span className="lab-pill warn">
                  Unresolved · proposed {set.statedSampleCode?.trim() || set.sampleId || 'not recorded'}
                </span>
              )}
              {set.sourceLotId && onOpenSourceLot && <button className="secondary" type="button" onClick={() => onOpenSourceLot(set.sourceLotId!)}>Recorded source lot ↗</button>}
            </div>
          </div>
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Test parameter</th>
                  <th>Substance</th>
                  <th>Reported result</th>
                  <th>Kind</th>
                  <th>Numeric</th>
                  <th>Unit</th>
                  <th>LOD / LOQ</th>
                  <th>Method</th>
                  <th>Uncertainty</th>
                  <th>Reference</th>
                </tr>
              </thead>
              <tbody>
                {set.results.map((row, i) => {
                  const substanceKey = row.substanceId?.trim();
                  const info = substanceKey ? substances[substanceKey] ?? substances[substanceKey.toUpperCase()] : undefined;
                  return <tr key={i}>
                    <td><strong>{row.testLabel}</strong></td>
                    <td className="mono-cell">{info ? `${info.code} · ${info.name}` : (row.substanceId || 'Unmapped')}</td>
                    <td className="mono-cell">{row.originalResultText}</td>
                    <td><span className="badge">{row.resultKind}</span></td>
                    <td className="mono-cell">{row.numericValue || '—'}</td>
                    <td className="mono-cell">{row.unit || '—'}</td>
                    <td className="mono-cell">{row.lod || '—'} / {row.loq || '—'}</td>
                    <td>{row.method || '—'}</td>
                    <td className="mono-cell">{row.uncertainty || '—'}</td>
                    <td>{row.reference || '—'}</td>
                  </tr>;
                })}
              </tbody>
            </table>
          </div>
        </section>
      ))}
    </section>

    {report.relatedReports?.length ? <section className="panel"><h2>Related report history</h2><ul>{report.relatedReports.map(item => <li key={item.id}><button className="text-button" onClick={() => onOpenReport(item.id)}>{item.reportKind} report {item.id}</button> {item.relationshipReason}</li>)}</ul></section> : null}
    {relationship && <ReportIntake canWrite={canWrite} initialSampleId={initialSampleId} initialLaboratory={report.laboratoryName} relatedReportId={report.id} initialKind={relationship} onCancel={() => setRelationship(null)} onCreated={createdId => { setRelationship(null); onOpenReport(createdId); }} />}
    {confirmOpen && <div className="farmer-modal-backdrop"><section className="farmer-modal capture-modal-wide lab-dialog" role="dialog" aria-modal="true" aria-labelledby="confirm-report-title">
      <div className="farmer-modal-heading"><h2 id="confirm-report-title">Review report transcription</h2><button type="button" className="farmer-modal-close" aria-label="Close confirmation" disabled={busy} onClick={() => setConfirmOpen(false)}>×</button></div>
      <p className="muted">Check every printed sample identity against registered custody history. Confirmation binds the results to the sample’s procurement source lot.</p>
      <form onSubmit={confirm}>
        <label className="checkbox-label"><input type="checkbox" checked={identityChecked} onChange={e => setIdentity(e.target.checked)} /> I checked sample/seal identity against original report and custody evidence.</label>
        <label className="checkbox-label"><input type="checkbox" checked={transcriptionChecked} onChange={e => setTranscription(e.target.checked)} /> I checked all result text, qualifiers, units, limits, and references against original file.</label>
        {report.resultSets.map((set, i) => (
          <div className="lab-result-set" key={set.id}>
            <h3>Section {i + 1}</h3>
            <div className="lab-confirm-check-box">
              <strong>Printed on report section {i + 1}:</strong>
              <div className="lab-confirm-check-grid">
                <div><span>Printed Sample Code</span><strong>{set.statedSampleCode?.trim() || '—'}</strong></div>
                <div><span>Printed Seal Code</span><strong>{set.statedSealCode?.trim() || '—'}</strong></div>
                <div><span>Lab Sample Ref</span><strong>{set.laboratorySampleReference?.trim() || '—'}</strong></div>
              </div>
            </div>
            <SampleChooser
              label={`Verified registered sample for section ${i + 1}`}
              value={matches[set.id]?.sampleId || set.sampleId || ''}
              disabled={busy || !!confirmPending}
              onChange={sampleId => setMatches(current => ({ ...current, [set.id]: { sampleId, identityBasis: sampleId ? current[set.id]?.identityBasis || suggestedIdentityBasis(set, selectedSamples[set.id]) : suggestedIdentityBasis(set), evidenceRef: current[set.id]?.evidenceRef || '' } }))}
              onSelectSample={sample => {
                if (!sample) {
                  setSelectedSamples(current => { const next = { ...current }; delete next[set.id]; return next; });
                  return;
                }
                setSelectedSamples(current => ({ ...current, [set.id]: sample }));
                setMatches(current => ({ ...current, [set.id]: { sampleId: sample.id, identityBasis: suggestedIdentityBasis(set, sample), evidenceRef: current[set.id]?.evidenceRef || '' } }));
              }}
              onLoadSelectedSample={sample => {
                setSelectedSamples(current => ({ ...current, [set.id]: sample }));
                setMatches(current => ({ ...current, [set.id]: { sampleId: sample.id, identityBasis: suggestedIdentityBasis(set, sample), evidenceRef: current[set.id]?.evidenceRef || '' } }));
              }}
            />
            {selectedSamples[set.id] && <div className="lab-confirm-check-box" aria-label="Identity comparison">
              <strong>Selected sample comparison</strong>
              <div className="lab-confirm-check-grid">
                {identityComparisons(set, selectedSamples[set.id]).map(item => (
                  <div key={item.label}>
                    <span>{item.label} · {item.matches ? 'Match' : 'Contradiction'}</span>
                    <strong>Printed: {item.printed} · Registered: {item.actual}</strong>
                  </div>
                ))}
                {!set.statedSampleCode?.trim() && !set.statedSealCode?.trim() && <div><span>No printed sample/seal code</span><strong>{selectedSamples[set.id].code} · seal {selectedSamples[set.id].sealCode}</strong></div>}
              </div>
            </div>}
            <label>Identity basis
              <select
                value={matches[set.id]?.identityBasis || suggestedIdentityBasis(set, selectedSamples[set.id])}
                disabled={busy || !!confirmPending}
                onChange={e => setMatches(current => ({ ...current, [set.id]: { sampleId: current[set.id]?.sampleId || set.sampleId || '', identityBasis: e.target.value, evidenceRef: current[set.id]?.evidenceRef || '' } }))}
              >
                <option>SAMPLE_CODE</option>
                <option>SEAL_CODE</option>
                <option>LAB_REFERENCE</option>
              </select>
            </label>
            <label>Supporting evidence reference <span className="optional">(required for independently evidenced lab reference)</span>
              <input
                value={matches[set.id]?.evidenceRef || ''}
                disabled={busy || !!confirmPending}
                onChange={e => setMatches(current => ({ ...current, [set.id]: { sampleId: current[set.id]?.sampleId || set.sampleId || '', identityBasis: current[set.id]?.identityBasis || 'SAMPLE_CODE', evidenceRef: e.target.value } }))}
              />
            </label>
          </div>
        ))}
        {mutationError && <div role="alert" className="notice error">{mutationError}</div>}
        <div className="form-footer">
          <button type="button" className="secondary" disabled={busy} onClick={() => setConfirmOpen(false)}>Cancel</button>
          <button disabled={busy || !identityChecked || !transcriptionChecked}>{busy ? 'Confirming…' : confirmPending ? 'Retry exact confirmation' : 'Confirm checked record'}</button>
        </div>
      </form>
    </section></div>}
  </div>;
}
