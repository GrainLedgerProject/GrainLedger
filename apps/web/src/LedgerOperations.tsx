import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, errorMessage, type User } from './api';
import { Choice, Enum, Evidence, Feedback, ListControls, Mass, Pagination, Panel, Table, emptyLookup, id, iso, kinds, localTime, mutate, name, now, positive, useAction, type Lookup, type Lot, type Operation, type Page, type Row } from './ledger-ui';
import { LedgerLotPicker } from './LedgerLotPicker';
import { TransferForm } from './LedgerStock';

const operationSortOptions = [{ value: 'occurredAt', label: 'Occurred at' }, { value: 'kind', label: 'Kind' }, { value: 'owner', label: 'Owner' }, { value: 'status', label: 'Status' }];
const correctionSortOptions = [{ value: 'requestedAt', label: 'Requested at' }, { value: 'status', label: 'Status' }, { value: 'owner', label: 'Owner' }];
const emptyPage = <T,>(): Page<T> => ({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });

function usePagedList<T>(path: string, scope: Record<string, string>, active: boolean, refresh: number, sortBy = 'occurredAt') {
  const [filters, setFilters] = useState({ page: '1', pageSize: '25', search: '', sortBy, sortDirection: 'desc', kind: '', ownerId: '', facilityId: '', status: '', queueOnly: '' });
  const query = new URLSearchParams(Object.entries({ ...filters, ...scope }).filter(([, value]) => value !== '')).toString();
  const [blank] = useState(() => emptyPage<T>());
  const [result, setResult] = useState<{ query: string; data: Page<T> }>({ query: '', data: blank });
  const [busy, setBusy] = useState(false), [error, setError] = useState('');
  useEffect(() => {
    if (!active) return;
    const controller = new AbortController();
    setBusy(true); setError('');
    void api<Page<T>>(`/api/ledger/${path}?${query}`, { signal: controller.signal }).then(data => {
      if (controller.signal.aborted) return;
      const lastPage = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
      if (Number(new URLSearchParams(query).get('page')) > lastPage) {
        setFilters(current => ({ ...current, page: String(lastPage) }));
        return;
      }
      setResult({ query, data });
    }).catch(error => { if (!controller.signal.aborted) setError(errorMessage(error)); })
      .finally(() => { if (!controller.signal.aborted) setBusy(false); });
    return () => controller.abort();
  }, [path, query, active, refresh]);
  return {
    filters, busy, error, data: result.query === query ? result.data : blank,
    change: (key: keyof typeof filters, value: string) => setFilters(current => ({ ...current, [key]: value, page: key === 'page' ? value : '1' })),
  };
}

async function linkedRecords<T>(path: 'operations' | 'lots', ids: string[], signal?: AbortSignal): Promise<T[]> {
  const distinct = [...new Set(ids.filter(Boolean))], batches: Promise<Page<T>>[] = [];
  for (let start = 0; start < distinct.length; start += 100) {
    const query = new URLSearchParams({ ids: distinct.slice(start, start + 100).join(','), page: '1', pageSize: '100' });
    batches.push(api<Page<T>>(`/api/ledger/${path}?${query}`, { signal }));
  }
  return (await Promise.all(batches)).flatMap(page => page.items);
}

function operationLabel(operation: Operation, look: Lookup) {
  return `${look.owners.find(owner => owner.id === operation.ownerId)?.name || 'Owner name unavailable in current scope'} — ${operation.kind}: ${operation.outputs.map(row => row.code).join(', ') || 'No recorded outputs'} — ${operation.occurredAt} — ${operation.status}`;
}

