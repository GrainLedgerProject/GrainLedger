import { useEffect, useState } from 'react';
import { api, errorMessage } from './api';
import { Feedback, Pagination, Panel, type Page } from './ledger-ui';

const categories = [
  ['PROCUREMENT', 'Procurement and arrival'], ['LAB', 'Laboratory reports'], ['PLAN', 'Export orders and plans'],
  ['RESERVATION', 'Reservations'], ['CONTAINER', 'Container execution'], ['HOLD', 'HOLD'], ['TRACE', 'Trace'],
  ['LEDGER', 'Processing and corrections'], ['ADMINISTRATION', 'Administration'],
] as const;
type Event = { id: string; category: string; eventType: string; recordedAt: string; reference: string | null; subjectId: string | null;
  actorId: string | null; actorName: string | null; context: Record<string, string | number>; evidenceId: string | null };
const contextLabels: Record<string, string> = { version: 'Version', reportKind: 'Report kind', relatedReportId: 'Related report',
  scope: 'Scope', holdId: 'HOLD reference', quantityKg: 'Quantity (kg)', proposalId: 'Proposal', documentType: 'Document type',
  direction: 'Direction', correctionId: 'Correction reference', reversesPostingId: 'Reversed posting' };
const empty = { category: '', eventType: '', search: '', from: '', to: '' };

export function BusinessHistory({ active }: { active: boolean }) {
  const [inputs, setInputs] = useState(empty), [filters, setFilters] = useState(empty);
  const [page, setPage] = useState(1), [result, setResult] = useState<Page<Event>>();
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  useEffect(() => {
    if (!active) return;
    const abort = new AbortController();
    const query = new URLSearchParams({ page: String(page), pageSize: '25' });
    Object.entries(filters).forEach(([key, value]) => { if (value.trim()) query.set(key, value.trim()); });
    setBusy(true); setError(''); setResult(undefined);
    void api<Page<Event>>(`/api/ledger/business-history?${query}`, { signal: abort.signal })
      .then(next => { if (!abort.signal.aborted) setResult(next); })
      .catch(cause => { if (!abort.signal.aborted) setError(errorMessage(cause)); })
      .finally(() => { if (!abort.signal.aborted) setBusy(false); });
    return () => abort.abort();
  }, [active, filters, page]);

  return <Panel title="Business history">
    <Feedback error={error} />
    <form onSubmit={event => { event.preventDefault(); setFilters({ ...inputs }); setPage(1); }}>
      <div className="ledger-form-grid">
        <label>Category<select value={inputs.category} onChange={event => setInputs({ ...inputs, category: event.target.value })}>
          <option value="">All permitted events</option>{categories.map(([key, label]) => <option key={key} value={key}>{label}</option>)}
        </select></label>
        <label>Event type<input value={inputs.eventType} maxLength={100} placeholder="e.g. CLOSE" onChange={event => setInputs({ ...inputs, eventType: event.target.value })} /></label>
        <label>Search reference<input value={inputs.search} maxLength={100} onChange={event => setInputs({ ...inputs, search: event.target.value })} /></label>
        <label>From date (UTC)<input type="date" value={inputs.from} max={inputs.to || undefined} onChange={event => setInputs({ ...inputs, from: event.target.value })} /></label>
        <label>Through date (UTC)<input type="date" value={inputs.to} min={inputs.from || undefined} onChange={event => setInputs({ ...inputs, to: event.target.value })} /></label>
      </div>
      <button disabled={busy}>Search history</button>
    </form>
    {busy && <p role="status">Loading history…</p>}
    {result && (result.items.length ? <>
      <div className="table-scroll"><table aria-label="Business history events">
        <thead><tr><th>Recorded</th><th>Category</th><th>Event</th><th>Actor</th><th>Reference</th></tr></thead>
        <tbody>{result.items.map(item => <tr key={`${item.category}/${item.id}`}>
          <td>{new Date(item.recordedAt).toLocaleString()}</td><td>{categories.find(([key]) => key === item.category)?.[1]}</td>
          <td>{item.eventType}</td><td>{item.actorName || item.actorId || '—'}</td>
          <td><details><summary>{item.reference || item.subjectId || item.id}</summary>
            <dl><dt>Event ID</dt><dd>{item.id}</dd>{item.subjectId && <><dt>Record ID</dt><dd>{item.subjectId}</dd></>}
              {Object.entries(item.context).map(([key, value]) => <div key={key}><dt>{contextLabels[key] || key}</dt><dd>{value}</dd></div>)}
            </dl>
            {item.evidenceId && <a href={`/api/ledger/evidence/${item.evidenceId}`}>View evidence</a>}
          </details></td>
        </tr>)}</tbody>
      </table></div>
      <Pagination label="business history" page={page} pageSize={result.pageSize} totalCount={result.totalCount} onChange={setPage} />
    </> : <p>No events match these filters.</p>)}
  </Panel>;
}
