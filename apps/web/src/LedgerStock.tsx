import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, type User } from './api';
import { Choice, Enum, Evidence, Feedback, ListControls, Mass, Pagination, Panel, Table, emptyLookup, id, iso, mutate, name, now, positive, stages, useAction, type Lookup, type Lot, type Page, type Row } from './ledger-ui';
import { LedgerLotPicker } from './LedgerLotPicker';

const emptyPage = <T,>(): Page<T> => ({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
const stockSort = [{ value: 'code', label: 'Lot' }, { value: 'stage', label: 'Stage' }, { value: 'owner', label: 'Owner' }, { value: 'quantity', label: 'Quantity' }];
const eventSort = [{ value: 'occurredAt', label: 'Date' }, { value: 'kind', label: 'Event type' }, { value: 'quantity', label: 'Quantity' }];
const holdSort = [{ value: 'createdAt', label: 'Created' }, { value: 'status', label: 'Status' }, { value: 'owner', label: 'Owner' }];
const notificationSort = [{ value: 'createdAt', label: 'Created' }, { value: 'readAt', label: 'Read date' }];

export function LedgerStock({ user, active = true }: { user: User; active?: boolean }) {
  const [look, setLook] = useState<Lookup>(emptyLookup), [stock, setStock] = useState<Page<Lot>>(emptyPage()), [facility, setFacility] = useState(''), [owner, setOwner] = useState(''), [location, setLocation] = useState(''), [stage, setStage] = useState(''), [search, setSearch] = useState(''), [page, setPage] = useState(1), [sortBy, setSortBy] = useState('code'), [direction, setDirection] = useState<'asc' | 'desc'>('asc'), [selected, setSelected] = useState<Lot | null>(null), [task, setTask] = useState<'stock' | 'holds' | 'history'>('stock'), [stockRevision, setStockRevision] = useState(0);
  const action = useAction(), requestId = useRef(0), detailRequestId = useRef(0);
  useEffect(() => { if (active) void action.run(async () => setLook(await api<Lookup>('/api/ledger/lookups'))); }, [active]);
  useEffect(() => {
    if (!active) return;
    const request = ++requestId.current;
    void action.run(async () => {
      const query = new URLSearchParams({ page: String(page), pageSize: '25', sortBy, sortDirection: direction });
      if (facility) query.set('facilityId', facility);
      if (owner) query.set('ownerId', owner);
      if (location) query.set('locationId', location);
      if (stage) query.set('stage', stage);
      if (search) query.set('search', search);
      const result = await api<Page<Lot>>(`/api/ledger/lots?${query}`);
      if (request === requestId.current) setStock(result);
    });
    return () => { requestId.current++; };
  }, [active, page, facility, owner, location, stage, search, sortBy, direction, stockRevision]);
  useEffect(() => {
    if (!active || !selected) return;
    const request = ++detailRequestId.current;
    void action.run(async () => {
      const detail = await api<Lot>(`/api/ledger/lots/${selected.id}`);
      if (request === detailRequestId.current) setSelected(detail);
    });
    return () => { detailRequestId.current++; };
  }, [active, selected?.id, stockRevision]);
  async function refresh() { await api<Lookup>('/api/ledger/lookups').then(setLook); setStockRevision(value => value + 1); }
  const reset = (change: () => void) => { change(); setPage(1); };
  return <>
    <Feedback {...action} />
    <nav className="ledger-task-nav" aria-label="Stock tasks">{([['stock', 'Stock'], ['holds', 'HOLDs & alerts'], ['history', 'History']] as const).map(([key, label]) => <button key={key} aria-pressed={task === key} onClick={() => setTask(key)}>{label}</button>)}</nav>
    <section hidden={task !== 'stock'}>
      <div className="ledger-task-intro"><h2>Stock</h2><p>Search scoped inventory and inspect lot positions, lineage and restrictions.</p></div>
      <Panel title="Scoped stock search">
        <ListControls label="stock" search={search} onSearch={value => reset(() => setSearch(value))} sortBy={sortBy} onSortBy={value => reset(() => setSortBy(value))} sortDirection={direction} onSortDirection={value => reset(() => setDirection(value))} sortOptions={stockSort}>
          <label>Facility filter<Choice rows={look.facilities} value={facility} onChange={value => reset(() => { setFacility(value); setLocation(''); })} optional /></label>
          <label>Company filter<Choice rows={look.owners} value={owner} onChange={value => reset(() => setOwner(value))} optional /></label>
          <label>Location filter<Choice rows={look.locations.filter(row => !facility || row.facilityId === facility)} value={location} onChange={value => reset(() => setLocation(value))} optional /></label>
          <label>Stage filter<Enum values={stages} value={stage} onChange={value => reset(() => setStage(value))} optional /></label>
        </ListControls>
        <p>Remaining physical quantity differs from available quantity when HOLD, correction or receiving restrictions apply.</p>
        <div className="table-scroll"><table><thead><tr><th scope="col">Lot</th><th scope="col">Commodity / stage / identity</th><th scope="col">Owner</th><th scope="col">Positions (kg)</th><th scope="col">Restrictions</th><th scope="col">Detail</th></tr></thead><tbody>
          {stock.items.map(lot => <tr key={lot.id}><td>{lot.code}</td><td>{name(look.commodities, lot.commodityId)} / {lot.stage} / {lot.productIdentity || '—'}</td><td>{look.owners.find(row => row.id === lot.ownerId)?.name || 'Owner name unavailable in current scope'}</td><td>{lot.positions.map(p => <p key={p.locationId}>{name(look.locations, p.locationId)}: physical {p.quantityKg}; available {p.availableKg}</p>)}</td><td>{lot.restrictions.join(', ') || 'None'}</td><td><button disabled={action.busy} aria-label={`Open lot detail for ${lot.code}`} onClick={() => setSelected(lot)}>Open lot detail</button></td></tr>)}
        </tbody></table></div>
        {!stock.items.length && <p className="empty">No stock in current filters.</p>}
        <Pagination page={stock.page} pageSize={stock.pageSize} totalCount={stock.totalCount} onChange={setPage} label="stock" busy={action.busy} />
      </Panel>
    </section>
    <section hidden={task !== 'stock'}>{selected && <Panel title={`Lot detail: ${selected.code}`}>
      <button onClick={() => setSelected(null)}>Close lot detail</button>
      <dl><dt>Owner</dt><dd>{look.owners.find(row => row.id === selected.ownerId)?.name || 'Owner name unavailable in current scope'}</dd><dt>Commodity</dt><dd>{name(look.commodities, selected.commodityId)}</dd><dt>Stage</dt><dd>{selected.stage}</dd><dt>Product identity</dt><dd>{selected.productIdentity || '—'}</dd><dt>Restrictions</dt><dd>{selected.restrictions.join(', ') || 'None'}</dd></dl>
      <h3>Stock positions</h3><Table rows={selected.positions.map(p => ({ Location: name(look.locations, p.locationId), Facility: name(look.facilities, p.facilityId), 'Physical quantity (kg)': p.quantityKg, 'Available quantity (kg)': p.availableKg }))} label="stock positions" />
      <h3>Source contributions</h3>{selected.contributions ? <details><summary>Source contributions</summary><Table rows={selected.contributions} label="source contributions" /></details> : <p>Source contributions unavailable in current scope.</p>}
      <h3>Lot history</h3>{selected.history ? <details><summary>Lot history</summary><LotHistory key={selected.id} lotId={selected.id} initial={selected.history} /></details> : <p>Lot history unavailable in current scope.</p>}
    </Panel>}</section>
    <section hidden={task !== 'history'}><div className="ledger-task-intro"><h2>History</h2><p>Chronological events retain original postings and reversal references.</p></div><History active={active && task === 'history'} look={look} /></section>
    <section hidden={task !== 'holds'}><div className="ledger-task-intro"><h2>HOLDs & alerts</h2><p>HOLD stays active after acknowledgement, isolation, response or notification read. No release workflow.</p></div><Holds active={active && task === 'holds'} look={look} user={user} onChanged={refresh} /></section>
  </>;
}
function LotHistory({ lotId, initial }: { lotId: string; initial: Page<Row> }) {
  const [page, setPage] = useState(initial.page), [search, setSearch] = useState(''), [sortBy, setSortBy] = useState('occurredAt'), [direction, setDirection] = useState<'asc' | 'desc'>('asc'), [result, setResult] = useState(initial), action = useAction(), requestId = useRef(0);
  useEffect(() => {
    if (page === initial.page && !search && sortBy === 'occurredAt' && direction === 'asc') { setResult(initial); return; }
    const request = ++requestId.current;
    void action.run(async () => {
      const query = new URLSearchParams({ lotId, page: String(page), pageSize: '25', sortBy, sortDirection: direction });
      if (search) query.set('search', search);
      const next = await api<Page<Row>>(`/api/ledger/history?${query}`);
      if (request === requestId.current) setResult(next);
    });
    return () => { requestId.current++; };
  }, [lotId, page, search, sortBy, direction, initial]);
  const reset = (change: () => void) => { change(); setPage(1); };
  return <><Feedback {...action} /><ListControls label="lot history" search={search} onSearch={value => reset(() => setSearch(value))} sortBy={sortBy} onSortBy={value => reset(() => setSortBy(value))} sortDirection={direction} onSortDirection={value => reset(() => setDirection(value))} sortOptions={eventSort} /><Table rows={result.items} label="lot history" controls={false} columns={['occurredAt', 'kind', 'ownerName', 'quantityKg', 'details']} /><details><summary>Technical history records</summary><Table rows={result.items} label="lot history records" controls={false} /></details><Pagination page={result.page} pageSize={result.pageSize} totalCount={result.totalCount} onChange={setPage} label="lot history" busy={action.busy} /></>;
}
function History({ look, active }: { look: Lookup; active: boolean }) {
  const [lotId, setLotId] = useState(''), [facility, setFacility] = useState(''), [search, setSearch] = useState(''), [sortBy, setSortBy] = useState('occurredAt'), [direction, setDirection] = useState<'asc' | 'desc'>('asc'), [page, setPage] = useState(1), [result, setResult] = useState<Page<Row>>(emptyPage()), action = useAction(), requestId = useRef(0);
  useEffect(() => { if (!active) return; const request = ++requestId.current; void action.run(async () => { const query = new URLSearchParams({ page: String(page), pageSize: '25', sortBy, sortDirection: direction }); if (lotId) query.set('lotId', lotId); if (facility) query.set('facilityId', facility); if (search) query.set('search', search); const next = await api<Page<Row>>(`/api/ledger/history?${query}`); if (request === requestId.current) setResult(next); }); return () => { requestId.current++; }; }, [active, page, lotId, facility, search, sortBy, direction]);
  const reset = (change: () => void) => { change(); setPage(1); };
  return <Panel title="Chronological events and signed postings"><Feedback {...action} /><p>History retains original postings and reversal references. Declared loss and signed variance appear separately in event details.</p><ListControls label="history" search={search} onSearch={value => reset(() => setSearch(value))} sortBy={sortBy} onSortBy={value => reset(() => setSortBy(value))} sortDirection={direction} onSortDirection={value => reset(() => setDirection(value))} sortOptions={eventSort}><LedgerLotPicker label="History lot" value={lotId} onChange={value => reset(() => setLotId(value))} look={look} /><label>History facility<Choice rows={look.facilities} value={facility} onChange={value => reset(() => setFacility(value))} optional /></label></ListControls><Table rows={result.items} label="history" controls={false} columns={['occurredAt', 'kind', 'ownerName', 'quantityKg', 'details']} /><details><summary>Technical history records</summary><Table rows={result.items} label="history records" controls={false} /></details><Pagination page={result.page} pageSize={result.pageSize} totalCount={result.totalCount} onChange={setPage} label="history" busy={action.busy} /></Panel>;
}
export function TransferForm({ hold, onChanged, look, pinnedLots = [] }: { look: Lookup; hold?: Row; onChanged: () => Promise<void>; pinnedLots?: Lot[] }) {
  const [lotId, setLotId] = useState(hold?.lotId || ''), [lot, setLot] = useState<Lot>(), [from, setFrom] = useState(''), [to, setTo] = useState(''), [quantity, setQuantity] = useState(''), [when, setWhen] = useState(now()), [commandId, setCommandId] = useState(id()), action = useAction();
  async function transfer(event: FormEvent) { event.preventDefault(); await action.run(async () => { await mutate('/api/ledger/transfers', { id: commandId, lotId, fromLocationId: from, toLocationId: to, quantityKg: quantity, occurredAt: iso(when), ...(hold ? { holdId: hold.id } : {}) }); setCommandId(id()); await onChanged(); }, hold ? 'Isolation transfer posted; HOLD remains active.' : 'Transfer posted; lot identity and lineage unchanged.'); }
  return <Panel title={hold ? 'Authorized HOLD isolation transfer' : 'Unchanged-lot transfer'}><Feedback {...action} /><form onSubmit={transfer}>
    <LedgerLotPicker key={commandId} label="Transfer lot" value={lotId} onChange={value => { setLotId(value); setFrom(''); }} onSelected={setLot} look={look} ownerId={hold?.ownerId} lockedLotId={hold?.lotId} pinnedLots={pinnedLots} />
    <label>Source stock position<Choice rows={(lot?.positions || []).filter(p => positive(p.quantityKg)).map(p => ({ id: p.locationId, name: `${name(look.locations, p.locationId)} — physical ${p.quantityKg} kg, available ${p.availableKg} kg` }))} value={from} onChange={setFrom} /></label>
    <label>{hold ? 'Isolation destination' : 'Destination location'}<Choice rows={look.locations.filter(row => row.id !== from && (!hold || row.kind === 'ISOLATION'))} value={to} onChange={setTo} /></label>
    <Mass label="Transfer quantity (kg)" value={quantity} onChange={setQuantity} />
    <label>Transfer occurred at<input required type="datetime-local" value={when} onChange={e => setWhen(e.target.value)} /></label>
    <button disabled={action.busy}>{hold ? 'Transfer to HOLD isolation' : 'Commit unchanged-lot transfer'}</button>
  </form><p>{hold ? 'Manager authority required at source and destination. Independent correction/receiving blocks still prevent movement. Transfer does not release HOLD.' : 'No new lot or source attribution. Mixing requires processing, not transfer.'}</p></Panel>;
}
function Holds({ look, user, onChanged, active }: { look: Lookup; user: User; onChanged: () => Promise<void>; active: boolean }) {
  const [holds, setHolds] = useState<Page<Row>>(emptyPage()), [notifications, setNotifications] = useState<Page<Row>>(emptyPage()), [lotId, setLotId] = useState(''), [reason, setReason] = useState(''), [holdPage, setHoldPage] = useState(1), [notificationPage, setNotificationPage] = useState(1), [holdSearch, setHoldSearch] = useState(''), [notificationSearch, setNotificationSearch] = useState(''), [holdOwner, setHoldOwner] = useState(''), [notificationOwner, setNotificationOwner] = useState(''), [holdStatus, setHoldStatus] = useState(''), [readStatus, setReadStatus] = useState(''), [holdSortBy, setHoldSortBy] = useState('createdAt'), [notificationSortBy, setNotificationSortBy] = useState('createdAt'), [holdDirection, setHoldDirection] = useState<'asc' | 'desc'>('desc'), [notificationDirection, setNotificationDirection] = useState<'asc' | 'desc'>('desc'), action = useAction(), requestId = useRef(0);
  async function load() {
    const holdQuery = new URLSearchParams({ page: String(holdPage), pageSize: '25', sortBy: holdSortBy, sortDirection: holdDirection }); if (holdSearch) holdQuery.set('search', holdSearch); if (holdOwner) holdQuery.set('ownerId', holdOwner); if (holdStatus) holdQuery.set('status', holdStatus);
    const notificationQuery = new URLSearchParams({ page: String(notificationPage), pageSize: '25', sortBy: notificationSortBy, sortDirection: notificationDirection }); if (notificationSearch) notificationQuery.set('search', notificationSearch); if (notificationOwner) notificationQuery.set('ownerId', notificationOwner); if (readStatus) notificationQuery.set('status', readStatus);
    const request = ++requestId.current;
    const [h, n] = await Promise.all([api<Page<Row>>(`/api/ledger/holds?${holdQuery}`), api<Page<Row>>(`/api/ledger/notifications?${notificationQuery}`)]);
    if (request === requestId.current) { setHolds(h); setNotifications(n); if (!h.items.length && h.page > 1) setHoldPage(Math.max(1, Math.ceil(h.totalCount / h.pageSize))); if (!n.items.length && n.page > 1) setNotificationPage(Math.max(1, Math.ceil(n.totalCount / n.pageSize))); }
  }
  useEffect(() => { if (active) void action.run(load); return () => { requestId.current++; }; }, [active, holdPage, notificationPage, holdSearch, notificationSearch, holdOwner, notificationOwner, holdStatus, readStatus, holdSortBy, notificationSortBy, holdDirection, notificationDirection]);
  async function command(path: string, body: Row) { await mutate('/api/ledger/' + path, body); await load(); await onChanged(); }
  const reset = (change: () => void, setter: (page: number) => void) => { change(); setter(1); };
  return <Panel title="Customer HOLD and notification inbox"><Feedback {...action} /><p>HOLD stays active after acknowledgement, isolation, response or notification read. No release workflow.</p><button disabled={action.busy} onClick={() => void action.run(load)}>Refresh HOLD inbox</button>{user.permissions.includes('holds.manage') && <form onSubmit={e => { e.preventDefault(); void action.run(() => command('holds', { id: id(), lotId, reason }), 'HOLD and recipient notifications saved atomically.'); }}><LedgerLotPicker label="Own-company lot to HOLD" value={lotId} onChange={setLotId} look={look} ownerId={user.organizationId || undefined} /><label>HOLD reason<textarea required value={reason} onChange={e => setReason(e.target.value)} /></label><button disabled={action.busy}>Place customer HOLD</button></form>}
    <h3>Customer HOLDs</h3><ListControls label="HOLDs" search={holdSearch} onSearch={value => reset(() => setHoldSearch(value), setHoldPage)} sortBy={holdSortBy} onSortBy={value => reset(() => setHoldSortBy(value), setHoldPage)} sortDirection={holdDirection} onSortDirection={value => reset(() => setHoldDirection(value), setHoldPage)} sortOptions={holdSort}><label>HOLD owner<Choice rows={look.owners} value={holdOwner} onChange={value => reset(() => setHoldOwner(value), setHoldPage)} optional /></label><label>HOLD status<Enum values={['ACTIVE', 'ACKNOWLEDGED', 'ISOLATED', 'RESPONDED']} value={holdStatus} onChange={value => reset(() => setHoldStatus(value), setHoldPage)} optional /></label></ListControls>
    {holds.items.map(hold => <HoldItem key={hold.id} hold={hold} look={look} user={user} command={command} onChanged={onChanged} />)}{!holds.items.length && <p className="empty">No HOLDs.</p>}<Pagination page={holds.page} pageSize={holds.pageSize} totalCount={holds.totalCount} onChange={setHoldPage} label="HOLDs" busy={action.busy} />
    <h3>In-app notifications</h3><ListControls label="notifications" search={notificationSearch} onSearch={value => reset(() => setNotificationSearch(value), setNotificationPage)} sortBy={notificationSortBy} onSortBy={value => reset(() => setNotificationSortBy(value), setNotificationPage)} sortDirection={notificationDirection} onSortDirection={value => reset(() => setNotificationDirection(value), setNotificationPage)} sortOptions={notificationSort}><label>Notification owner<Choice rows={look.owners} value={notificationOwner} onChange={value => reset(() => setNotificationOwner(value), setNotificationPage)} optional /></label><label>Read state<Enum values={['READ', 'UNREAD']} value={readStatus} onChange={value => reset(() => setReadStatus(value), setNotificationPage)} optional /></label></ListControls>
    {notifications.items.map(notification => <div className="ledger-notification" key={notification.id}><p>{notification.message} — {notification.createdAt}</p>{notification.lotCode && <p>Lot: {notification.lotCode}</p>}{notification.ownerName && <p>Owner: {notification.ownerName}</p>}{notification.readAt ? <p>Read: {notification.readAt}</p> : <button disabled={action.busy} onClick={() => void action.run(() => command(`notifications/${notification.id}/read`, {}), 'Notification marked read; HOLD unchanged.')}>Mark notification read</button>}<details><summary>Technical notification identifiers</summary><dl><dt>Notification ID</dt><dd>{notification.id}</dd><dt>Lot ID</dt><dd>{notification.lotId}</dd></dl></details></div>)}{!notifications.items.length && <p>No notifications.</p>}<Pagination page={notifications.page} pageSize={notifications.pageSize} totalCount={notifications.totalCount} onChange={setNotificationPage} label="notifications" busy={action.busy} />
  </Panel>;
}
function HoldItem({ hold, look, user, command, onChanged }: { hold: Row; look: Lookup; user: User; command: (path: string, body: Row) => Promise<void>; onChanged: () => Promise<void> }) {
  const [lot, setLot] = useState<Lot>(), action = useAction();
  async function loadLot() { setLot(await api<Lot>(`/api/ledger/lots/${hold.lotId}`)); }
  async function refreshAfterTransfer() { await onChanged(); await action.run(loadLot); }
  return <details onToggle={event => { if (event.currentTarget.open && !lot) void action.run(loadLot); }}><summary>{hold.lotCode || lot?.code || 'Stock lot'} — {lot?.positions.map(position => `${position.quantityKg} kg`).join(', ') || 'Quantity unavailable'} — {hold.status}</summary><Feedback {...action} /><p>{hold.reason}</p><p>Placed: {hold.createdAt}</p>{hold.ownerName && <p>Owner: {hold.ownerName}</p>}{hold.response && <p>Manager response: {hold.response}</p>}{hold.evidenceId && <a href={`/api/ledger/evidence/${hold.evidenceId}`} target="_blank" rel="noreferrer">Response evidence</a>}<details><summary>Technical HOLD identifiers</summary><dl><dt>HOLD ID</dt><dd>{hold.id}</dd><dt>Lot ID</dt><dd>{hold.lotId}</dd></dl></details>{user.permissions.includes('holds.process') && <HoldResponse hold={hold} lot={lot} look={look} command={command} onChanged={onChanged} onTransferred={refreshAfterTransfer} />}</details>;
}
function HoldResponse({ hold, lot, look, command, onChanged, onTransferred }: { hold: Row; lot?: Lot; look: Lookup; command: (path: string, body: Row) => Promise<void>; onChanged: () => Promise<void>; onTransferred: () => Promise<void> }) {
  const [status, setStatus] = useState('ACKNOWLEDGED'), [response, setResponse] = useState(''), [evidence, setEvidence] = useState(''), [facility, setFacility] = useState(''), action = useAction();
  const physicalPositions = lot?.positions.filter(position => positive(position.quantityKg)) || [];
  const facilities = look.facilities.filter(row => (physicalPositions.length ? physicalPositions : lot?.positions || []).some(position => position.facilityId === row.id));
  const facilityValid = facilities.some(row => row.id === facility);
  useEffect(() => { if (facility && !facilityValid) { setFacility(''); setEvidence(''); } }, [facility, facilityValid]);
  return <><TransferForm hold={hold} look={look} onChanged={onTransferred} /><form onSubmit={e => { e.preventDefault(); void action.run(() => command(`holds/${hold.id}/respond`, { status, response, evidenceId: evidence }), 'Manager response recorded; HOLD remains active.'); }}><Feedback {...action} /><label>Response facility<Choice rows={facilities} value={facility} onChange={value => { setFacility(value); setEvidence(''); }} /></label><Evidence key={facility} ownerId={lot?.ownerId || ''} facilityId={facility} value={evidence} onChange={setEvidence} /><label>Manager HOLD response status<Enum values={['ACKNOWLEDGED', 'ISOLATED', 'RESPONDED']} value={status} onChange={setStatus} /></label><label>Manager HOLD response<textarea required value={response} onChange={e => setResponse(e.target.value)} /></label><p>ISOLATED requires actual stock already transferred into isolation locations.</p><button disabled={action.busy || !evidence}>Record manager HOLD response</button></form></>;
}
