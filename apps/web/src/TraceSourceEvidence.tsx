import { useEffect, useState } from 'react';
import { api, errorMessage } from './api';
import type { LabTraceOrigin } from './LedgerTraceability';
import type { Report } from './LabReports';
import { SampleList } from './Samples';
import { Panel } from './ledger-ui';

type ReportPage = { items: Report[]; page: number; hasMore: boolean };
const comparisonLabels: Record<string, string> = {
  NOT_ASSESSED: 'Not assessed', WITHIN_ASSESSED_LIMITS: 'Within compared limits', EXCEEDS_LIMIT: 'Exceeds a compared limit',
  INSUFFICIENT_EVIDENCE: 'Insufficient evidence', NO_MRL_IN_VN: 'No numeric VN limit found',
};

// This is a separate, explicitly requested evidence read. It does not change the
// captured graph/audit or pretend that mutable comparison results share its snapshot.
export function TraceSourceEvidence({ sourceRecordId, onClose, onTraceResult }: { sourceRecordId: string; onClose: () => void; onTraceResult?: (origin: LabTraceOrigin) => void }) {
  const [page, setPage] = useState(1), [retry, setRetry] = useState(0);
  const [reports, setReports] = useState<ReportPage | null>(null), [error, setError] = useState(''), [readAt, setReadAt] = useState('');
  useEffect(() => {
    const controller = new AbortController(); let current = true;
    setReports(null); setError(''); setReadAt('');
    const query = new URLSearchParams({ sourceLotId: sourceRecordId, page: String(page), pageSize: '20' });
    void api<ReportPage>(`/api/lab-reports?${query}`, { signal: controller.signal }).then(value => {
      if (current) { setReports(value); setReadAt(new Date().toISOString()); }
    }).catch(cause => { if (current) setError(errorMessage(cause)); });
    return () => { current = false; controller.abort(); };
  }, [sourceRecordId, page, retry]);
  return <Panel title="Source samples and laboratory evidence">
    <button type="button" onClick={onClose}>Close source evidence</button>
    <p>Evidence belongs to the original procurement source, not to a directly tested pool or processing output. Opening it does not change stock or HOLD.</p>
    <small>Source record: {sourceRecordId}</small>
    <SampleList sourceLotId={sourceRecordId} onOpen={id => window.location.assign(`/capture/samples/${encodeURIComponent(id)}`)} />
    <h3>Confirmed reports for this source</h3>
    <p>Only result sections confirmed against this source are shown. Samples awaiting identity confirmation are not included as confirmed report evidence.</p>
    {error && <div className="notice error" role="alert">{error} <button type="button" onClick={() => setRetry(value => value + 1)}>Retry reports</button></div>}
    {!reports && !error && <p role="status">Loading source reports…</p>}
    {reports && <>
      <p>Evidence read: {new Date(readAt).toLocaleString()}. This is a separate read after the trace snapshot. Report comparisons can change when published reference data changes.</p>
      {!reports.items.length && <p>No confirmed report evidence found on this page. This does not establish that the source meets a standard.</p>}
      {reports.items.map(report => {
        const sections = report.resultSets.filter(set => set.confirmedSampleId && set.sourceLotId === sourceRecordId);
        return <section key={report.id} aria-label={`Source report ${report.reportNumber}`}>
          <h4>{report.reportNumber} · {report.reportKind}</h4>
          <p>{report.laboratoryName} · Issued {report.issuedOn} · Confirmed {report.confirmedAt ? new Date(report.confirmedAt).toLocaleString() : 'Not confirmed'}</p>
          <a href={`/api/lab-reports/${encodeURIComponent(report.id)}/file`}>Download original report</a>
          {sections.map(set => <div key={set.id}>
            <p>Sample: {set.statedSampleCode || set.confirmedSampleId} · Seal: {set.statedSealCode || 'Not recorded'}</p>
            <a href={`/capture/samples/${encodeURIComponent(set.confirmedSampleId!)}`}>View sample and handover history</a>
            <div className="table-scroll"><table aria-label={`Results for source ${sourceRecordId} in ${report.reportNumber}`}>
              <thead><tr><th>Test</th><th>Reported result</th><th>Unit</th><th>LOD</th><th>LOQ</th><th>Method</th><th>Trace</th></tr></thead>
              <tbody>{set.results.map((row, index) => <tr key={index}><td>{row.testLabel}</td><td>{row.originalResultText}</td><td>{row.unit || '—'}</td><td>{row.lod || '—'}</td><td>{row.loq || '—'}</td><td>{row.method || '—'}</td><td>{row.id && onTraceResult && <button type="button" onClick={() => onTraceResult({ reportId: report.id, resultId: row.id! })}>Trace forward from result</button>}</td></tr>)}</tbody>
            </table></div>
          </div>)}
          <details><summary>Whole-report VN comparison</summary>
            <p>{comparisonLabels[report.assessmentStatus] || 'Comparison unavailable'}. This summary covers the whole report, which may contain other samples. It is not a decision for this source, its descendants or an export shipment.</p>
          </details>
          {report.relatedReportId && <p>Linked parent report: {report.relatedReportId} · {report.relationshipReason || report.reportKind}</p>}
          {!!report.relatedReports?.length && <details><summary>Related report history</summary><ul>{report.relatedReports.map(related => <li key={related.id}>{related.reportKind} · {related.id} · {related.relationshipReason || 'Linked evidence'}</li>)}</ul></details>}
        </section>;
      })}
      <nav aria-label="Source report pages"><button type="button" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Previous reports</button><span> Page {reports.page} </span><button type="button" disabled={!reports.hasMore} onClick={() => setPage(value => value + 1)}>Next reports</button></nav>
    </>}
  </Panel>;
}
