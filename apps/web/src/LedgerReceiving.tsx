import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, type User } from './api';
import { Choice, Evidence, Feedback, ListControls, Pagination, Panel, Table, id, iso, mutate, useAction, type Page, type Row } from './ledger-ui';

type Sources = { farmers: Row[]; plots: Row[]; seasons: Row[]; plotSeasons: Row[] };
type ListState = { page: number; pageSize: number; totalCount: number; hasMore: boolean };
type Procurement = Row & { id: string; code: string; items: Row[] };
const envelope = (payload: Row) => ({ deviceId: 'grainledger-web', clientOperationId: id(), capturedAt: new Date().toISOString(), payloadSchemaVersion: 1, payload });
const emptySources: Sources = { farmers: [], plots: [], seasons: [], plotSeasons: [] };
const emptyPage: ListState = { page: 1, pageSize: 25, totalCount: 0, hasMore: false };
function compareDecimal(left: string, right: string): number {
  const integer = (value: string) => { const [whole, fraction = ''] = value.split('.'); return BigInt(`${whole}${fraction.padEnd(6, '0')}`); };
  const a = integer(left), b = integer(right);
  return a < b ? -1 : a > b ? 1 : 0;
}
const receivingStatusLabel = (status: string) => {
  const labels: Record<string, string> = { PENDING_ARRIVAL: 'Pending mill arrival', AWAITING_MANAGER_REVIEW: 'Awaiting manager review', REMEASUREMENT_REQUESTED: 'Remeasurement requested', BLOCKED_UNRESOLVED: 'Blocked — unresolved', RECEIVED_MATCHED: 'Received — exact match', RECEIVED_SHORTAGE_APPROVED: 'Received — shortage approved' };
  return labels[status] || status;
};
export function LedgerReceiving({ user, active = true }: { user: User; active?: boolean }) {
  const action = useAction(), sourceWrite = user.permissions.includes('farms.write'), sourceRead = user.permissions.some(p => ['farms.read', 'farms.write'].includes(p)), purchase = user.permissions.includes('purchases.write'), configure = user.permissions.includes('platform.configure'), canOperateReceiving = user.permissions.includes('lots.receive'), canReviewReceiving = user.permissions.includes('receiving.reconcile'), canReadCapture = sourceRead || purchase;
  const [task, setTask] = useState<'procurement' | 'sources'>('procurement');
  const [sources, setSources] = useState<Sources>(emptySources), [organizations, setOrganizations] = useState<Row[]>([]), [companyId, setCompanyId] = useState(configure ? '' : user.organizationId || ''), [receipts, setReceipts] = useState<Row[]>([]), [receiptPage, setReceiptPage] = useState(emptyPage), [reference, setReference] = useState<Row | null>(null), [referencePage, setReferencePage] = useState(emptyPage), [referenceQuery, setReferenceQuery] = useState(''), [referenceSort, setReferenceSort] = useState('collectionSiteName'), [referenceDirection, setReferenceDirection] = useState<'asc' | 'desc'>('asc'), [selected, setSelected] = useState<Procurement | null>(null), [sourceLot, setSourceLot] = useState<Row | null>(null), [sourcePages, setSourcePages] = useState({ farmers: 1, plots: 1, seasons: 1, plotSeasons: 1 }), [sourceStates, setSourceStates] = useState<Record<keyof Sources, ListState>>({ farmers: emptyPage, plots: emptyPage, seasons: emptyPage, plotSeasons: emptyPage }), [sourceSearch, setSourceSearch] = useState<Record<keyof Sources, string>>({ farmers: '', plots: '', seasons: '', plotSeasons: '' }), [sourceSort, setSourceSort] = useState<Record<keyof Sources, string>>({ farmers: 'name', plots: 'code', seasons: 'name', plotSeasons: 'id' }), [sourceDirection, setSourceDirection] = useState<Record<keyof Sources, 'asc' | 'desc'>>({ farmers: 'asc', plots: 'asc', seasons: 'asc', plotSeasons: 'asc' }), [facilityId, setFacilityId] = useState(''), [tasks, setTasks] = useState<Row[]>([]), [taskPage, setTaskPage] = useState(emptyPage), [taskQuery, setTaskQuery] = useState(''), [taskStatus, setTaskStatus] = useState(''), [taskSort, setTaskSort] = useState('receivedAt'), [taskDirection, setTaskDirection] = useState<'asc' | 'desc'>('desc'), [selectedTask, setSelectedTask] = useState<Row | null>(null);
  const [referenceRevision, setReferenceRevision] = useState(0);
  const [receiptQuery, setReceiptQuery] = useState(''), [receiptSort, setReceiptSort] = useState('receivedAt'), [receiptDirection, setReceiptDirection] = useState<'asc' | 'desc'>('desc');
  const [taskOwnerId, setTaskOwnerId] = useState('');
  const requestIds = useRef<Record<string, number>>({}), requestControllers = useRef<Record<string, AbortController>>({});
  useEffect(() => () => { Object.values(requestControllers.current).forEach(controller => controller.abort()); }, []);
  async function latest<T>(key: string, path: string, apply: (value: T) => void) {
    requestControllers.current[key]?.abort();
    const controller = requestControllers.current[key] = new AbortController();
    const requestId = requestIds.current[key] = (requestIds.current[key] || 0) + 1;
    try {
      const value = await api<T>(path, { signal: controller.signal });
      if (requestIds.current[key] === requestId) apply(value);
    } catch (error) {
      if (!(error instanceof DOMException && error.name === 'AbortError')) throw error;
    }
  }
  async function loadSource(key: keyof Sources, nextPage = sourcePages[key]) {
    const params = new URLSearchParams({ page: String(nextPage), pageSize: '25', sortBy: sourceSort[key], sortDirection: sourceDirection[key] });
    if (sourceSearch[key]) params.set('search', sourceSearch[key]);
    if (key === 'seasons' && configure && companyId) params.set('companyId', companyId);
    let fallbackPage = 0;
    await latest<Page<Row>>(`source-${key}`, `/api/${key === 'seasons' ? 'crop-seasons' : key === 'plotSeasons' ? 'plot-seasons' : key}?${params}`, result => {
      setSources(current => ({ ...current, [key]: result.items }));
      setSourcePages(current => ({ ...current, [key]: result.page }));
      setSourceStates(current => ({ ...current, [key]: result }));
      if (result.items.length === 0 && result.page > 1) fallbackPage = result.page - 1;
    });
    if (fallbackPage) await loadSource(key, fallbackPage);
  }
  async function loadSources() { await Promise.all((Object.keys(sources) as (keyof Sources)[]).map(key => loadSource(key))); }
  async function loadReceipts(nextPage = receiptPage.page) {
    const params = new URLSearchParams({ page: String(nextPage), pageSize: '25', sortBy: receiptSort, sortDirection: receiptDirection });
    if (receiptQuery) params.set('search', receiptQuery);
    await latest<Page<Row>>('procurements', `/api/procurements?${params}`, result => { setReceipts(result.items); setReceiptPage(result); });
  }
  async function loadReference(nextPage = referencePage.page) {
    const params = new URLSearchParams({ page: String(nextPage), pageSize: '25', sortBy: referenceSort, sortDirection: referenceDirection });
    if (referenceQuery) params.set('search', referenceQuery);
    await latest<Row>('procurement-reference', `/api/procurement-reference-data?${params}`, result => { setReference(result); setReferencePage({ page: result.page, pageSize: result.pageSize, totalCount: result.totalCount, hasMore: result.hasMore }); });
  }
  async function load() { if (configure) setOrganizations((await api<Row[]>('/api/admin/organizations')).filter(row => row.kind === 'Customer')); }
  useEffect(() => { if (active && (canReadCapture || configure)) void action.run(load); }, [active, canReadCapture, configure]);
  useEffect(() => { if (active && purchase && sourceRead) void action.run(() => loadReceipts()); }, [active, purchase, sourceRead, receiptQuery, receiptSort, receiptDirection, receiptPage.page]);
  useEffect(() => { if (active && sourceRead && (!configure || companyId)) void action.run(loadSources); else if (active && configure && companyId) void action.run(() => loadSource('seasons')); }, [active, sourceRead, configure, companyId, sourceSearch, sourceSort, sourceDirection]);
  useEffect(() => { if (active && purchase && sourceRead) void action.run(() => loadReference()); }, [active, purchase, sourceRead, referenceQuery, referenceSort, referenceDirection, referencePage.page]);
  async function register(path: string, payload: Row) { await mutate<Row>(path, envelope(payload)); await loadSources(); setReferenceRevision(value => value + 1); }
  async function commit(payload: Row) { await mutate<Row>('/api/procurements', envelope(payload)); await loadReceipts(1); }
  async function loadReceivingTasks(nextPage = taskPage.page) {
    const query = new URLSearchParams({ facilityId, page: String(nextPage), pageSize: '25', sortBy: taskSort, sortDirection: taskDirection });
    if (taskQuery) query.set('search', taskQuery);
    if (taskStatus) query.set('status', taskStatus);
    if (taskOwnerId) query.set('ownerId', taskOwnerId);
    let fallbackPage = 0;
    await latest<Page<Row>>('receiving-tasks', `/api/ledger/receiving-tasks?${query}`, result => { setTasks(result.items); setTaskPage(result); if (result.items.length === 0 && result.page > 1) fallbackPage = result.page - 1; });
    if (fallbackPage) await loadReceivingTasks(fallbackPage);
    if (selectedTask) await latest<Row>('receiving-detail', `/api/ledger/receiving/${selectedTask.id}`, setSelectedTask);
  }
  function selectFacility(value: string) { requestControllers.current['receiving-detail']?.abort(); setFacilityId(value); setSelectedTask(null); setTaskPage(emptyPage); }
  useEffect(() => { if (active && facilityId) void action.run(() => loadReceivingTasks()); }, [active, facilityId, taskPage.page, taskQuery, taskStatus, taskOwnerId, taskSort, taskDirection]);
  async function submitMeasurements(task: Row, items: Row[], observations: string, capturedAt: string, operationId: string) {
    await mutate(`/api/ledger/receiving/${task.id}/measurements`, { operationId, expectedReceivingVersion: task.receivingVersion, capturedAt, observations, items });
    await loadReceivingTasks();
  }
  async function submitReview(task: Row, measurementSetId: string, actionName: string, reason: string, evidenceId: string, operationId: string) {
    await mutate(`/api/ledger/receiving/${task.id}/reviews`, { operationId, expectedReceivingVersion: task.receivingVersion, measurementSetId, action: actionName, reason, evidenceId });
    await loadReceivingTasks();
  }
  return <><Feedback {...action} />{sourceRead && <button type="button" disabled={action.busy} onClick={() => void action.run(async () => { if (configure) await load(); if (sourceRead && (!configure || companyId)) { await loadSources(); setReferenceRevision(value => value + 1); } if (purchase && sourceRead) { await loadReceipts(); await loadReference(); } if (facilityId) await loadReceivingTasks(); })}>Refresh Capture</button>}
    {configure && <Panel title="Company crop-season setup"><label>Customer company<Choice rows={organizations} value={companyId} onChange={value => { setCompanyId(value); setSourcePages(current => ({ ...current, seasons: 1 })); }} /></label>{companyId && <CropSeasonForm companyId={companyId} refresh={() => void action.run(async () => { await loadSource('seasons', 1); setReferenceRevision(value => value + 1); })} />}</Panel>}
    {configure && companyId && <Panel title="Company crop seasons"><SourceTable title="Crop seasons" rows={sources.seasons} page={sourceStates.seasons} search={sourceSearch.seasons} onSearch={value => { setSourceSearch(current => ({ ...current, seasons: value })); setSourcePages(current => ({ ...current, seasons: 1 })); }} sortBy={sourceSort.seasons} onSortBy={value => { setSourceSort(current => ({ ...current, seasons: value })); setSourcePages(current => ({ ...current, seasons: 1 })); }} sortDirection={sourceDirection.seasons} onSortDirection={value => { setSourceDirection(current => ({ ...current, seasons: value })); setSourcePages(current => ({ ...current, seasons: 1 })); }} onPage={n => loadSource('seasons', n)} editable={configure} onRefresh={() => loadSource('seasons')} onReferenceChange={() => setReferenceRevision(value => value + 1)} referenceRevision={referenceRevision} companyId={companyId} /></Panel>}
    {(canOperateReceiving || canReviewReceiving) && <ReceivingPanel operator={canOperateReceiving} manager={canReviewReceiving} facilityId={facilityId} tasks={tasks} taskPage={taskPage} taskQuery={taskQuery} taskStatus={taskStatus} taskOwnerId={taskOwnerId} taskSort={taskSort} taskDirection={taskDirection} setTaskQuery={value => { setTaskQuery(value); setTaskPage(current => ({ ...current, page: 1 })); }} setTaskStatus={value => { setTaskStatus(value); setTaskPage(current => ({ ...current, page: 1 })); }} setTaskOwnerId={value => { setTaskOwnerId(value); setTaskPage(current => ({ ...current, page: 1 })); }} setTaskSort={value => { setTaskSort(value); setTaskPage(current => ({ ...current, page: 1 })); }} setTaskDirection={value => { setTaskDirection(value); setTaskPage(current => ({ ...current, page: 1 })); }} selectedTask={selectedTask} setSelectedTask={setSelectedTask} chooseFacility={selectFacility} changePage={page => setTaskPage(current => ({ ...current, page }))} refresh={() => void action.run(() => loadReceivingTasks())} submitMeasurements={submitMeasurements} submitReview={submitReview} />}
    {purchase && sourceRead && <>
      <nav className="ledger-task-nav" aria-label="Purchasing tasks">
        <button type="button" aria-pressed={task === 'procurement'} onClick={() => setTask('procurement')}>Procurement</button>
        <button type="button" aria-pressed={task === 'sources'} onClick={() => setTask('sources')}>Source setup</button>
      </nav>
      <section hidden={task !== 'procurement'}><h2>Procurement</h2><p>Register expected source quantities. Mill staff must enter and confirm actual arrival before stock becomes available.</p>
        <ListControls label="Procurements" search={receiptQuery} onSearch={value => { setReceiptQuery(value); setReceiptPage(current => ({ ...current, page: 1 })); }} sortBy={receiptSort} onSortBy={value => { setReceiptSort(value); setReceiptPage(current => ({ ...current, page: 1 })); }} sortDirection={receiptDirection} onSortDirection={value => { setReceiptDirection(value); setReceiptPage(current => ({ ...current, page: 1 })); }} sortOptions={[{ value: 'receivedAt', label: 'Received at' }, { value: 'code', label: 'Code' }, { value: 'status', label: 'Status' }]} />
        <Panel title="Procurement records"><p>Pending procurements are not usable stock. Mill staff verify actual quantities at assigned facility.</p>{receipts.length ? <ul>{receipts.map(row => <li key={row.id}><button type="button" className="secondary" onClick={() => void action.run(async () => setSelected(await api<Procurement>(`/api/procurements/${row.id}`)))}>{row.code} — {receivingStatusLabel(row.status)}</button></li>)}</ul> : <p className="empty">No procurements yet. Register source records first, then add procurement.</p>}<Pagination label="Procurements" page={receiptPage.page} pageSize={receiptPage.pageSize} totalCount={receiptPage.totalCount} busy={action.busy} onChange={page => void action.run(() => loadReceipts(page))} />{selected && <details open><summary>{selected.code} procurement detail</summary><p>Status: {receivingStatusLabel(selected.status)}; received: {selected.receivedAt}; expected net mass: {selected.totalNetMassKg} kg. Pending arrival creates no usable mill stock.</p><Table rows={selected.items.map(item => ({ line: item.lineNo, expectedQuantity: item.measuredValue, unit: item.measuredUnit, expectedNetMassKg: item.netMassKg, actualQuantity: item.actualValue ? `${item.actualValue} ${item.actualUnit}` : 'Not measured', arrivalStatus: receivingStatusLabel(item.arrivalStatus || selected.status), usableStock: item.initialStock?.receivedKg === '0' ? '0 kg — not available' : item.initialStock?.receivedKg ? `${item.initialStock.receivedKg} kg` : 'Availability not provided' }))} actions={row => { const item = selected.items.find(candidate => candidate.lineNo === row.line); return item ? <button type="button" className="secondary" onClick={() => void action.run(async () => setSourceLot(await api<Row>(`/api/source-lots/${item.sourceLotId}`)))}>Source lot detail</button> : null; }} /><details><summary>Technical identifiers and full procurement record</summary><Table rows={[selected]} /><Table rows={selected.items} /></details></details>}{sourceLot && <details><summary>Source lot {sourceLot.lotCode} technical detail</summary><Table rows={[sourceLot]} /><Table rows={sourceLot.currentStock || []} /></details>}</Panel>
        <details className="ledger-disclosure" open={receipts.length === 0}><summary>Register procurement</summary><p>Need source records? <button type="button" className="secondary" onClick={() => setTask('sources')}>Open Source setup</button></p>{reference && <ProcurementForm reference={reference} commit={commit} referenceRevision={referenceRevision} />}</details>
        {reference && <><ListControls label="Receiving options" search={referenceQuery} onSearch={value => { setReferenceQuery(value); setReferencePage(current => ({ ...current, page: 1 })); }} sortBy={referenceSort} onSortBy={value => { setReferenceSort(value); setReferencePage(current => ({ ...current, page: 1 })); }} sortDirection={referenceDirection} onSortDirection={value => { setReferenceDirection(value); setReferencePage(current => ({ ...current, page: 1 })); }} sortOptions={[{ value: 'collectionSiteName', label: 'Collection site' }, { value: 'receivingLocationCode', label: 'Receiving location' }]} /><ul>{(reference.receivingOptions || []).map((option: Row) => <li key={`${option.collectionSiteId}:${option.receivingLocationId}`}>{option.collectionSiteName} — {option.receivingLocationCode}</li>)}</ul><Pagination label="Receiving options" page={referencePage.page} pageSize={referencePage.pageSize} totalCount={referencePage.totalCount} busy={action.busy} onChange={page => setReferencePage(current => ({ ...current, page }))} /></>}
      </section>
      <section hidden={task !== 'sources'}>
        <h2>Source setup</h2>
        <p>Register farmer, plot, crop season, then link plot to season. Use Procurement task after source records are ready.</p>
        {sourceWrite && <SourceForms register={register} referenceRevision={referenceRevision} />}
        <Panel title="Source records">
          {(['farmers', 'plots', 'seasons', 'plotSeasons'] as (keyof Sources)[]).map(key => (
            <SourceTable
              key={key}
              title={{ farmers: 'Farmers', plots: 'Plots', seasons: 'Crop seasons', plotSeasons: 'Plot seasons' }[key]}
              rows={sources[key]}
              page={sourceStates[key]}
              search={sourceSearch[key]}
              onSearch={value => { setSourceSearch(current => ({ ...current, [key]: value })); setSourcePages(current => ({ ...current, [key]: 1 })); }}
              sortBy={sourceSort[key]}
              onSortBy={value => { setSourceSort(current => ({ ...current, [key]: value })); setSourcePages(current => ({ ...current, [key]: 1 })); }}
              sortDirection={sourceDirection[key]}
              onSortDirection={value => { setSourceDirection(current => ({ ...current, [key]: value })); setSourcePages(current => ({ ...current, [key]: 1 })); }}
              onPage={nextPage => loadSource(key, nextPage)}
              editable={sourceWrite}
              onRefresh={() => loadSource(key)}
              companyId={companyId || user.organizationId || ''}
              onReferenceChange={() => setReferenceRevision(value => value + 1)}
              referenceRevision={referenceRevision}
            />
          ))}
        </Panel>
      </section>
    </>}
    {!purchase && sourceRead && <section>
      <h2>Source setup</h2>
      <p>Register source records in prerequisite order: farmer, plot, crop season, then plot-season link.</p>
      {sourceWrite && <SourceForms register={register} referenceRevision={referenceRevision} />}
      <Panel title="Source records">
        <span className="sr-only">{sources.farmers.length} farmer references loaded on current source page.</span>
        {(['farmers', 'plots', 'seasons', 'plotSeasons'] as (keyof Sources)[]).map(key => (
          <SourceTable
            key={key}
            title={{ farmers: 'Farmers', plots: 'Plots', seasons: 'Crop seasons', plotSeasons: 'Plot seasons' }[key]}
            rows={sources[key]}
            page={sourceStates[key]}
            search={sourceSearch[key]}
            onSearch={value => { setSourceSearch(current => ({ ...current, [key]: value })); setSourcePages(current => ({ ...current, [key]: 1 })); }}
            sortBy={sourceSort[key]}
            onSortBy={value => { setSourceSort(current => ({ ...current, [key]: value })); setSourcePages(current => ({ ...current, [key]: 1 })); }}
            sortDirection={sourceDirection[key]}
            onSortDirection={value => { setSourceDirection(current => ({ ...current, [key]: value })); setSourcePages(current => ({ ...current, [key]: 1 })); }}
            onPage={nextPage => loadSource(key, nextPage)}
            editable={sourceWrite}
            onRefresh={() => loadSource(key)}
            onReferenceChange={() => setReferenceRevision(value => value + 1)}
            referenceRevision={referenceRevision}
            companyId={companyId || user.organizationId || ''}
          />
        ))}
      </Panel>
    </section>}
  </>;
}
function ReceivingPanel({ operator, manager, facilityId, tasks, taskPage, taskQuery, taskStatus, taskOwnerId, taskSort, taskDirection, setTaskQuery, setTaskStatus, setTaskOwnerId, setTaskSort, setTaskDirection, selectedTask, setSelectedTask, chooseFacility, changePage, refresh, submitMeasurements, submitReview }: {
  operator: boolean; manager: boolean; facilityId: string; tasks: Row[]; taskPage: ListState; taskQuery: string; taskStatus: string; taskOwnerId: string; taskSort: string; taskDirection: 'asc' | 'desc';
  setTaskQuery: (value: string) => void; setTaskStatus: (value: string) => void; setTaskOwnerId: (value: string) => void; setTaskSort: (value: string) => void; setTaskDirection: (value: 'asc' | 'desc') => void;
  selectedTask: Row | null; setSelectedTask: (task: Row | null) => void; chooseFacility: (value: string) => void; changePage: (page: number) => void;
  refresh: () => void; submitMeasurements: (task: Row, items: Row[], observations: string, capturedAt: string, operationId: string) => Promise<void>;
  submitReview: (task: Row, setId: string, action: string, reason: string, evidenceId: string, operationId: string) => Promise<void>;
}) {
  const action = useAction(), [facilities, setFacilities] = useState<Row[]>([]), [owners, setOwners] = useState<Row[]>([]), taskDetailRequest = useRef<AbortController | null>(null);
  useEffect(() => { void action.run(async () => { const lookups = await api<{ facilities: Row[]; owners: Row[] }>('/api/ledger/lookups'); setFacilities(lookups.facilities); setOwners(lookups.owners); }); return () => taskDetailRequest.current?.abort(); }, []);
  async function openTask(taskId: string) { taskDetailRequest.current?.abort(); const controller = taskDetailRequest.current = new AbortController(); await action.run(async () => { try { setSelectedTask(await api<Row>(`/api/ledger/receiving/${taskId}`, { signal: controller.signal })); } catch (error) { if (!(error instanceof DOMException && error.name === 'AbortError')) throw error; } }); }
  return <Panel title={operator ? 'Mill arrival measurement' : 'Mill manager arrival review'}>
    <Feedback {...action} /><p>Tasks and records shown only within facilities assigned to your account. Pending arrivals are not usable stock.</p>
    <label>Assigned facility<Choice rows={facilities} value={facilityId} onChange={value => { taskDetailRequest.current?.abort(); setSelectedTask(null); void chooseFacility(value); }} /></label>
    <button type="button" className="secondary" disabled={!facilityId || action.busy} onClick={refresh}>Refresh receiving tasks</button>
    {facilityId && <><ListControls label="Receiving tasks" search={taskQuery} onSearch={setTaskQuery} sortBy={taskSort} onSortBy={setTaskSort} sortDirection={taskDirection} onSortDirection={setTaskDirection} sortOptions={[{ value: 'receivedAt', label: 'Received at' }, { value: 'status', label: 'Status' }, { value: 'owner', label: 'Owner' }]}><label>Filter receiving tasks by status<select value={taskStatus} onChange={event => setTaskStatus(event.target.value)}><option value="">All statuses</option>{['PENDING_ARRIVAL', 'AWAITING_MANAGER_REVIEW', 'REMEASUREMENT_REQUESTED', 'BLOCKED_UNRESOLVED', 'RECEIVED_MATCHED', 'RECEIVED_SHORTAGE_APPROVED'].map(status => <option key={status} value={status}>{receivingStatusLabel(status)}</option>)}</select></label><label>Receiving owner<Choice rows={owners} value={taskOwnerId} optional onChange={setTaskOwnerId} /></label></ListControls><Pagination label="Receiving tasks" page={taskPage.page} pageSize={taskPage.pageSize} totalCount={taskPage.totalCount} busy={action.busy} onChange={changePage} /></>}
    {!facilityId ? <p className="empty">Select an assigned facility to view its arrival tasks.</p> : tasks.length === 0 ? <p className="empty">No receiving tasks pending in this facility.</p> : <ul>{tasks.map(task => <li key={task.id}><button type="button" className="secondary" onClick={() => void openTask(task.id)}>{task.code} — {receivingStatusLabel(task.status)} — {task.ownerCompanyName || task.ownerId}</button></li>)}</ul>}
    {selectedTask && <ReceivingTaskDetail key={selectedTask.id} operator={operator} manager={manager} task={selectedTask} submitMeasurements={submitMeasurements} submitReview={submitReview} />}
  </Panel>;
}
function ReceivingTaskDetail({ operator, manager, task, submitMeasurements, submitReview }: {
  operator: boolean; manager: boolean; task: Row;
  submitMeasurements: (task: Row, items: Row[], observations: string, capturedAt: string, operationId: string) => Promise<void>;
  submitReview: (task: Row, setId: string, action: string, reason: string, evidenceId: string, operationId: string) => Promise<void>;
}) {
  const action = useAction(), [measurements, setMeasurements] = useState<Record<string, { value: string; unit: string }>>({}), [observations, setObservations] = useState(''), [confirmed, setConfirmed] = useState(false), [measurementOperationId, setMeasurementOperationId] = useState(id()), [measurementCapturedAt, setMeasurementCapturedAt] = useState(() => new Date().toISOString()), [reviewOperationId, setReviewOperationId] = useState(id());
  const [reviewAction, setReviewAction] = useState(''), [reason, setReason] = useState(''), [evidenceId, setEvidenceId] = useState('');
  const pending = task.status === 'PENDING_ARRIVAL' || task.status === 'REMEASUREMENT_REQUESTED';
  const reviewable = task.status === 'AWAITING_MANAGER_REVIEW' || task.status === 'BLOCKED_UNRESOLVED';
  const latestSet = (task.measurementSets || []).find((set: Row) => set.id === task.latestMeasurementSetId);
  const mismatch = (task.items || []).some((line: Row) => line.actualKg !== null && compareDecimal(line.actualKg, line.expectedKg) !== 0);
  const surplus = (task.items || []).some((line: Row) => line.actualKg !== null && compareDecimal(line.actualKg, line.expectedKg) > 0);
  async function measure(event: FormEvent) {
    event.preventDefault();
    await action.run(async () => {
      const items = task.items.map((line: Row) => {
        const actual = measurements[line.id];
        if (!actual?.value || !actual.unit || !/[1-9]/.test(actual.value)) throw Error(`Enter a positive actual quantity and unit for line ${line.lineNo}.`);
        return { procurementItemId: line.id, measuredValue: actual.value, measuredUnit: actual.unit };
      });
      if (!confirmed) throw Error('Confirm all actual quantities measured and correct before submitting.');
      await submitMeasurements(task, items, observations, measurementCapturedAt, measurementOperationId);
      setMeasurementOperationId(id()); setMeasurementCapturedAt(new Date().toISOString()); setMeasurements({}); setObservations(''); setConfirmed(false);
    }, 'Measurements recorded. Mismatches need manager review and create no usable stock.');
  }
  async function review(event: FormEvent) {
    event.preventDefault();
    if (!latestSet || !reviewAction) return;
    await action.run(async () => { await submitReview(task, latestSet.id, reviewAction, reason, evidenceId, reviewOperationId); setReviewOperationId(id()); setReason(''); setEvidenceId(''); setReviewAction(''); }, 'Manager review recorded.');
  }
  return <section className="receiving-detail"><h3>{task.code} — {receivingStatusLabel(task.status)}</h3>
    <p>{task.ownerCompanyName} · {task.facilityName} · {task.receivingLocationName}</p>
    <p>Expected total: {task.expectedTotalKg} kg · Actual total: {task.actualTotalKg ?? 'Not measured'} kg · Usable stock: {task.usableStockKg} kg</p>
    <div className="table-scroll"><table><thead><tr><th>Line</th><th>Source lot</th><th>Expected</th><th>Actual</th><th>Difference</th></tr></thead><tbody>{task.items.map((line: Row) => <tr key={line.id}><td>{line.lineNo}</td><td>{line.lotCode}</td><td>{line.expectedValue} {line.expectedUnit} ({line.expectedKg} kg)</td><td>{line.actualValue === null ? 'Not measured' : `${line.actualValue} ${line.actualUnit} (${line.actualKg} kg)`}</td><td>{line.actualKg === null ? 'Awaiting measurement' : compareDecimal(line.actualKg, line.expectedKg) === 0 ? 'Exact match' : compareDecimal(line.actualKg, line.expectedKg) < 0 ? 'Shortage — manager review' : 'Surplus — cannot approve'}</td></tr>)}</tbody></table></div>
    {(task.measurementSets || []).length > 0 && <Table label="Measurement sets" rows={task.measurementSets.map((set: Row, index: number) => ({ order: index + 1, recordedAt: set.recordedAt, observations: set.observations || 'None', lineCount: set.items.length }))} actions={row => { const set = task.measurementSets[row.order - 1]; return set ? <details><summary>View {row.lineCount} measured lines</summary><p>Observations: {set.observations || 'None'}</p><Table label="Measurement lines" rows={set.items} /></details> : null; }} />}
    {(task.reviews || []).length > 0 && <Table label="Manager reviews" rows={task.reviews.map((review: Row, index: number) => ({ order: index + 1, recordedAt: review.recordedAt, action: review.action, reason: review.reason, evidenceId: review.evidenceId || '—' }))} actions={row => { const review = task.reviews[row.order - 1]; return review?.evidenceId ? <a href={`/api/ledger/evidence/${review.evidenceId}`} target="_blank" rel="noreferrer">View review evidence</a> : null; }} />}
    {operator && pending && <form onSubmit={e => void measure(e)}><h4>Measure every line</h4><p>Enter actual quantity in each line’s unit. Exact match confirms arrival and makes actual stock available; any mismatch awaits manager review with no stock posted.</p>{task.items.map((line: Row) => <fieldset key={line.id}><legend>Line {line.lineNo} — {line.lotCode}</legend><p>Expected {line.expectedValue} {line.expectedUnit} · {line.expectedKg} kg</p><label>Actual received quantity<input required inputMode="decimal" pattern="[0-9]+([.][0-9]{1,6})?" value={measurements[line.id]?.value || ''} onChange={e => setMeasurements(current => ({ ...current, [line.id]: { value: e.target.value, unit: current[line.id]?.unit || '' } }))} /></label><label>Actual unit<select required value={measurements[line.id]?.unit || ''} onChange={e => setMeasurements(current => ({ ...current, [line.id]: { value: current[line.id]?.value || '', unit: e.target.value } }))}><option value="">Select unit…</option><option value="KG">Kilograms (kg)</option><option value="G">Grams (g)</option><option value="T">Tonnes (t)</option></select></label></fieldset>)}<label>Observations<textarea value={observations} onChange={e => setObservations(e.target.value)} /></label><label className="checkbox-label"><input type="checkbox" required checked={confirmed} onChange={e => setConfirmed(e.target.checked)} />I confirm all actual quantities are measured correctly and understand an exact match confirms arrival and posts stock.</label><button disabled={action.busy}>Submit arrival measurements</button></form>}
    {manager && reviewable && latestSet && <form onSubmit={e => void review(e)}><h4>Review latest measurement set</h4><p>Decision bound to measurement set {latestSet.id} and receiving version {task.receivingVersion}. Evidence required; upload scoped to this owner and facility.</p>{surplus && <p className="notice error">Surplus detected. Shortage approval unavailable; keep blocked or request remeasurement.</p>}<label>Review action<select required value={reviewAction} onChange={e => setReviewAction(e.target.value)}><option value="">Choose action…</option><option value="APPROVE_SHORTAGE" disabled={surplus || !mismatch}>Approve shortage and post actual stock</option><option value="REQUEST_REMEASUREMENT">Request remeasurement</option><option value="KEEP_BLOCKED">Keep blocked — no usable stock</option></select></label><label>Reason<textarea required value={reason} onChange={e => setReason(e.target.value)} /></label><Evidence ownerId={task.ownerCompanyId} facilityId={task.facilityId} value={evidenceId} onChange={setEvidenceId} /><button disabled={action.busy || !reviewAction || !reason.trim() || !evidenceId}>Record manager decision</button></form>}
    {operator && !pending && <p className="notice pending">Task not accepting new measurements in current state.</p>}
    {manager && !reviewable && <p className="notice pending">No manager decision available in current state.</p>}
    <Feedback {...action} />
  </section>;
}
function SourceTable({ title, rows, page, search, onSearch, sortBy, onSortBy, sortDirection, onSortDirection, onPage, editable, onRefresh, onReferenceChange, referenceRevision, companyId = '' }: { title: string; rows: Row[]; page: ListState; search: string; onSearch: (value: string) => void; sortBy: string; onSortBy: (value: string) => void; sortDirection: 'asc' | 'desc'; onSortDirection: (value: 'asc' | 'desc') => void; onPage: (page: number) => Promise<void>; editable: boolean; onRefresh: () => Promise<void>; onReferenceChange: () => void; referenceRevision: number; companyId?: string }) {
  const action = useAction(), path = title === 'Farmers' ? 'farmers' : title === 'Plots' ? 'plots' : title === 'Crop seasons' ? 'crop-seasons' : 'plot-seasons';
  const recordName = (row: Row) => row.name || row.code || (row.riceVariety ? `${row.riceVariety} — ${row.plotCode} — ${row.seasonName}` : 'source record');
  async function remove(row: Row) { const name = recordName(row); if (!row.canDelete || !window.confirm(`Archive ${name}? Stored record and history will be retained; it will no longer be available for new links.`)) return; await action.run(async () => { await mutate(`/api/${path}/${row.id}`, { version: row.version }, 'DELETE'); await onRefresh(); onReferenceChange(); }, 'Source record archived; stored history retained.'); }
  const sortOptions = title === 'Farmers' ? referenceSortOptions.farmers : title === 'Plots' ? referenceSortOptions.plots : title === 'Crop seasons' ? referenceSortOptions['crop-seasons'] : referenceSortOptions['plot-seasons'];
  return <section><h3>{title}</h3><Feedback {...action} /><ListControls label={title} search={search} onSearch={onSearch} sortBy={sortBy} onSortBy={onSortBy} sortDirection={sortDirection} onSortDirection={onSortDirection} sortOptions={sortOptions} />{rows.length ? <ul>{rows.map((row, index) => <li key={row.id || index}>{recordName(row)}{editable && row.canEdit && <SourceRecordEditor row={row} type={path} companyId={companyId} referenceRevision={referenceRevision} onSave={async payload => { const saved = await action.run(async () => { const updated = await mutate<Row>(`/api/${path}/${row.id}`, payload, 'PUT'); await onRefresh(); onReferenceChange(); return updated; }, 'Source record updated.'); return saved?.version; }} />}{editable && row.canDelete && <button type="button" disabled={action.busy} onClick={() => void remove(row)}>Archive {recordName(row)}</button>}</li>)}</ul> : <p className="empty">No {title.toLowerCase()} found.</p>}<details><summary>Technical identifiers, permissions and fields</summary><Table rows={rows} controls={false} columns={Array.from(new Set(rows.flatMap(row => Object.keys(row))))} /></details><Pagination label={title} page={page.page} pageSize={page.pageSize} totalCount={page.totalCount} busy={action.busy} onChange={next => void action.run(() => onPage(next))} /></section>;
}
type CaptureReference = 'farmers' | 'plots' | 'crop-seasons' | 'plot-seasons';
const referenceSortOptions: Record<CaptureReference, { value: string; label: string }[]> = {
  farmers: [{ value: 'name', label: 'Name' }, { value: 'code', label: 'Code' }, { value: 'createdAt', label: 'Created at' }],
  plots: [{ value: 'code', label: 'Code' }, { value: 'areaHa', label: 'Area (ha)' }],
  'crop-seasons': [{ value: 'name', label: 'Name' }, { value: 'startsOn', label: 'Starts on' }],
  'plot-seasons': [{ value: 'id', label: 'Record' }]
};
function CaptureReferencePicker({ label, resource, value, onChange, filter = '', referenceRevision = 0 }: { label: string; resource: CaptureReference; value: string; onChange: (value: string) => void; filter?: string; referenceRevision?: number }) {
  const [items, setItems] = useState<Row[]>([]), [page, setPage] = useState<ListState>(emptyPage), [search, setSearch] = useState(''), [sortBy, setSortBy] = useState(referenceSortOptions[resource][0].value), [direction, setDirection] = useState<'asc' | 'desc'>('asc'), [pinned, setPinned] = useState<Row | null>(null), [error, setError] = useState('');
  useEffect(() => {
    const controller = new AbortController(), params = new URLSearchParams({ page: String(page.page), pageSize: '25', sortBy, sortDirection: direction });
    if (search) params.set('search', search);
    if (filter) new URLSearchParams(filter).forEach((value, key) => params.set(key, value));
    void api<Page<Row>>(`/api/${resource}?${params}`, { signal: controller.signal }).then(result => {
      const maxPage = Math.max(1, Math.ceil(result.totalCount / Math.max(1, result.pageSize)));
      if (result.page > maxPage) {
        setPage(current => ({ ...current, page: maxPage }));
        return;
      }
      setItems(result.items); setPage(result); setError('');
    }).catch(cause => { if (!(cause instanceof DOMException && cause.name === 'AbortError')) setError(cause instanceof Error ? cause.message : 'Could not load reference options.'); });
    return () => controller.abort();
  }, [resource, page.page, search, sortBy, direction, filter, referenceRevision]);
  useEffect(() => { setPage(current => current.page === 1 ? current : { ...current, page: 1 }); }, [filter]);
  useEffect(() => {
    if (!value) { setPinned(null); return; }
    const controller = new AbortController();
    void api<Row>(`/api/${resource}/${value}`, { signal: controller.signal }).then(row => { setPinned(row); setError(''); }).catch(cause => { if (!(cause instanceof DOMException && cause.name === 'AbortError')) { setPinned(null); setError(cause instanceof Error ? cause.message : 'Could not load selected reference.'); } });
    return () => controller.abort();
  }, [resource, value, page.page, search, sortBy, direction, filter, referenceRevision]);
  const display = (row: Row) => resource === 'plot-seasons' ? { ...row, label: `${row.riceVariety} — ${row.plotCode} — ${row.seasonName}` } : row;
  const choices = pinned && pinned.id === value && !items.some(item => item.id === value) ? [...items.map(display), display(pinned)] : items.map(display);
  return <fieldset className="capture-reference-picker"><legend>{label}</legend><ListControls label={label} search={search} onSearch={value => { setSearch(value); setPage(current => ({ ...current, page: 1 })); }} sortBy={sortBy} onSortBy={value => { setSortBy(value); setPage(current => ({ ...current, page: 1 })); }} sortDirection={direction} onSortDirection={value => { setDirection(value); setPage(current => ({ ...current, page: 1 })); }} sortOptions={referenceSortOptions[resource]}><label>{label}<Choice rows={choices} value={value} onChange={next => { setPinned(items.find(item => item.id === next) || null); onChange(next); }} /></label></ListControls>{error && <p className="notice error" role="alert">{error}</p>}<Pagination label={label} page={page.page} pageSize={page.pageSize} totalCount={page.totalCount} onChange={next => setPage(current => ({ ...current, page: next }))} /></fieldset>;
}
function SourceRecordEditor({ row, type, companyId, referenceRevision, onSave }: { row: Row; type: string; companyId: string; referenceRevision: number; onSave: (payload: Row) => Promise<number | undefined> }) {
  const [expectedVersion, setExpectedVersion] = useState(row.version);
  const [name, setName] = useState(row.name || ''), [contact, setContact] = useState(row.contact || ''), [farmerId, setFarmerId] = useState(row.farmerId || ''), [code, setCode] = useState(row.code || ''), [area, setArea] = useState(String(row.areaHa ?? '')), [latitude, setLatitude] = useState(''), [longitude, setLongitude] = useState(''), [geometry, setGeometry] = useState<Row | null>(null), [startsOn, setStartsOn] = useState(row.startsOn?.slice(0, 10) || ''), [endsOn, setEndsOn] = useState(row.endsOn?.slice(0, 10) || ''), [plotId, setPlotId] = useState(row.plotId || ''), [seasonId, setSeasonId] = useState(row.seasonId || ''), [riceVariety, setRiceVariety] = useState(row.riceVariety || ''), [detailsLoaded, setDetailsLoaded] = useState(type !== 'plots');
  const action = useAction();
  async function loadDetails(open: boolean) { if (!open || detailsLoaded || type !== 'plots') return; await action.run(async () => { const detail = await api<Row>(`/api/${type}/${row.id}`); setGeometry(detail.geometry); if (detail.geometry?.type === 'Point') { setLatitude(String(detail.geometry.coordinates[1])); setLongitude(String(detail.geometry.coordinates[0])); } setDetailsLoaded(true); }); }
  async function submit(event: FormEvent) { event.preventDefault(); const version = await onSave(type === 'farmers' ? { expectedVersion, code: row.code, name, contact: contact || null } : type === 'plots' ? { expectedVersion, farmerId, code, areaHa: Number(area), geometry: geometry?.type === 'Point' ? { type: 'Point', coordinates: [Number(longitude), Number(latitude)] } : geometry } : type === 'crop-seasons' ? { companyId: row.companyId || companyId, expectedVersion, name, startsOn, endsOn } : { expectedVersion, plotId, seasonId, riceVariety }); if (version !== undefined) setExpectedVersion(version); }
  return <details onToggle={event => void loadDetails(event.currentTarget.open)}><summary>Edit source record</summary><Feedback {...action} /><form onSubmit={submit}>{type === 'farmers' ? <><label>Farmer name<input required value={name} onChange={event => setName(event.target.value)} /></label><label>Contact<input value={contact} onChange={event => setContact(event.target.value)} /></label></> : type === 'plots' ? <><CaptureReferencePicker label="Farmer" resource="farmers" value={farmerId} onChange={setFarmerId} referenceRevision={referenceRevision} /><label>Plot code<input required value={code} onChange={event => setCode(event.target.value)} /></label><label>Area (ha)<input required inputMode="decimal" value={area} onChange={event => setArea(event.target.value)} /></label>{geometry?.type === 'Point' ? <><label>Latitude<input required type="number" min="-90" max="90" step="any" value={latitude} onChange={event => setLatitude(event.target.value)} /></label><label>Longitude<input required type="number" min="-180" max="180" step="any" value={longitude} onChange={event => setLongitude(event.target.value)} /></label></> : <p>{geometry ? `${geometry.type} geometry preserved; coordinates remain unchanged.` : 'Loading plot geometry…'}</p>}</> : type === 'crop-seasons' ? <><label>Season name<input required value={name} onChange={event => setName(event.target.value)} /></label><label>Starts<input required type="date" value={startsOn} onChange={event => setStartsOn(event.target.value)} /></label><label>Ends<input required type="date" value={endsOn} onChange={event => setEndsOn(event.target.value)} /></label></> : <><CaptureReferencePicker label="Plot" resource="plots" value={plotId} onChange={setPlotId} referenceRevision={referenceRevision} /><CaptureReferencePicker label="Crop season" resource="crop-seasons" value={seasonId} onChange={setSeasonId} filter={companyId ? `companyId=${companyId}` : ''} referenceRevision={referenceRevision} /><label>Rice variety<input required value={riceVariety} onChange={event => setRiceVariety(event.target.value)} /></label></>}<button disabled={action.busy || (type === 'plots' && !detailsLoaded)}>Save changes</button></form></details>;
}
function SourceForms({ register, referenceRevision }: { register: (path: string, payload: Row) => Promise<void>; referenceRevision: number }) {
  const action = useAction(), [farmerName, setFarmerName] = useState(''), [contact, setContact] = useState(''), [farmer, setFarmer] = useState(''), [plotName, setPlotName] = useState(''), [area, setArea] = useState(''), [latitude, setLatitude] = useState(''), [longitude, setLongitude] = useState(''), [plot, setPlot] = useState(''), [season, setSeason] = useState(''), [variety, setVariety] = useState('');
  async function submit(event: FormEvent, path: string, payload: Row) { event.preventDefault(); await action.run(() => register(path, payload), 'Capture source registered.'); }
  return <Panel title="Source registration"><Feedback {...action} /><p>Register prerequisites in order: farmer → plot → crop season → link plot and season. Later steps require earlier records.</p><details open><summary>Step 1 — register farmer</summary><form onSubmit={e => void submit(e, '/api/farmers', { entityId: id(), code: null, name: farmerName, contact: contact || null })}><label>Farmer name<input required value={farmerName} onChange={e => setFarmerName(e.target.value)} /></label><label>Contact (optional)<input value={contact} onChange={e => setContact(e.target.value)} /></label><button disabled={action.busy}>Register farmer</button></form></details>
    <details><summary>Step 2 — register plot</summary><form onSubmit={e => void submit(e, '/api/plots', { entityId: id(), farmerId: farmer, code: plotName, areaHa: Number(area), geometry: { type: 'Point', coordinates: [Number(longitude), Number(latitude)] } })}><CaptureReferencePicker label="Farmer" resource="farmers" value={farmer} onChange={setFarmer} referenceRevision={referenceRevision} /><label>Plot code<input required value={plotName} onChange={e => setPlotName(e.target.value)} /></label><label>Area (ha)<input required inputMode="decimal" pattern="[0-9]+([.][0-9]{1,12})?" value={area} onChange={e => setArea(e.target.value)} /></label><label>Latitude<input required type="number" min="-90" max="90" step="any" value={latitude} onChange={e => setLatitude(e.target.value)} /></label><label>Longitude<input required type="number" min="-180" max="180" step="any" value={longitude} onChange={e => setLongitude(e.target.value)} /></label><button disabled={action.busy || !farmer}>Register plot</button></form></details>
    <details><summary>Step 3 — crop-season prerequisite</summary><p>Administrator creates crop seasons for your customer company. Ask administrator if no suitable season exists.</p></details>
    <details><summary>Step 4 — link plot and season</summary><form onSubmit={e => void submit(e, '/api/plot-seasons', { entityId: id(), plotId: plot, seasonId: season, riceVariety: variety })}><CaptureReferencePicker label="Plot" resource="plots" value={plot} onChange={setPlot} referenceRevision={referenceRevision} /><CaptureReferencePicker label="Crop season" resource="crop-seasons" value={season} onChange={setSeason} referenceRevision={referenceRevision} /><label>Rice variety<input required value={variety} onChange={e => setVariety(e.target.value)} /></label><button disabled={action.busy || !plot || !season}>Register plot season</button></form></details></Panel>;
}
function CropSeasonForm({ companyId, refresh }: { companyId: string; refresh: () => void }) { const [name, setName] = useState(''), [startsOn, setStarts] = useState(''), [endsOn, setEnds] = useState(''), action = useAction(); return <Panel title="Admin crop-season creation"><Feedback {...action} /><form onSubmit={e => { e.preventDefault(); void action.run(async () => { await mutate('/api/crop-seasons', { companyId, name, startsOn, endsOn }); setName(''); refresh(); }, 'Crop season created.'); }}><label>Season name<input required value={name} onChange={e => setName(e.target.value)} /></label><label>Starts<input required type="date" value={startsOn} onChange={e => setStarts(e.target.value)} /></label><label>Ends<input required type="date" value={endsOn} onChange={e => setEnds(e.target.value)} /></label><button disabled={action.busy}>Create crop season</button></form></Panel>; }
function ProcurementForm({ reference, commit, referenceRevision }: { reference: Row; commit: (payload: Row) => Promise<void>; referenceRevision: number }) {
 const action = useAction(), [farmer, setFarmer] = useState(''), [plot, setPlot] = useState(''), [site, setSite] = useState(''), [location, setLocation] = useState(''), [received, setReceived] = useState(''), [plotSeason, setPlotSeason] = useState(''), [mass, setMass] = useState(''), [unit, setUnit] = useState(''), [moisture, setMoisture] = useState(''), [pinnedOption, setPinnedOption] = useState<Row | null>(null);
 const pageOptions = reference.receivingOptions || [], options = pinnedOption && !pageOptions.some((option: Row) => option.collectionSiteId === pinnedOption.collectionSiteId && option.receivingLocationId === pinnedOption.receivingLocationId) ? [pinnedOption, ...pageOptions] : pageOptions, selectedOption = options.find((x: Row) => x.collectionSiteId === site && x.receivingLocationId === location);
 useEffect(() => { const match = pageOptions.find((option: Row) => option.collectionSiteId === site && option.receivingLocationId === location); if (match && pinnedOption?.receivingLocationId !== match.receivingLocationId) setPinnedOption(match); }, [pageOptions, site, location, pinnedOption]);
 return <Panel title="Register procurement"><Feedback {...action} />{!reference.commodity || !reference.massUnits?.length || !options.length ? <p className="notice">Procurement unavailable: Capture reference data has no configured PADDY commodity, mass units, or eligible receiving options.</p> : <form onSubmit={e => { e.preventDefault(); if (!selectedOption) return; void action.run(() => commit({ entityId: id(), farmerId: farmer, collectionSiteId: site, receivingLocationId: location, commodityId: reference.commodity.id, receivedAt: iso(received), items: [{ itemId: id(), lineNo: 1, plotSeasonId: plotSeason, measuredValue: mass, measuredUnit: unit, moisturePercent: moisture, lotId: id(), sourceLotId: id() }] }), 'Procurement registered. Pending mill arrival confirmation; stock is not yet available.'); }}>
 <p>Complete in order: farmer, eligible receiving site and location, plot season, expected source mass and unit, moisture, received time. Quantity stays exact decimal text. Mill staff measure each line separately before stock becomes available.</p><CaptureReferencePicker label="Farmer" resource="farmers" value={farmer} onChange={value => { setFarmer(value); setPlot(''); setPlotSeason(''); }} referenceRevision={referenceRevision} />
 <label>Collection site<Choice rows={Object.values(options.reduce((sites: Record<string, Row>, row: Row) => { sites[row.collectionSiteId] ||= { id: row.collectionSiteId, name: row.collectionSiteName }; return sites; }, {} as Record<string, Row>))} value={site} onChange={v => { setSite(v); setLocation(''); }} /></label><label>Receiving location<Choice rows={options.filter((x: Row) => x.collectionSiteId === site).map((x: Row) => ({ id: x.receivingLocationId, name: `${x.receivingLocationCode} — ${x.collectionSiteName}` }))} value={location} onChange={setLocation} /></label>
 {farmer ? <CaptureReferencePicker label="Plot" resource="plots" value={plot} onChange={value => { setPlot(value); setPlotSeason(''); }} filter={`farmerId=${farmer}`} referenceRevision={referenceRevision} /> : <p>Select farmer to find plots.</p>}{plot ? <CaptureReferencePicker label="Plot season" resource="plot-seasons" value={plotSeason} onChange={setPlotSeason} filter={`plotId=${plot}`} referenceRevision={referenceRevision} /> : <p>Select eligible farmer plot to find plot seasons.</p>}
 <label>Expected source quantity (exact decimal string)<input required inputMode="decimal" pattern="[0-9]+([.][0-9]{1,12})?" value={mass} onChange={e => setMass(e.target.value)} /></label><label>Unit<Choice rows={reference.massUnits.map((x: Row) => ({ id: x.code, name: x.name }))} value={unit} onChange={setUnit} /></label><label>Moisture percent<input required inputMode="decimal" pattern="[0-9]+([.][0-9]{1,12})?" value={moisture} onChange={e => setMoisture(e.target.value)} /></label><label>Received at<input required type="datetime-local" value={received} onChange={e => setReceived(e.target.value)} /></label><button disabled={action.busy || !selectedOption || !farmer || !plot || !plotSeason}>Register procurement</button></form>}</Panel>;
}