export function LedgerOperations({ user, active = true }: { user: User; active?: boolean }) {
  const [look, setLook] = useState<Lookup>(emptyLookup), [lots, setLots] = useState<Lot[]>([]), [linkedOperations, setLinkedOperations] = useState<Operation[]>([]);
  const [editing, setEditing] = useState<Operation | null>(null), [replacement, setReplacement] = useState(false), [editorKey, setEditorKey] = useState(0), [refresh, setRefresh] = useState(0);
  const [savedLotCodes, setSavedLotCodes] = useState<string[]>([]);
  const [list, setList] = useState<'drafts' | 'committed'>('drafts'), [committedStatus, setCommittedStatus] = useState('COMMITTED'), [task, setTask] = useState<'processing' | 'transfers' | 'corrections'>('processing');
  const action = useAction(), write = user.permissions.includes('conversions.write');
  const operations = usePagedList<Operation>('operations', { status: list === 'drafts' ? 'DRAFT' : committedStatus, ...(list === 'committed' ? { queueOnly: '' } : {}) }, active && task === 'processing', refresh);
  const corrections = usePagedList<Row>('corrections', {}, active && task === 'corrections', refresh, 'requestedAt');
  useEffect(() => {
    if (!active) return;
    const controller = new AbortController();
    void api<Lookup>('/api/ledger/lookups', { signal: controller.signal }).then(lookup => {
      if (!controller.signal.aborted) setLook(lookup);
    }).catch(error => { if (!controller.signal.aborted) void action.run(async () => { throw error; }); });
    return () => controller.abort();
  }, [active]);
  useEffect(() => {
    if (!active) return;
    const controller = new AbortController();
    void linkedRecords<Operation>('operations', corrections.data.items.flatMap(row => [row.operationId, row.replacementOperationId]), controller.signal).then(rows => {
      if (!controller.signal.aborted) setLinkedOperations(rows);
    }).catch(error => { if (!controller.signal.aborted) void action.run(async () => { throw error; }); });
    return () => controller.abort();
  }, [active, corrections.data, refresh]);
  useEffect(() => {
    if (!active) return;
    const controller = new AbortController();
    void linkedRecords<Lot>('lots', [...operations.data.items, ...linkedOperations].flatMap(operation => operation.inputs.map(row => row.lotId)), controller.signal).then(rows => {
      if (!controller.signal.aborted) setLots(rows);
    }).catch(error => { if (!controller.signal.aborted) void action.run(async () => { throw error; }); });
    return () => controller.abort();
  }, [active, operations.data, linkedOperations, refresh]);
  const editorRef = useRef<HTMLDetailsElement>(null);
  useEffect(() => { if (editing || replacement) { editorRef.current?.scrollIntoView?.({ block: 'center' }); editorRef.current?.querySelector('summary')?.focus(); } }, [editorKey]);
  async function load() { setRefresh(value => value + 1); }
  async function command(path: string, body: Row, method = 'POST') {
    const result = await mutate<Row>('/api/ledger/' + path, body, method);
    await load();
    action.setStatus('Ledger change saved. Updated operations and correction outcomes appear below.');
    return result;
  }
  function edit(operation: Operation | null, copy = false) { setEditing(operation); setReplacement(copy); setEditorKey(key => key + 1); setTask('processing'); }
  const operationListLabel = list === 'drafts' ? 'processing drafts' : 'committed processing';
  return <>
    <Feedback {...action} />
    <nav className="ledger-task-nav" aria-label="Operations tasks">
      {([['processing', 'Processing'], ['transfers', 'Transfers'], ['corrections', 'Corrections']] as const).filter(([key]) => key !== 'transfers' || write).map(([key, label]) => <button key={key} aria-pressed={task === key} onClick={() => setTask(key)}>{label}</button>)}
    </nav>
    <section hidden={task !== 'processing'}>
      <div className="ledger-task-intro"><h2>Processing</h2><p>Draft, confirm variance, manager review, then commit. Declared loss and signed measurement variance are separate. Server checks exact arithmetic, stages and policy before commit. Arrival confirmation is handled in Receive stock.</p></div>
      {savedLotCodes.length > 0 && <div className="notice" role="status">Processing draft saved. Output lot {savedLotCodes.length === 1 ? 'code' : 'codes'} assigned by the server: {savedLotCodes.join(', ')}.</div>}
      <button disabled={operations.busy} onClick={() => void load()}>Refresh operations</button>
      <nav className="ledger-task-nav" aria-label="Processing lists">
        <button aria-pressed={list === 'drafts'} onClick={() => { setList('drafts'); operations.change('page', '1'); }}>Draft queue</button>
        <button aria-pressed={list === 'committed'} onClick={() => { setList('committed'); operations.change('page', '1'); }}>Committed list</button>
      </nav>
      <Panel title={list === 'drafts' ? 'Processing draft queue' : 'Committed processing'}>
        <Feedback error={operations.error} />
        <ListControls label={operationListLabel} search={operations.filters.search} onSearch={value => operations.change('search', value)} sortBy={operations.filters.sortBy} onSortBy={value => operations.change('sortBy', value)} sortDirection={operations.filters.sortDirection as 'asc' | 'desc'} onSortDirection={value => operations.change('sortDirection', value)} sortOptions={operationSortOptions}>
          <label>Processing kind filter<Enum values={kinds} value={operations.filters.kind} onChange={value => operations.change('kind', value)} optional /></label>
          <label>Processing owner filter<Choice rows={look.owners} value={operations.filters.ownerId} onChange={value => operations.change('ownerId', value)} optional /></label>
          <label>Processing facility filter<Choice rows={look.facilities} value={operations.filters.facilityId} onChange={value => operations.change('facilityId', value)} optional /></label>
          {list === 'drafts' ? <label>Draft queue filter<select value={operations.filters.queueOnly} onChange={event => operations.change('queueOnly', event.target.value)}><option value="">All drafts</option><option value="true">Needs variance confirmation</option></select></label> : <label>Committed status filter<Enum values={['COMMITTED', 'CORRECTED']} value={committedStatus} onChange={value => { setCommittedStatus(value); operations.change('page', '1'); }} /></label>}
        </ListControls>
        {operations.data.items.map(operation => <OperationDetail key={operation.id} operation={operation} lots={lots} look={look} user={user} command={command} onEdit={() => edit(operation)} />)}
        {!operations.busy && !operations.data.items.length && <p className="empty">{list === 'drafts' ? 'No processing drafts match these filters.' : 'No committed operations match these filters.'}</p>}
        <Pagination label={operationListLabel} page={Number(operations.filters.page)} pageSize={operations.data.pageSize} totalCount={operations.data.totalCount} onChange={value => operations.change('page', String(value))} busy={operations.busy} />
      </Panel>
      {write && <details ref={editorRef} className="ledger-disclosure" open={!!editing || replacement}><summary>{replacement ? 'Correction replacement draft' : editing ? 'Edit processing draft' : 'Create processing draft'}</summary><OperationEditor key={editorKey} look={look} lots={lots} operation={editing} replacement={replacement} command={command} onSaved={saved => { setSavedLotCodes((saved.outputs || []).map((row: Row) => row.code).filter(Boolean)); edit(null); }} onClose={() => edit(null)} /></details>}
    </section>
    {write && <section hidden={task !== 'transfers'}><div className="ledger-task-intro"><h2>Transfers</h2><p>Move unchanged lot stock between eligible locations.</p></div><TransferForm look={look} onChanged={load} /></section>}
    <section hidden={task !== 'corrections'}>
      <div className="ledger-task-intro"><h2>Corrections</h2><p>Request blocks original outputs immediately. Compensation and replacement commit together. No automatic downstream reversal. Independent HOLD or receiving blocks remain effective.</p></div>
      {write && <CorrectionRequest look={look} command={command} active={active && task === 'corrections'} refresh={refresh} />}
      <Panel title="Processing corrections">
        <Feedback error={corrections.error} />
        <ListControls label="processing corrections" search={corrections.filters.search} onSearch={value => corrections.change('search', value)} sortBy={corrections.filters.sortBy} onSortBy={value => corrections.change('sortBy', value)} sortDirection={corrections.filters.sortDirection as 'asc' | 'desc'} onSortDirection={value => corrections.change('sortDirection', value)} sortOptions={correctionSortOptions}>
          <label>Correction status filter<Enum values={['REQUESTED', 'RECONCILED', 'COMMITTED', 'CANCELLED']} value={corrections.filters.status} onChange={value => corrections.change('status', value)} optional /></label>
          <label>Correction owner filter<Choice rows={look.owners} value={corrections.filters.ownerId} onChange={value => corrections.change('ownerId', value)} optional /></label>
          <label>Correction facility filter<Choice rows={look.facilities} value={corrections.filters.facilityId} onChange={value => corrections.change('facilityId', value)} optional /></label>
        </ListControls>
        {corrections.data.items.map(correction => <ProcessingReview key={correction.id} correction={correction} original={linkedOperations.find(operation => operation.id === correction.operationId)} bound={linkedOperations.find(operation => operation.id === correction.replacementOperationId)} lots={lots} look={look} user={user} command={command} onReplacement={original => edit(original, true)} active={active && task === 'corrections'} refresh={refresh} />)}
        {!corrections.busy && !corrections.data.items.length && <p className="empty">No processing corrections match these filters.</p>}
        <Pagination label="processing corrections" page={Number(corrections.filters.page)} pageSize={corrections.data.pageSize} totalCount={corrections.data.totalCount} onChange={value => corrections.change('page', String(value))} busy={corrections.busy} />
      </Panel>
    </section>
  </>;
}
function OperationEditor({ look, lots, operation, replacement, command, onSaved, onClose }: { look: Lookup; lots: Lot[]; operation: Operation | null; replacement: boolean; command: (path: string, body: Row, method?: string) => Promise<Row>; onSaved: (saved: Row) => void; onClose: () => void }) {
  const action = useAction(), [operationId] = useState(replacement || !operation ? id() : operation.id), [owner, setOwner] = useState(operation?.ownerId || ''), [facility, setFacility] = useState(operation?.facilityId || ''), [kind, setKind] = useState(operation?.kind || 'DRYING'), [equipment, setEquipment] = useState(operation?.equipmentId || ''), [when, setWhen] = useState(operation ? localTime(operation.occurredAt) : now()), [loss, setLoss] = useState(operation?.declaredLossKg || '0');
  const [inputs, setInputs] = useState<Row[]>(operation ? operation.inputs.map(row => ({ ...row })) : [{ lotId: '', locationId: '', quantityKg: '' }]);
  const [outputs, setOutputs] = useState<Row[]>(operation ? operation.outputs.map(row => ({ ...row, id: replacement ? id() : row.id, code: replacement ? '' : row.code, originalLotId: replacement ? row.id : row.originalLotId, productIdentity: row.productIdentity || row.stage })) : [{ id: id(), code: '', commodityId: '', stage: 'DRIED_PADDY', productIdentity: 'DRIED_PADDY', locationId: '', quantityKg: '' }]);
  const [selectedLots, setSelectedLots] = useState<Lot[]>(() => lots.filter(lot => operation?.inputs.some(input => input.lotId === lot.id)));
  const [pinsReady, setPinsReady] = useState(!operation);
  useEffect(() => {
    if (!operation) return;
    const controller = new AbortController();
    void linkedRecords<Lot>('lots', operation.inputs.map(row => row.lotId), controller.signal).then(rows => {
      if (controller.signal.aborted) return;
      if (operation.inputs.some(input => !rows.some(lot => lot.id === input.lotId)))
        throw Error('Some input lots are unavailable in current scope. Processing requires every selected input.');
      setSelectedLots(rows);
      setPinsReady(true);
    }).catch(error => { if (!controller.signal.aborted) void action.run(async () => { throw error; }); });
    return () => controller.abort();
  }, [operation?.id]);
  const firstLot = selectedLots.find(lot => lot.id === inputs[0]?.lotId);
  function selectedLot(lot: Lot | undefined) {
    if (lot) setSelectedLots(rows => rows.find(row => row.id === lot.id) === lot ? rows : [...rows.filter(row => row.id !== lot.id), lot]);
  }
  const fractions = ['HEAD_RICE', 'BROKEN_RICE', 'BRAN', 'HUSK'];
  function outputRules(row: Row, sourceLot = firstLot) {
    const inherited = sourceLot?.productIdentity || sourceLot?.stage || '';
    const stage = kind === 'DRYING' ? 'DRIED_PADDY' : kind === 'MILLING' ? (fractions.includes(row.stage) ? row.stage : 'HEAD_RICE') : kind === 'BLENDING' ? 'BLENDED_RICE' : kind === 'BAGGING' ? 'BAGGED_RICE' : kind === 'MERGE' ? sourceLot?.stage || '' : '';
    return { ...row, stage, productIdentity: kind === 'BLENDING' ? 'BLENDED_RICE' : kind === 'BAGGING' || kind === 'MERGE' ? inherited : stage, commodityId: kind === 'BLENDING' ? row.commodityId || '' : sourceLot?.commodityId || '' };
  }
  function updateDerivedOutput(row: Row, sourceLot: Lot) {
    const next = outputRules(row, sourceLot);
    return (row.code || row.originalLotId) && (next.stage !== row.stage || next.productIdentity !== row.productIdentity)
      ? { ...next, id: id(), code: '', originalLotId: undefined } : next;
  }
  function changeKind(value: string) {
    setKind(value);
    setOutputs(rows => rows.map(row => {
      const stage = value === 'DRYING' ? 'DRIED_PADDY' : value === 'MILLING'
        ? (fractions.includes(row.stage) ? row.stage : 'HEAD_RICE') : value === 'BLENDING'
          ? 'BLENDED_RICE' : value === 'BAGGING' ? 'BAGGED_RICE' : value === 'MERGE'
            ? firstLot?.stage || row.stage : row.stage;
      const productIdentity = value === 'BAGGING' || value === 'MERGE'
        ? firstLot?.productIdentity || firstLot?.stage || '' : stage;
      const changed = stage !== row.stage || productIdentity !== row.productIdentity;
      return changed && (row.code || row.originalLotId)
        ? { ...row, stage, productIdentity, id: id(), code: '', originalLotId: undefined }
        : { ...row, stage, productIdentity };
    }));
  }
  const owners = look.owners;
  function updateInput(index: number, patch: Row) {
    setInputs(rows => rows.map((row, i) => i === index ? { ...row, ...patch } : row));
  }
  function updateOutput(index: number, patch: Row) { setOutputs(rows => rows.map((row, i) => {
    if (i !== index) return row;
    const next = outputRules({ ...row, ...patch });
    return (row.code || row.originalLotId) && (next.stage !== row.stage || next.productIdentity !== row.productIdentity)
      ? { ...next, id: id(), code: '', originalLotId: undefined } : next;
  })); }
  async function save(event: FormEvent) { event.preventDefault(); await action.run(async () => {
    const saved = await command(operation && !replacement ? `operations/${operation.id}` : 'operations', {
      id: operationId, ownerId: owner, facilityId: facility, kind, equipmentId: equipment, occurredAt: iso(when), inputs,
      outputs: outputs.map(row => { const output = outputRules(row); return { ...output, productIdentity: output.productIdentity || null }; }),
      declaredLossKg: loss, ...(operation && !replacement ? { version: operation.version } : {}),
    }, operation && !replacement ? 'PUT' : 'POST'); onSaved(saved);
  }, 'Processing draft saved.'); }
  return <Panel title={replacement ? 'Prepare correction replacement draft' : operation ? 'Edit processing draft' : 'New processing draft'}><Feedback {...action} />{replacement && <p>Inputs copied from original consumed stock. Commit only through correction package after manager reconciles actual original output positions and this replacement version. Set replacement output locations to verified physical locations; original locations cannot silently override transfers.</p>}<form onSubmit={save}>
    <label>Stock owner<Choice rows={operation ? owners.filter(row => row.id === operation.ownerId) : owners} value={owner} onChange={value => { setOwner(value); setSelectedLots([]); setInputs([{ lotId: '', locationId: '', quantityKg: '' }]); }} /></label>
    <label>Processing facility<Choice rows={look.facilities.filter(row => !operation || row.id === operation.facilityId)} value={facility} onChange={value => { setFacility(value); setSelectedLots([]); setEquipment(''); setInputs([{ lotId: '', locationId: '', quantityKg: '' }]); setOutputs(rows => rows.map(row => ({ ...row, locationId: '' }))); }} /></label>
    <label>Processing kind<Enum values={kinds} value={kind} onChange={changeKind} /></label><label>Equipment<Choice rows={look.equipment.filter(row => row.facilityId === facility)} value={equipment} onChange={setEquipment} /></label><label>Operation occurred at<input required type="datetime-local" value={when} onChange={e => setWhen(e.target.value)} /></label>
    <h3>Input allocations</h3>
    {inputs.map((input, index) => {
      const lot = selectedLots.find(lot => lot.id === input.lotId);
      return <fieldset key={index}>
        <legend>Input {index + 1}</legend>
        <LedgerLotPicker label="Input lot" value={input.lotId} onChange={value => updateInput(index, { lotId: value, locationId: '' })} onSelected={lot => { selectedLot(lot); if (index === 0 && lot && lot.id !== firstLot?.id) setOutputs(rows => rows.map(row => updateDerivedOutput(row, lot))); }} look={look} ownerId={owner} facilityId={facility} disabled={!owner || !facility || !pinsReady} pinnedLots={selectedLots} />
        <label>Input stock position<Choice rows={(lot?.positions || []).filter(position => position.facilityId === facility).map(position => ({ id: position.locationId, name: `${name(look.locations, position.locationId)} — physical ${position.quantityKg} kg; available ${position.availableKg} kg` }))} value={input.locationId} onChange={value => updateInput(index, { locationId: value })} /></label>
        <Mass label="Allocated input (kg)" value={input.quantityKg} onChange={value => updateInput(index, { quantityKg: value })} />
        <button type="button" disabled={inputs.length === 1} onClick={() => setInputs(rows => rows.filter((_, i) => i !== index))}>Remove input</button>
      </fieldset>;
    })}
    <button type="button" onClick={() => setInputs(rows => [...rows, { lotId: '', locationId: '', quantityKg: '' }])}>Add input allocation</button>
    <h3>Actual outputs</h3>
    <p>Milling records every actual positive fraction. Never fabricate missing fractions or hide bran/husk as loss. Bagging retains underlying product identity.</p>
    {outputs.map((output, index) => <fieldset key={output.id}>
      <legend>Output {index + 1}</legend>
      <p>Output lot code: {output.code || 'Assigned when draft is saved'}</p>
      {kind === 'BLENDING' ? <label>Output commodity<Choice rows={look.commodities} value={output.commodityId} onChange={value => updateOutput(index, { commodityId: value })} /></label> : <label>Output commodity<input readOnly value={name(look.commodities, firstLot?.commodityId || '')} aria-label="Output commodity (derived from first input)" /></label>}
      {kind === 'MILLING' ? <label>Produced fraction<Enum values={fractions} value={output.stage} onChange={value => updateOutput(index, { stage: value })} /></label> : <p>Output stage: {outputRules(output).stage}</p>}
      {kind === 'BAGGING' || kind === 'MERGE' ? <p>Underlying product identity: {outputRules(output).productIdentity || 'Set first input lot to derive'}</p> : null}
      <label>Output physical location<Choice rows={look.locations.filter(row => replacement || row.facilityId === facility)} value={output.locationId} onChange={value => updateOutput(index, { locationId: value })} /></label>
      <Mass label="Actual output (kg)" value={output.quantityKg} onChange={value => updateOutput(index, { quantityKg: value })} />
      <button type="button" disabled={outputs.length === 1} onClick={() => setOutputs(rows => rows.filter((_, i) => i !== index))}>Remove output</button>
    </fieldset>)}
    <button type="button" onClick={() => setOutputs(rows => [...rows, outputRules({ id: id(), code: '', commodityId: '', stage: kind === 'BLENDING' ? '' : outputs[0]?.stage || 'DRIED_PADDY', productIdentity: outputs[0]?.productIdentity || 'DRIED_PADDY', locationId: '', quantityKg: '' })])}>Add actual output</button>
    <Mass label="Declared loss (kg, nonnegative)" value={loss} onChange={setLoss} />
    <div className="ledger-actions"><button disabled={action.busy || !pinsReady || inputs.some(input => !selectedLots.some(lot => lot.id === input.lotId))}>{operation && !replacement ? 'Save draft changes' : 'Save processing draft'}</button>{operation && <button type="button" onClick={onClose}>Close processing editor</button>}</div>
  </form></Panel>;
}
function OperationDetail({ operation, lots, look, user, command, onEdit }: { operation: Operation; lots: Lot[]; look: Lookup; user: User; command: (path: string, body: Row, method?: string) => Promise<Row>; onEdit: () => void }) {
  const outputSummary = operation.outputs.map(row => `${row.quantityKg} kg ${name(look.commodities, row.commodityId)} (${row.stage}) — ${row.code}`).join('; ');
  const inputSummary = operation.inputs.map(row => `${row.quantityKg} kg — ${lots.find(lot => lot.id === row.lotId)?.code || name(look.locations, row.locationId)}`).join('; ');
  const action = useAction(), [evidence, setEvidence] = useState(''), [reason, setReason] = useState('');
  const [visited, setVisited] = useState(false);
  return <details onToggle={event => { if (event.target === event.currentTarget && event.currentTarget.open) setVisited(true); }}>
    <summary onClick={() => setVisited(true)}>{operationLabel(operation, look)}</summary>
    {visited && <><Feedback {...action} />
    <dl>
      <dt>Owner</dt><dd>{look.owners.find(row => row.id === operation.ownerId)?.name || 'Owner name unavailable in current scope'}</dd>
      <dt>Facility</dt><dd>{name(look.facilities, operation.facilityId)}</dd>
      <dt>Occurred at</dt><dd>{operation.occurredAt}</dd>
      <dt>Status</dt><dd>{operation.status}</dd>
      <dt>Input quantities</dt><dd>{inputSummary || 'No recorded inputs'}</dd>
      <dt>Output quantities</dt><dd>{outputSummary || 'No recorded outputs'}</dd>
      <dt>Declared loss (kg)</dt><dd>{operation.declaredLossKg}</dd>
      <dt>Signed measurement variance (kg)</dt><dd>{operation.varianceKg ?? 'Preview unavailable; commit requires server review.'}</dd>
      <dt>Allowed variance (kg)</dt><dd>{operation.allowedVarianceKg ?? 'No applicable confirmed policy; processing commit blocked.'}</dd>
      <dt>Manager confirmation</dt><dd>{operation.confirmedVersion === operation.version ? 'Current version confirmed' : operation.varianceKg != null && /^[-+]?0+(?:\.0+)?$/.test(operation.varianceKg) ? 'Not required (zero variance)' : 'Unconfirmed or stale'}</dd>
    </dl>
    <details><summary>Technical record (IDs and full payload)</summary><Table rows={operation.inputs} /><Table rows={operation.outputs} /><dl><dt>Operation ID</dt><dd>{operation.id}</dd><dt>Version</dt><dd>{operation.version}</dd></dl></details>
    {operation.status === 'DRAFT' && <>
      {user.permissions.includes('conversions.confirm') && <form onSubmit={event => {
        event.preventDefault();
        void action.run(() => command(`operations/${operation.id}/confirm`, { version: operation.version, evidenceId: evidence, reason }), 'Reviewed version confirmed by authenticated manager.');
      }}>
        <h3>Manager review, when required</h3>
        <Evidence ownerId={operation.ownerId} facilityId={operation.facilityId} value={evidence} onChange={setEvidence} />
        <label>Variance review reason<textarea required value={reason} onChange={event => setReason(event.target.value)} /></label>
        <button disabled={action.busy || !evidence || !reason.trim()}>Confirm reviewed operation version</button>
      </form>}
      {user.permissions.includes('conversions.write') && <div className="ledger-actions">
        {operation.canEdit && <button disabled={action.busy} onClick={onEdit}>Edit processing draft</button>}
        {operation.canDelete && <button disabled={action.busy} onClick={() => {
          if (window.confirm('Remove draft from active queue? Stored record retained.'))
            void action.run(() => command(`operations/${operation.id}`, { version: operation.version }, 'DELETE'), 'Processing draft removed from active queue. Stored record retained.');
        }}>Delete processing draft</button>}
        <button disabled={action.busy || !!operation.boundCorrectionId} onClick={() => void action.run(() => command(`operations/${operation.id}/commit`, {}), 'Operation committed; stock and lineage posted atomically.')}>Commit processing</button>
        {operation.boundCorrectionId && <p>Bound replacement: use correction package commit.</p>}
      </div>}
    </>}
    </>}
  </details>;
}
function OperationPicker({ label, look, status, value, onChange, onSelected, ownerId, facilityId, active, refresh, pinned }: {
  label: string; look: Lookup; status: string; value: string; onChange: (value: string) => void;
  onSelected?: (operation: Operation | undefined) => void; ownerId?: string; facilityId?: string;
  active: boolean; refresh: number; pinned?: Operation;
}) {
  const choices = usePagedList<Operation>('operations', { status, ...(ownerId ? { ownerId } : {}), ...(facilityId ? { facilityId } : {}) }, active, refresh);
  const [pin, setPin] = useState<Operation | undefined>(pinned), [pinError, setPinError] = useState('');
  const callback = useRef(onSelected); callback.current = onSelected;
  useEffect(() => {
    if (!active || !value) { if (!value) setPin(undefined); return; }
    const controller = new AbortController();
    const query = new URLSearchParams({ ids: value, page: '1', pageSize: '1', status, ...(ownerId ? { ownerId } : {}), ...(facilityId ? { facilityId } : {}) });
    setPinError('');
    void api<Page<Operation>>(`/api/ledger/operations?${query}`, { signal: controller.signal }).then(page => {
      if (!controller.signal.aborted) setPin(page.items[0]);
    }).catch(error => { if (!controller.signal.aborted) setPinError(errorMessage(error)); });
    return () => controller.abort();
  }, [value, status, ownerId, facilityId, active, refresh]);
  const selected = choices.data.items.find(operation => operation.id === value) || (pin?.id === value ? pin : undefined);
  useEffect(() => { callback.current?.(selected); }, [selected]);
  const rows = selected && !choices.data.items.some(operation => operation.id === selected.id) ? [selected, ...choices.data.items] : choices.data.items;
  return <div className="ledger-lot-picker">
    <Feedback error={choices.error || pinError} />
    <ListControls label={label} search={choices.filters.search} onSearch={value => choices.change('search', value)} sortBy={choices.filters.sortBy} onSortBy={value => choices.change('sortBy', value)} sortDirection={choices.filters.sortDirection as 'asc' | 'desc'} onSortDirection={value => choices.change('sortDirection', value)} sortOptions={operationSortOptions}>
      <label>{label} kind filter<Enum values={kinds} value={choices.filters.kind} onChange={value => choices.change('kind', value)} optional /></label>
      {!ownerId && <label>{label} owner filter<Choice rows={look.owners} value={choices.filters.ownerId} onChange={value => choices.change('ownerId', value)} optional /></label>}
      {!facilityId && <label>{label} facility filter<Choice rows={look.facilities} value={choices.filters.facilityId} onChange={value => choices.change('facilityId', value)} optional /></label>}
    </ListControls>
    <label>{label}<Choice rows={rows.map(operation => ({ id: operation.id, name: `${operationLabel(operation, look)} — version ${operation.version}` }))} value={value} onChange={value => {
      setPin(rows.find(operation => operation.id === value));
      onChange(value);
    }} /></label>
    <Pagination label={label} page={Number(choices.filters.page)} pageSize={choices.data.pageSize} totalCount={choices.data.totalCount} onChange={value => choices.change('page', String(value))} busy={choices.busy} />
  </div>;
}
function CorrectionRequest({ look, command, active, refresh }: { look: Lookup; command: (path: string, body: Row) => Promise<Row>; active: boolean; refresh: number }) {
  const [operationId, setOperationId] = useState(''), [reason, setReason] = useState(''), [expanded, setExpanded] = useState(false), action = useAction();
  return <details onToggle={event => { if (event.target === event.currentTarget) setExpanded(event.currentTarget.open); }}>
    <summary>Request processing correction</summary>
    <form onSubmit={event => {
      event.preventDefault();
      void action.run(() => command('corrections', { id: id(), operationId, reason }), 'Correction requested; original outputs blocked.');
    }}>
      <Feedback {...action} />
      <OperationPicker label="Committed operation to correct" look={look} status="COMMITTED" value={operationId} onChange={setOperationId} active={active && expanded} refresh={refresh} />
      <label>Processing correction reason<textarea required value={reason} onChange={event => setReason(event.target.value)} /></label>
      <button disabled={action.busy || !operationId || !reason.trim()}>Request processing correction</button>
    </form>
  </details>;
}
function ProcessingReview({ correction, original, bound, lots, look, user, command, onReplacement, active, refresh }: {
  correction: Row; original?: Operation; bound?: Operation; lots: Lot[]; look: Lookup; user: User;
  command: (path: string, body: Row) => Promise<Row>; onReplacement: (original: Operation) => void; active: boolean; refresh: number;
}) {
  const action = useAction(), [positions, setPositions] = useState<Row[]>([]), [physicalLoaded, setPhysicalLoaded] = useState(false);
  const [replacementId, setReplacementId] = useState(correction.replacementOperationId || ''), [replacement, setReplacement] = useState<Operation | undefined>(bound), [replacementLots, setReplacementLots] = useState<Lot[]>([]);
  const [evidence, setEvidence] = useState(''), [reason, setReason] = useState(''), [verified, setVerified] = useState(false), [originalCorrect, setOriginalCorrect] = useState(false), [expanded, setExpanded] = useState(false);
  const manager = user.permissions.includes('corrections.reconcile'), write = user.permissions.includes('conversions.write'), open = ['REQUESTED', 'RECONCILED'].includes(correction.status);
  useEffect(() => { setVerified(false); }, [replacement?.id, replacement?.version]);
  useEffect(() => {
    if (!replacement || !active || !expanded) return;
    const controller = new AbortController();
    void linkedRecords<Lot>('lots', replacement.inputs.map(row => row.lotId), controller.signal).then(rows => {
      if (!controller.signal.aborted) setReplacementLots(rows);
    }).catch(error => { if (!controller.signal.aborted) void action.run(async () => { throw error; }); });
    return () => controller.abort();
  }, [replacement, active, expanded, refresh]);
  async function loadPositions() {
    await action.run(async () => {
      if (!original) throw Error('Original operation not available in current scope.');
      setPhysicalLoaded(false); setVerified(false);
      const ids = [...new Set(original.outputs.map(row => String(row.id)))];
      const outputs = await linkedRecords<Lot>('lots', ids);
      if (outputs.length !== ids.length) throw Error('Some original outputs are unavailable in current scope. Reconciliation requires every output.');
      setPositions(outputs.flatMap(lot => lot.positions.filter(position => positive(position.quantityKg)).map(position => ({ lotId: lot.id, code: lot.code, locationId: position.locationId, quantityKg: position.quantityKg }))));
      setPhysicalLoaded(true);
    });
  }
  async function reconcile(event: FormEvent) {
    event.preventDefault();
    await action.run(async () => {
      if (!replacement) throw Error('Select replacement draft.');
      await command(`corrections/${correction.id}/reconcile`, {
        evidenceId: evidence, reason,
        reconciliation: positions.map(({ lotId, locationId, quantityKg }) => ({ lotId, locationId, quantityKg })),
        replacementOperationId: replacement.id, replacementVersion: replacement.version,
      });
    }, 'Reconciliation bound to reviewed replacement version and physical positions.');
  }
  return <details onToggle={event => { if (event.target === event.currentTarget) setExpanded(event.currentTarget.open); }}>
    <summary>{original ? `${name(look.owners, original.ownerId)} — ${original.kind}: ${original.outputs.map(row => `${row.quantityKg} kg ${row.code}`).join(', ')}` : 'Original operation unavailable in current scope'} — correction {correction.status}</summary>
    <Feedback {...action} />
    <p>{correction.reason}</p>
    {correction.evidenceId && <a href={`/api/ledger/evidence/${correction.evidenceId}`} target="_blank" rel="noreferrer">Immutable reconciliation evidence</a>}
    {correction.reconciliation ? <details><summary>Verified physical reconciliation</summary><Table label="Verified reconciliation" rows={correction.reconciliation} /></details> : <p>Reconciliation evidence restricted or not recorded.</p>}
    {correction.replacementOperationId && <p>Bound replacement: {bound?.outputs.map(row => row.code).join(', ') || 'Replacement unavailable in current scope'}; current version {bound?.version || 'not visible'}.</p>}
    <details><summary>Technical identifiers</summary><dl><dt>Correction ID</dt><dd>{correction.id}</dd><dt>Original operation ID</dt><dd>{correction.operationId}</dd></dl></details>
    {write && open && original && <button onClick={() => onReplacement(original)}>Prepare replacement from original</button>}
    {manager && open && original && <>
      <Evidence ownerId={original.ownerId} facilityId={original.facilityId} value={evidence} onChange={setEvidence} />
      <label>Physical reconciliation / cancellation reason<textarea required value={reason} onChange={event => setReason(event.target.value)} /></label>
      <button disabled={action.busy} onClick={() => void loadPositions()}>Load actual current output positions</button>
      <form onSubmit={reconcile}>
        {positions.map((position, index) => <fieldset key={`${position.lotId}:${position.locationId}`}>
          <legend>{position.code} @ {name(look.locations, position.locationId)}</legend>
          <Mass label="Verified physical position (kg)" value={position.quantityKg} onChange={value => {
            setVerified(false); setPositions(rows => rows.map((row, i) => i === index ? { ...row, quantityKg: value } : row));
          }} />
        </fieldset>)}
        <OperationPicker label="Replacement draft to review" look={look} status="DRAFT" ownerId={original.ownerId} facilityId={original.facilityId} value={replacementId} onChange={value => { setReplacementId(value); setVerified(false); }} onSelected={setReplacement} pinned={bound} active={active && expanded} refresh={refresh} />
        {replacement && <>
          <h3>Replacement inputs</h3>
          <p>{replacement.inputs.map(row => `${row.quantityKg} kg — ${[...replacementLots, ...lots].find(lot => lot.id === row.lotId)?.code || 'Lot name unavailable in current scope'}`).join('; ') || 'No recorded inputs'}</p>
          <h3>Replacement outputs</h3>
          <p>{replacement.outputs.map(row => `${row.quantityKg} kg ${name(look.commodities, row.commodityId)} (${row.stage}) — ${row.code}`).join('; ') || 'No recorded outputs'}</p>
          <p>Declared loss {replacement.declaredLossKg} kg. Reviewed version: {replacement.version}</p>
          <details><summary>Technical replacement payload</summary><Table label="Replacement inputs" rows={replacement.inputs} /><Table label="Replacement outputs" rows={replacement.outputs} /></details>
        </>}
        <label className="checkbox-label"><input type="checkbox" checked={verified} onChange={event => setVerified(event.target.checked)} />I verified every actual physical output position and reviewed replacement content/version.</label>
        <button disabled={action.busy || !physicalLoaded || !positions.length || !verified || !evidence || !reason.trim() || !replacement}>Record bound physical reconciliation</button>
      </form>
      <form onSubmit={event => {
        event.preventDefault();
        void action.run(() => command(`corrections/${correction.id}/cancel`, { evidenceId: evidence, reason }), 'Correction cancelled with verified original-correct evidence; independent restrictions remain.');
      }}>
        <label className="checkbox-label"><input type="checkbox" checked={originalCorrect} onChange={event => setOriginalCorrect(event.target.checked)} />Evidence verifies original record was correct.</label>
        <button disabled={action.busy || !originalCorrect || !evidence || !reason.trim()}>Cancel verified correction request</button>
      </form>
    </>}
    {write && correction.status === 'RECONCILED' && <button disabled={action.busy || !correction.replacementOperationId} onClick={() => void action.run(() => command(`corrections/${correction.id}/commit`, { replacementOperationId: correction.replacementOperationId }), 'Correction compensation and replacement committed atomically.')}>Commit bound correction package</button>}
  </details>;
}
