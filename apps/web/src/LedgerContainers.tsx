import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, errorMessage, type User } from './api';
import { Feedback, Pagination, Panel, emptyLookup, type Lookup, type Lot, type Page } from './ledger-ui';
import { LedgerLotPicker } from './LedgerLotPicker';

type Line = { lotId: string; locationId: string; quantityKg: string; lotCode?: string };
type Draft = { id: string; loadedAt: string; lines: Line[]; totalKg: string };
type Container = { id: string; code: string; status: string; facilityId: string; orderId: string | null; targetKg: string; containerNumber: string | null; sealNumber: string | null; version: string; committedLoadedKg: string; remainingKg: string; drafts: Draft[] };
type Summary = Pick<Container, 'id' | 'code' | 'status' | 'targetKg' | 'facilityId'>;
type Check = { ready: boolean; version: string; findings: { code: string; message: string; lotId?: string }[]; committedLoadedKg: string; draftKg: string; targetKg: string };
type Event = { id: string; kind: string; actorId: string; recorded_at: string };
const root = '/api/ledger/containers';
const blankLine = (): Line => ({ lotId: '', locationId: '', quantityKg: '' });
const emptyPage = <T,>(): Page<T> => ({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
const massPattern = '[0-9]{1,14}(\\.[0-9]{1,6})?';

export function LedgerContainers({ user, active = true }: { user: User; active?: boolean }) {
  const writable = user.role === 'ExportStaff' && user.permissions.includes('containers.write');
  const [look, setLook] = useState<Lookup>(emptyLookup), [list, setList] = useState<Page<Summary>>(emptyPage), [page, setPage] = useState(1), [search, setSearch] = useState('');
  const [selected, setSelected] = useState<Container | null>(null), [history, setHistory] = useState<Page<Event>>(emptyPage), [historyPage, setHistoryPage] = useState(1);
  const [code, setCode] = useState(''), [facility, setFacility] = useState(''), [target, setTarget] = useState(''), [newId, setNewId] = useState(() => crypto.randomUUID());
  const [order, setOrder] = useState(''), [number, setNumber] = useState(''), [seal, setSeal] = useState('');
  const [draftId, setDraftId] = useState<string>(() => crypto.randomUUID()), [loadedAt, setLoadedAt] = useState(() => new Date().toISOString().slice(0, 16)), [lines, setLines] = useState<Line[]>([blankLine()]);
  const [chosenLots, setChosenLots] = useState<Record<string, Lot>>({}), [check, setCheck] = useState<Check | null>(null), [error, setError] = useState(''), [busy, setBusy] = useState(false);
  const sequence = useRef(0), listSequence = useRef(0), historySequence = useRef(0), pending = useRef<{ key: string; id: string } | null>(null), live = useRef(true);
  useEffect(() => { live.current = active; if (!active) { setBusy(false); setCheck(null); } return () => { live.current = false; sequence.current++; listSequence.current++; historySequence.current++; }; }, [active, user.id, user.role, user.organizationId]);
  async function refreshList() {
    const request = ++listSequence.current;
    try { const next = await api<Page<Summary>>(`${root}?${new URLSearchParams({ page: String(page), pageSize: '25', ...(search.trim() ? { search: search.trim() } : {}) })}`); if (live.current && request === listSequence.current) setList(next); }
    catch (e) { if (live.current && request === listSequence.current) setError(errorMessage(e)); }
  }
  useEffect(() => { if (active) void refreshList(); }, [active, page, search, user.id, user.role, user.organizationId]);
  useEffect(() => {
    if (!active) return;
    let valid = true;
    void api<Lookup>('/api/ledger/lookups').then(value => { if (valid) setLook(value); }).catch(e => { if (valid) setError(errorMessage(e)); });
    return () => { valid = false; };
  }, [active, user.id, user.role, user.organizationId]);
  useEffect(() => {
    if (!active || !selected) return;
    const request = ++historySequence.current;
    void api<Page<Event>>(`${root}/${selected.id}/history?page=${historyPage}&pageSize=25`).then(value => { if (live.current && request === historySequence.current) setHistory(value); }).catch(e => { if (live.current && request === historySequence.current) setError(errorMessage(e)); });
    return () => { historySequence.current++; };
  }, [active, selected?.id, selected?.version, historyPage]);
  useEffect(() => { setSelected(null); setCheck(null); setList(emptyPage()); setLook(emptyLookup); setHistory(emptyPage()); setError(''); }, [user.id, user.role, user.organizationId]);
  function show(c: Container) {
    setSelected(c); setOrder(c.orderId || ''); setNumber(c.containerNumber || ''); setSeal(c.sealNumber || ''); setCheck(null); setHistoryPage(1);
  }
  function resetDraft() { setDraftId(crypto.randomUUID()); setLines([blankLine()]); setChosenLots({}); setCheck(null); }
  async function run<T,>(work: () => Promise<T>, apply: (value: T) => void) {
    const request = ++sequence.current; setBusy(true); setError('');
    try { const value = await work(); if (live.current && request === sequence.current) apply(value); }
    catch (e) { if (live.current && request === sequence.current) setError(errorMessage(e)); }
    finally { if (live.current && request === sequence.current) setBusy(false); }
  }
  async function mutate(path: string, payload: object) {
    const key = path + JSON.stringify(payload);
    if (pending.current?.key !== key) pending.current = { key, id: crypto.randomUUID() };
    return api<Container>(path, { method: 'POST', body: JSON.stringify({ ...payload, requestId: pending.current.id }) });
  }
  function create(e: FormEvent) {
    e.preventDefault();
    void run(() => mutate(root, { id: newId, code, facilityId: facility, targetKg: target }), c => { show(c); resetDraft(); setNewId(crypto.randomUUID()); setCode(''); setTarget(''); void refreshList(); });
  }
  function patchLine(index: number, changes: Partial<Line>) { setLines(previous => previous.map((line, i) => i === index ? { ...line, ...changes } : line)); setCheck(null); }
  function editDraft(draft: Draft) { setDraftId(draft.id); setLines(draft.lines.map(({ lotId, locationId, quantityKg }) => ({ lotId, locationId, quantityKg }))); setLoadedAt(draft.loadedAt.slice(0, 16)); setCheck(null); }
  return <>
    <div className="ledger-task-intro"><h2>Containers</h2><p>Prepare loading after blending, then check the requirements for closure.</p></div>
    <Feedback error={error} />
    <p className="notice">Preparation is available. Approved order/blend outputs, stock reservations, export assessment and container restrictions are awaiting integration. Saving a draft does not hold or withdraw stock. Loading and closure are unavailable until these checks are connected.</p>
    {writable && <Panel title="Create container"><form onSubmit={create}>
      <div className="ledger-form-grid"><label>Internal container code<input required maxLength={80} value={code} onChange={e => setCode(e.target.value)} /></label>
      <label>Loading facility<select required value={facility} onChange={e => setFacility(e.target.value)}><option value="">Select…</option>{look.facilities.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}</select></label>
      <label>Target net kg<input required inputMode="decimal" pattern={massPattern} value={target} onChange={e => setTarget(e.target.value)} /></label></div>
      <button disabled={busy || !active}>Create preparation</button>
    </form></Panel>}
    <Panel title="Container list"><label>Search containers<input type="search" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} /></label>
      <div className="table-scroll"><table aria-label="Containers"><thead><tr><th>Container</th><th>State</th><th>Target net kg</th></tr></thead><tbody>{list.items.map(c => <tr key={c.id}><td><button disabled={busy || !active} onClick={() => void run(() => api<Container>(`${root}/${c.id}`), value => { show(value); resetDraft(); })}>{c.code}</button></td><td>{c.status}</td><td>{c.targetKg}</td></tr>)}</tbody></table></div>
      {!list.items.length && <p>No containers in this scope.</p>}
      <Pagination label="containers" page={list.page} pageSize={25} totalCount={list.totalCount} onChange={setPage} busy={busy} />
    </Panel>
    {selected && <>
      <Panel title={`Container — ${selected.code}`}>
        <p>{selected.status} · Target: {selected.targetKg} kg · Committed loaded: {selected.committedLoadedKg} kg · Remaining: {selected.remainingKg} kg</p>
        <button type="button" disabled={busy || !active} onClick={() => void run(() => api<Container>(`${root}/${selected.id}`), show)}>Reload container</button>
        {writable ? <form onSubmit={e => { e.preventDefault(); void run(() => mutate(`${root}/${selected.id}/metadata`, { version: selected.version, orderId: order || null, containerNumber: number, sealNumber: seal }), show); }}>
          <div className="ledger-form-grid"><label>Order reference (optional UUID)<input value={order} onChange={e => setOrder(e.target.value)} /></label><label>Container number<input maxLength={80} value={number} onChange={e => setNumber(e.target.value)} /></label><label>Seal number<input maxLength={80} value={seal} onChange={e => setSeal(e.target.value)} /></label></div>
          <p>Order references are retained for preparation; they are not yet verified against an approved order.</p><button disabled={busy || !active}>Save metadata</button>
        </form> : <p>Container: {selected.containerNumber || 'Unassigned'} · Seal: {selected.sealNumber || 'Unassigned'}</p>}
      </Panel>
      {writable && <Panel title="Prepare loading draft"><form onSubmit={e => { e.preventDefault(); void run(async () => mutate(`${root}/${selected.id}/loading-draft`, { version: selected.version, draftId, loadedAt: new Date(loadedAt + 'Z').toISOString(), lines: lines.map(({ lotId, locationId, quantityKg }) => ({ lotId, locationId, quantityKg })) }), c => { show(c); }); }}>
        <p>Drafts are alternative preparations. They do not count toward loaded mass, reserve stock or authorize a product for export.</p>
        <label>Planned loading time (UTC)<input required type="datetime-local" value={loadedAt} onChange={e => setLoadedAt(e.target.value)} /></label>
        {lines.map((line, index) => {
          const lot = chosenLots[line.lotId];
          const positions = lot?.positions.filter(p => p.facilityId === selected.facilityId) || [];
          return <fieldset key={index}><legend>Loading line {index + 1}</legend>
            <LedgerLotPicker label={`Lot for line ${index + 1}`} value={line.lotId} facilityId={selected.facilityId} look={look} onChange={id => patchLine(index, { lotId: id, locationId: '' })} onSelected={value => { if (value) setChosenLots(previous => previous[value.id] === value ? previous : { ...previous, [value.id]: value }); }} disabled={busy || !active} />
            <label>Source position for line {index + 1}<select required value={line.locationId} onChange={e => patchLine(index, { locationId: e.target.value })}><option value="">Select…</option>{positions.map(p => <option key={p.locationId} value={p.locationId}>{look.locations.find(l => l.id === p.locationId)?.name || p.locationId} · Physical {p.quantityKg} kg</option>)}{line.locationId && !positions.some(p => p.locationId === line.locationId) && <option value={line.locationId}>{line.locationId} — saved draft position</option>}</select></label>
            <label>Net kg for line {index + 1}<input required inputMode="decimal" pattern={massPattern} value={line.quantityKg} onChange={e => patchLine(index, { quantityKg: e.target.value })} /></label>
            {lines.length > 1 && <button type="button" onClick={() => { setLines(previous => previous.filter((_, i) => i !== index)); setCheck(null); }}>Remove line {index + 1}</button>}
          </fieldset>;
        })}
        <button type="button" disabled={lines.length >= 100 || busy} onClick={() => { setLines(previous => [...previous, blankLine()]); setCheck(null); }}>Add loading line</button>{' '}
        <button disabled={busy || !active}>Save loading draft</button>{' '}<button type="button" disabled={busy} onClick={resetDraft}>New draft</button>
      </form></Panel>}
      <Panel title="Saved loading drafts">
        {selected.drafts.length === 0 && <p>No loading drafts saved.</p>}
        {selected.drafts.map(d => <div key={d.id}><p><strong>{d.totalKg} kg draft</strong> · {new Date(d.loadedAt).toLocaleString()} · {d.lines.length} lines</p>
          {writable && <button disabled={busy || !active} onClick={() => editDraft(d)}>Edit draft {d.id}</button>}{' '}
          <button disabled={busy || !active} onClick={() => void run(() => api<Check>(`${root}/${selected.id}/loading-check`, { method: 'POST', body: JSON.stringify({ draftId: d.id }) }), setCheck)}>Check loading {d.id}</button>
        </div>)}
        <p>Commit loading is unavailable while the required integrations are missing.</p><button disabled>Commit loading</button>
      </Panel>
      <Panel title="Close container"><button disabled={busy || !active} onClick={() => void run(() => api<Check>(`${root}/${selected.id}/close-check`, { method: 'POST', body: '{}' }), setCheck)}>Check closure</button>{' '}<button disabled>Close container</button>
        <p>Only committed loading counts toward the target. A successful closure must retain its assessment and immutable dossier together.</p>
        {check && <section role="status" aria-label="Container readiness"><p><strong>{check.ready ? 'Ready' : 'Blocked'}</strong> · Committed loaded: {check.committedLoadedKg} / {check.targetKg} kg</p><ul>{check.findings.map((finding, i) => <li key={`${finding.code}-${i}`}>{finding.message}{finding.lotId && <small> · {finding.lotId}</small>}</li>)}</ul></section>}
      </Panel>
      <Panel title="Container history"><ul>{history.items.map(e => <li key={e.id}>{e.kind} · {new Date(e.recorded_at).toLocaleString()} · {e.actorId}</li>)}</ul><Pagination label="container history" page={history.page} pageSize={25} totalCount={history.totalCount} onChange={setHistoryPage} busy={busy} /></Panel>
    </>}
  </>;
}
