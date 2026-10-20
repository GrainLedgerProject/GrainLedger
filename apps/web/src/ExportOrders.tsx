import { useEffect, useState, type FormEvent } from 'react';
import { api, apiWithEtag, ApiError, errorMessage } from './api';

type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number; hasMore: boolean };
type Catalog = { id: string; code: string; name: string; unit?: string; quantityKind?: string; isDeleted?: boolean };
type Limit = { attributeId: string; attributeCode?: string; attributeName?: string; unit?: string; minValue: string | null; maxValue: string | null };
type Target = { targetMass: string; unit: string };
type Container = { id: string; orderId: string; internalCode: string; targetMassKg: string; originalMass: string; originalUnit: string; status: string; version: number };
type Summary = { id: string; code: string; marketId: string; commodityId: string; requiredMassKg: string; shippingFrom: string; shippingTo: string; createdAt: string; version: number };
type Order = Summary & { originalMass: string; originalUnit: string; specificationVersion: number; limits: Limit[]; allocatedMassKg: string; remainingMassKg: string; canEdit: boolean };
type Specification = { versionNo: number; recordedAt: string; limits: Limit[] };
type HistoryEvent = { action: string; recordedAt: string; snapshot: string; actorName: string };
type FormState = { marketId: string; commodityId: string; requiredMass: string; unit: string; shippingFrom: string; shippingTo: string; limits: Limit[]; containers: Target[] };
export type ExportListState = { page: number; search: string; query: string; marketId: string; commodityId: string; from: string; to: string; sortBy: string; sortDirection: string };
export const initialExportListState: ExportListState = { page: 1, search: '', query: '', marketId: '', commodityId: '', from: '', to: '', sortBy: 'createdAt', sortDirection: 'desc' };

const empty: FormState = { marketId: '', commodityId: '', requiredMass: '', unit: '', shippingFrom: '', shippingTo: '', limits: [{ attributeId: '', minValue: null, maxValue: null }], containers: [{ targetMass: '', unit: '' }] };
const supportedMassUnits = new Set(['KG', 'G', 'T']);
function useMassUnits() {
  const [units, setUnits] = useState<Catalog[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      const found: Catalog[] = [];
      for (let page = 1; ; page++) {
        const result = await api<Page<Catalog>>(`/api/measurement-units?page=${page}&pageSize=100`, { signal: controller.signal });
        found.push(...result.items.filter(unit => !unit.isDeleted && unit.quantityKind === 'MASS' && supportedMassUnits.has(unit.code)));
        if (!result.hasMore) break;
      }
      if (!controller.signal.aborted) setUnits(found);
    }
    void load().catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, []);
  return { units, error, loading };
}
function MassUnitSelect({ value, onChange, catalog, errors, name }: { value: string; onChange: (value: string) => void; catalog: ReturnType<typeof useMassUnits>; errors: Record<string, string[]>; name: string }) {
  return <label>Unit<select value={value} onChange={event => onChange(event.target.value)} required disabled={catalog.loading || !!catalog.error}>
    <option value="">Select unit</option>
    {value && !catalog.units.some(unit => unit.code === value) && <option value={value} disabled>{value} (unavailable)</option>}
    {catalog.units.map(unit => <option key={unit.id} value={unit.code}>{unit.name} ({unit.code})</option>)}
  </select>{catalog.error && <span className="field-error" role="alert">Units could not load: {catalog.error}</span>}
    {!catalog.loading && !catalog.error && catalog.units.length === 0 && <span className="field-error" role="alert">No supported mass units are configured.</span>}
    <FieldError errors={errors} name={name} /></label>;
}
function historyDescription(row: HistoryEvent) {
  try {
    const data = JSON.parse(row.snapshot) as Record<string, unknown>;
    const pick = (record: Record<string, unknown>, name: string) => record[name] ?? record[name[0].toUpperCase() + name.slice(1)];
    const wrapped = pick(data, 'request');
    const request = (wrapped && typeof wrapped === 'object' ? wrapped : data) as Record<string, unknown>;
    if (row.action === 'ORDER_CREATED') return `Order created for ${pick(request, 'requiredMass')} ${pick(request, 'unit')}; shipping ${pick(request, 'shippingFrom')} to ${pick(request, 'shippingTo')}.`;
    if (row.action === 'ORDER_UPDATED') return `Order changed to ${pick(request, 'requiredMass')} ${pick(request, 'unit')}; shipping ${pick(request, 'shippingFrom')} to ${pick(request, 'shippingTo')}.`;
    if (row.action === 'SPECIFICATION_VERSION_ADDED') return `New specification with ${Array.isArray(pick(request, 'limits')) ? (pick(request, 'limits') as unknown[]).length : '?'} attributes.`;
    if (row.action === 'CONTAINERS_ADDED') return `Added ${Array.isArray(pick(request, 'containers')) ? (pick(request, 'containers') as unknown[]).length : '?'} container targets.`;
    if (row.action === 'CONTAINER_UPDATED') return `Container target changed to ${pick(request, 'targetMass')} ${pick(request, 'unit')}.`;
  } catch { /* Older snapshots can have another shape. */ }
  return row.action.replaceAll('_', ' ');
}
const limitPayload = (rows: Limit[]) => rows.map(({ attributeId, minValue, maxValue }) => ({ attributeId, minValue, maxValue }));
const makeId = () => crypto.randomUUID();
const message = (cause: unknown) => cause instanceof ApiError && cause.status === 412 ? 'This record changed. Reload and try again.' : errorMessage(cause);
// Only a definitive API rejection permits a new request. A response/body failure
// can happen after the server commits, so retain the original operation for replay.
const isUncertain = (cause: unknown) => !(cause instanceof ApiError) || cause.status >= 500 || cause.status === 408;
const uncertain = (cause: unknown) => isUncertain(cause) ? 'The result is uncertain. Retry sends the same request to prevent a duplicate.' : message(cause);
const pendingCreateKey = (userId: string) => `grainledger.export.pending-create.${userId}`;
type PendingCreate = { form: FormState; operationId: string };
type PendingMutation = { route: string; method: 'PATCH' | 'POST'; body: string; etag: string };
function readPendingMutation(key: string): PendingMutation | null {
  try {
    const value = sessionStorage.getItem(key);
    if (!value) return null;
    const parsed = JSON.parse(value) as PendingMutation;
    return parsed.route && parsed.body && parsed.etag && (parsed.method === 'POST' || parsed.method === 'PATCH') ? parsed : null;
  } catch { return null; }
}
function readPendingCreate(userId: string): PendingCreate | null {
  try {
    const value = sessionStorage.getItem(pendingCreateKey(userId));
    if (!value) return null;
    const parsed = JSON.parse(value) as PendingCreate;
    return parsed.form && typeof parsed.operationId === 'string' ? parsed : null;
  } catch { return null; }
}

function FieldError({ errors, name }: { errors: Record<string, string[]>; name: string }) {
  return errors[name] && <span className="field-error" role="alert">{errors[name].join(' ')}</span>;
}
function focusFirstFieldError() {
  requestAnimationFrame(() => {
    const error = document.querySelector('.export-form .field-error, .export-edit-form .field-error');
    const container = error?.closest('label, .export-item, .export-catalog') ?? error?.parentElement;
    const field = container?.querySelector<HTMLElement>('select') ?? container?.querySelector<HTMLElement>('input, textarea');
    field?.focus();
  });
}

function CatalogSelect({ kind, label, value, onChange, required = true }: { kind: string; label: string; value: string; onChange: (id: string, selected?: Catalog) => void; required?: boolean }) {
  const [rows, setRows] = useState<Catalog[]>([]);
  const [error, setError] = useState('');
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      const found: Catalog[] = [];
      for (let page = 1; ; page++) {
        const params = new URLSearchParams({ page: String(page), pageSize: '100' });
        const result = await api<Page<Catalog>>(`/api/${kind}?${params}`, { signal: controller.signal });
        found.push(...result.items);
        if (!result.hasMore) break;
      }
      if (!controller.signal.aborted) setRows(previous => {
        const selected = previous.find(row => row.id === value);
        return selected && !found.some(row => row.id === value) ? [selected, ...found] : found;
      });
    }
    void load().catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); });
    return () => controller.abort();
  }, [kind, value]);
  useEffect(() => {
    if (!value || rows.some(row => row.id === value)) return;
    const controller = new AbortController();
    void api<Catalog>(`/api/${kind}/${value}`, { signal: controller.signal })
      .then(selected => setRows(previous => previous.some(row => row.id === selected.id) ? previous : [selected, ...previous]))
      .catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); });
    return () => controller.abort();
  }, [kind, value, rows]);
  return <div className="export-catalog">
    <label>{label}<select value={value} onChange={event => onChange(event.target.value, rows.find(row => row.id === event.target.value))} required={required}><option value="">Select {label.toLowerCase()}</option>{rows.map(row => <option value={row.id} key={row.id}>{row.code} · {row.name}</option>)}</select></label>
    {error && <span className="field-error" role="alert">{error}</span>}
  </div>;
}

function LimitsEditor({ rows, onChange, errors }: { rows: Limit[]; onChange: (next: Limit[]) => void; errors: Record<string, string[]> }) {
  const update = (index: number, patch: Partial<Limit>) => onChange(rows.map((row, i) => i === index ? { ...row, ...patch } : row));
  return <section className="panel export-section"><h2>Quality specification</h2><p className="muted">Set the buyer's required range for each attribute. These are targets, not measured lot results.</p>
    {rows.map((row, index) => <div className="export-item" key={index}>
      <CatalogSelect kind="quality-attributes" label={`Attribute ${index + 1}`} value={row.attributeId} onChange={(attributeId, selected) => update(index, { attributeId, unit: selected?.unit })} />
      {row.unit && <p className="muted">Values in {row.unit}</p>}
      <div className="form-columns"><label>Minimum (optional)<input inputMode="decimal" value={row.minValue ?? ''} onChange={event => update(index, { minValue: event.target.value || null })} /><FieldError errors={errors} name={`specification.limits[${index}].minValue`} /></label>
        <label>Maximum (optional)<input inputMode="decimal" value={row.maxValue ?? ''} onChange={event => update(index, { maxValue: event.target.value || null })} /><FieldError errors={errors} name={`specification.limits[${index}].maxValue`} /></label></div>
      <FieldError errors={errors} name={`specification.limits[${index}]`} /><FieldError errors={errors} name={`specification.limits[${index}].attributeId`} />
      {rows.length > 1 && <button type="button" className="secondary" onClick={() => onChange(rows.filter((_, i) => i !== index))}>Remove attribute</button>}
    </div>)}<FieldError errors={errors} name="specification.limits" />
    <button type="button" className="secondary" onClick={() => onChange([...rows, { attributeId: '', minValue: null, maxValue: null }])}>+ Add attribute</button>
  </section>;
}

function TargetsEditor({ rows, onChange, errors, massUnits }: { rows: Target[]; onChange: (next: Target[]) => void; errors: Record<string, string[]>; massUnits: ReturnType<typeof useMassUnits> }) {
  const update = (index: number, patch: Partial<Target>) => onChange(rows.map((row, i) => i === index ? { ...row, ...patch } : row));
  return <section className="panel export-section"><h2>Container targets</h2><p className="muted">Plan the amount for each container. The physical container number and seal are recorded during loading.</p>
    {rows.map((row, index) => <div className="export-item" key={index}><strong>Container {index + 1}</strong><div className="form-columns"><label>Target mass<input inputMode="decimal" value={row.targetMass} onChange={event => update(index, { targetMass: event.target.value })} required /><FieldError errors={errors} name={`containers[${index}].targetMass`} /></label>
      <MassUnitSelect value={row.unit} onChange={unit => update(index, { unit })} catalog={massUnits} errors={errors} name={`containers[${index}].unit`} /></div>
      {rows.length > 1 && <button type="button" className="secondary" onClick={() => onChange(rows.filter((_, i) => i !== index))}>Remove container</button>}</div>)}<FieldError errors={errors} name="containers" />
    <button type="button" className="secondary" onClick={() => onChange([...rows, { targetMass: '', unit: '' }])}>+ Add container</button>
  </section>;
}

export function ExportOrders({ canWrite, path, onNavigate, userId = 'current', listState = initialExportListState, onListStateChange = () => {} }: { canWrite: boolean; path: string; onNavigate: (path: string) => void; userId?: string; listState?: ExportListState; onListStateChange?: (state: ExportListState) => void }) {
  const id = path.match(/^\/export\/orders\/([^/]+)\/?$/)?.[1];
  const containerId = path.match(/^\/export\/containers\/([^/]+)\/?$/)?.[1];
  if (containerId) return <ContainerDetail key={containerId} id={containerId} userId={userId} canWrite={canWrite} onNavigate={onNavigate} />;
  if (id === 'new') return canWrite ? <CreateOrder userId={userId} onNavigate={onNavigate} /> : <p className="notice error">You cannot create export orders.</p>;
  if (id) return <OrderDetail key={id} id={id} userId={userId} canWrite={canWrite} onNavigate={onNavigate} />;
  return <OrderList canWrite={canWrite} onNavigate={onNavigate} initialState={listState} onStateChange={onListStateChange} />;
}

function OrderList({ canWrite, onNavigate, initialState, onStateChange }: { canWrite: boolean; onNavigate: (path: string) => void; initialState: ExportListState; onStateChange: (state: ExportListState) => void }) {
  const [rows, setRows] = useState<Page<Summary> | null>(null);
  const [page, setPage] = useState(initialState.page); const [search, setSearch] = useState(initialState.search); const [query, setQuery] = useState(initialState.query);
  const [marketId, setMarketId] = useState(initialState.marketId); const [commodityId, setCommodityId] = useState(initialState.commodityId);
  const [from, setFrom] = useState(initialState.from); const [to, setTo] = useState(initialState.to);
  const [sortBy, setSortBy] = useState(initialState.sortBy); const [sortDirection, setSortDirection] = useState(initialState.sortDirection);
  const [error, setError] = useState(''); const [loading, setLoading] = useState(true);
  useEffect(() => { onStateChange({ page, search, query, marketId, commodityId, from, to, sortBy, sortDirection }); },
    [page, search, query, marketId, commodityId, from, to, sortBy, sortDirection, onStateChange]);
  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError('');
    const params = new URLSearchParams({ page: String(page), pageSize: '20', q: query, marketId, commodityId, from, to, sortBy, sortDirection });
    void api<Page<Summary>>(`/api/export-orders?${params}`, { signal: controller.signal })
      .then(setRows).catch(cause => { if (!controller.signal.aborted) setError(message(cause)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [page, query, marketId, commodityId, from, to, sortBy, sortDirection]);
  return <><div className="page-heading section-toolbar"><div><span className="eyebrow">EXPORT</span><h1>Export orders</h1><p className="muted">Record demand, quality requirements and planned container targets.</p></div>{canWrite && <button onClick={() => onNavigate('/export/orders/new')}>+ Create export order</button>}</div>
    <section className="panel"><form className="export-filters" onSubmit={event => { event.preventDefault(); setPage(1); setQuery(search.trim()); }}><label>Order code<input value={search} onChange={event => setSearch(event.target.value)} placeholder="Search code" /></label><button type="submit">Search</button></form>
      <div className="admin-form-grid"><CatalogSelect kind="markets" label="Market" value={marketId} required={false} onChange={id => { setMarketId(id); setPage(1); }} /><CatalogSelect kind="commodities" label="Commodity" value={commodityId} required={false} onChange={id => { setCommodityId(id); setPage(1); }} /><label>Shipping from<input type="date" value={from} onChange={event => { setFrom(event.target.value); setPage(1); }} /></label><label>Shipping to<input type="date" value={to} onChange={event => { setTo(event.target.value); setPage(1); }} /></label><label>Sort by<select value={sortBy} onChange={event => setSortBy(event.target.value)}><option value="createdAt">Created</option><option value="code">Code</option><option value="shippingFrom">Shipping date</option></select></label><label>Direction<select value={sortDirection} onChange={event => setSortDirection(event.target.value)}><option value="desc">Newest first</option><option value="asc">Oldest first</option></select></label></div>
      {error && <div className="notice error" role="alert">{error}</div>}
      <div className="export-results">{loading ? <p role="status">Loading orders…</p> : !rows?.items.length ? <p className="empty">No export orders found.</p> : <div className="table-scroll"><table><thead><tr><th>Order</th><th>Mass</th><th>Shipping window</th><th /></tr></thead><tbody>{rows.items.map(row => <tr key={row.id}><td>{row.code}</td><td>{row.requiredMassKg} kg</td><td>{row.shippingFrom} – {row.shippingTo}</td><td><button className="text-button" onClick={() => onNavigate(`/export/orders/${row.id}`)}>Open</button></td></tr>)}</tbody></table></div>}</div>
      <div className="form-footer"><span>Page {page}{rows ? ` · ${rows.totalCount} orders` : ''}</span><div><button className="secondary" disabled={page === 1 || loading} onClick={() => setPage(p => p - 1)}>Previous</button> <button className="secondary" disabled={!rows?.hasMore || loading} onClick={() => setPage(p => p + 1)}>Next</button></div></div>
    </section></>;
}

function CreateOrder({ userId, onNavigate }: { userId: string; onNavigate: (path: string) => void }) {
  const massUnits = useMassUnits();
  const [pending, setPending] = useState<PendingCreate | null>(() => readPendingCreate(userId));
  const [form, setForm] = useState<FormState>(() => pending?.form ?? empty);
  const [operationId, setOperationId] = useState(() => pending?.operationId ?? makeId());
  const [errors, setErrors] = useState<Record<string, string[]>>({}); const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  const update = (patch: Partial<FormState>) => { if (pending || busy) return; setForm(current => ({ ...current, ...patch })); setOperationId(makeId()); setErrors({}); setError(''); };
  async function save(event: FormEvent) {
    event.preventDefault(); if (busy) return; setBusy(true); setError(''); setErrors({});
    const request = pending ?? { form, operationId };
    sessionStorage.setItem(pendingCreateKey(userId), JSON.stringify(request));
    try {
      const order = await api<Order>('/api/export-orders', { method: 'POST', body: JSON.stringify({ ...request.form, limits: limitPayload(request.form.limits), operationId: request.operationId }) });
      if (!order || typeof order.id !== 'string' || !order.id)
        throw new Error('The saved order could not be read from the response.');
      sessionStorage.removeItem(pendingCreateKey(userId));
      setPending(null);
      onNavigate(`/export/orders/${order.id}`);
    } catch (cause) {
      if (cause instanceof ApiError) { setErrors(cause.errors ?? {}); if (cause.errors) focusFirstFieldError(); }
      if (isUncertain(cause)) setPending(request);
      else { sessionStorage.removeItem(pendingCreateKey(userId)); setPending(null); }
      setError(uncertain(cause));
    } finally { setBusy(false); }
  }
  return <><button className="text-button" onClick={() => onNavigate('/export/orders')}>← Back to orders</button><div className="page-heading"><span className="eyebrow">EXPORT</span><h1>Create export order</h1><p className="muted">Record the customer's demand. Stock and compliance are assessed when a plan is prepared.</p></div>
    <form onSubmit={save} className="export-form"><fieldset disabled={busy || !!pending} className="export-fieldset"><section className="panel export-section"><h2>Order details</h2><div className="admin-form-grid export-order-fields"><div><CatalogSelect kind="markets" label="Export market" value={form.marketId} onChange={marketId => update({ marketId })} /><FieldError errors={errors} name="marketId" /></div><div><CatalogSelect kind="commodities" label="Commodity" value={form.commodityId} onChange={commodityId => update({ commodityId })} /><FieldError errors={errors} name="commodityId" /></div><label>Required mass<input inputMode="decimal" value={form.requiredMass} onChange={event => update({ requiredMass: event.target.value })} required /><FieldError errors={errors} name="requiredMass" /></label><MassUnitSelect value={form.unit} onChange={unit => update({ unit })} catalog={massUnits} errors={errors} name="unit" /><label>Shipping from<input type="date" value={form.shippingFrom} onChange={event => update({ shippingFrom: event.target.value })} required /><FieldError errors={errors} name="shippingFrom" /></label><label>Shipping to<input type="date" value={form.shippingTo} min={form.shippingFrom} onChange={event => update({ shippingTo: event.target.value })} required /><FieldError errors={errors} name="shippingTo" /></label></div></section>
      <LimitsEditor rows={form.limits} onChange={limits => update({ limits })} errors={errors} /><TargetsEditor rows={form.containers} onChange={containers => update({ containers })} errors={errors} massUnits={massUnits} />
      </fieldset>{(error || pending) && <div className="notice error" role="alert">{error || 'A previous submission has an uncertain result. Retry the saved request before editing.'}</div>}<div className="form-footer"><span className="muted">Codes are assigned when the order is saved.</span><button disabled={busy}>{busy ? 'Saving…' : pending ? 'Retry same request' : 'Create export order'}</button></div>
    </form></>;
}

function OrderDetail({ id, userId, canWrite, onNavigate }: { id: string; userId: string; canWrite: boolean; onNavigate: (path: string) => void }) {
  const massUnits = useMassUnits();
  const [order, setOrder] = useState<Order | null>(null); const [etag, setEtag] = useState<string | null>(null);
  const [reload, setReload] = useState(0); const [loading, setLoading] = useState(true);
  const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  const [edit, setEdit] = useState<'header' | 'spec' | 'containers' | null>(null);
  const [header, setHeader] = useState<FormState>(empty); const [limits, setLimits] = useState<Limit[]>([{ attributeId: '', minValue: null, maxValue: null }]);
  const [targets, setTargets] = useState<Target[]>([{ targetMass: '', unit: '' }]);
  const [operationId, setOperationId] = useState(makeId); const [errors, setErrors] = useState<Record<string, string[]>>({});
  const mutationKey = `grainledger.export.pending-order.${userId}.${id}`;
  const [pendingMutation, setPendingMutation] = useState<PendingMutation | null>(() => readPendingMutation(mutationKey));
  const [history, setHistory] = useState<Page<HistoryEvent> | null>(null);
  const [historyPage, setHistoryPage] = useState(1);
  const [containers, setContainers] = useState<Page<Container> | null>(null);
  const [containerPage, setContainerPage] = useState(1);
  const [specifications, setSpecifications] = useState<Page<Specification> | null>(null);
  const [specificationPage, setSpecificationPage] = useState(1);
  const [selectedVersion, setSelectedVersion] = useState<number | null>(null);
  const [market, setMarket] = useState<Catalog | null>(null);
  const [commodity, setCommodity] = useState<Catalog | null>(null);
  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError('');
    void apiWithEtag<Order>(`/api/export-orders/${id}`, { signal: controller.signal }).then(result => {
       setOrder(result.data); setEtag(result.etag);
       if (specificationPage === 1) setSelectedVersion(result.data.specificationVersion);
      setHeader({ marketId: result.data.marketId, commodityId: result.data.commodityId,
        requiredMass: result.data.originalMass, unit: result.data.originalUnit,
        shippingFrom: result.data.shippingFrom, shippingTo: result.data.shippingTo, limits: [], containers: [] });
      setLimits(result.data.limits.map(x => ({ ...x })));
    }).catch(cause => { if (!controller.signal.aborted) setError(message(cause)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [id, reload]);
  useEffect(() => {
    const controller = new AbortController();
    setSpecifications(null);
    void api<Page<Specification>>(`/api/export-orders/${id}/specifications?page=${specificationPage}&pageSize=20`, { signal: controller.signal })
      .then(result => { setSpecifications(result); setSelectedVersion(current => result.items.some(spec => spec.versionNo === current) ? current : result.items[0]?.versionNo ?? null); })
      .catch(cause => { if (!controller.signal.aborted) setError(message(cause)); });
    return () => controller.abort();
  }, [id, reload, specificationPage]);
  useEffect(() => {
    const controller = new AbortController(); setContainers(null);
    void api<Page<Container>>(`/api/export-orders/${id}/containers?page=${containerPage}&pageSize=20`, { signal: controller.signal })
      .then(setContainers).catch(cause => { if (!controller.signal.aborted) setError(message(cause)); });
    return () => controller.abort();
  }, [id, reload, containerPage]);
  useEffect(() => {
    const controller = new AbortController(); setHistory(null);
    void api<Page<HistoryEvent>>(`/api/export-orders/${id}/history?page=${historyPage}&pageSize=20`, { signal: controller.signal })
      .then(setHistory).catch(cause => { if (!controller.signal.aborted) setError(message(cause)); });
    return () => controller.abort();
  }, [id, reload, historyPage]);
  useEffect(() => {
    if (!order) return;
    const controller = new AbortController();
    void Promise.all([
      api<Catalog>(`/api/markets/${order.marketId}`, { signal: controller.signal }),
      api<Catalog>(`/api/commodities/${order.commodityId}`, { signal: controller.signal }),
    ]).then(([nextMarket, nextCommodity]) => { setMarket(nextMarket); setCommodity(nextCommodity); })
      .catch(cause => { if (!controller.signal.aborted) setError(message(cause)); });
    return () => controller.abort();
  }, [order?.marketId, order?.commodityId]);
  const touch = () => { setOperationId(makeId()); setErrors({}); setError(''); };
  async function save(event?: FormEvent) {
    event?.preventDefault(); if ((!edit || !etag) && !pendingMutation || busy) return;
    setBusy(true); setError(''); setErrors({});
    const route = edit === 'header' ? `/api/export-orders/${id}` : `/api/export-orders/${id}/${edit === 'spec' ? 'specifications' : 'containers'}`;
    const body = edit === 'header' ? { ...header, operationId } : edit === 'spec' ? { operationId, limits: limitPayload(limits) } : { operationId, containers: targets };
    const request = pendingMutation ?? { route, method: edit === 'header' ? 'PATCH' as const : 'POST' as const, body: JSON.stringify(body), etag: etag! };
    sessionStorage.setItem(mutationKey, JSON.stringify(request));
    try {
      await api<Order>(request.route, { method: request.method,
        headers: { 'If-Match': request.etag }, body: request.body });
      sessionStorage.removeItem(mutationKey); setPendingMutation(null);
      setEdit(null); setOperationId(makeId()); setTargets([{ targetMass: '', unit: '' }]); setSpecificationPage(1); setReload(n => n + 1);
    } catch (cause) {
      if (cause instanceof ApiError) { setErrors(cause.errors ?? {}); if (cause.errors) focusFirstFieldError(); }
      if (isUncertain(cause)) setPendingMutation(request);
      else { sessionStorage.removeItem(mutationKey); setPendingMutation(null); }
      setError(uncertain(cause));
    } finally { setBusy(false); }
  }
  return <><button className="text-button" onClick={() => onNavigate('/export/orders')}>← Back to orders</button>
    {loading && <p role="status">Loading order…</p>}{error && <div className="notice error" role="alert">{error}</div>}
    {pendingMutation && <div className="notice error" role="alert">A previous change has an uncertain result. <button disabled={busy} onClick={() => void save()}>Retry same change</button></div>}
    {order && <><div className="page-heading"><span className="eyebrow">EXPORT ORDER</span><h1>{order.code}</h1><p className="muted">Shipping {order.shippingFrom} to {order.shippingTo}</p></div>
       <section className="panel export-section"><div className="section-toolbar"><h2>Order details</h2>{canWrite && order.canEdit && !pendingMutation && !busy && edit !== 'header' && <button className="secondary" onClick={() => { setEdit('header'); touch(); }}>Edit order</button>}</div>
        <dl className="export-facts"><div><dt>Market</dt><dd>{market?.name ?? 'Loading…'}</dd></div><div><dt>Commodity</dt><dd>{commodity?.name ?? 'Loading…'}</dd></div><div><dt>Required</dt><dd>{order.requiredMassKg} kg</dd></div><div><dt>Allocated</dt><dd>{order.allocatedMassKg} kg</dd></div><div><dt>Remaining</dt><dd>{order.remainingMassKg} kg</dd></div></dl>
         {edit === 'header' && <form className="export-edit-form" onSubmit={save}><fieldset className="export-fieldset" disabled={busy || !!pendingMutation}><div className="admin-form-grid export-order-fields"><div><CatalogSelect kind="markets" label="Export market" value={header.marketId} onChange={marketId => { setHeader(x => ({ ...x, marketId })); touch(); }} /><FieldError errors={errors} name="marketId" /></div><div><CatalogSelect kind="commodities" label="Commodity" value={header.commodityId} onChange={commodityId => { setHeader(x => ({ ...x, commodityId })); touch(); }} /><FieldError errors={errors} name="commodityId" /></div><label>Required mass<input value={header.requiredMass} onChange={event => { setHeader(x => ({ ...x, requiredMass: event.target.value })); touch(); }} required /><FieldError errors={errors} name="requiredMass" /></label><MassUnitSelect value={header.unit} onChange={unit => { setHeader(x => ({ ...x, unit })); touch(); }} catalog={massUnits} errors={errors} name="unit" /><label>Shipping from<input type="date" value={header.shippingFrom} onChange={event => { setHeader(x => ({ ...x, shippingFrom: event.target.value })); touch(); }} required /><FieldError errors={errors} name="shippingFrom" /></label><label>Shipping to<input type="date" value={header.shippingTo} min={header.shippingFrom} onChange={event => { setHeader(x => ({ ...x, shippingTo: event.target.value })); touch(); }} required /><FieldError errors={errors} name="shippingTo" /></label></div><div className="admin-form-actions"><button>{busy ? 'Saving…' : 'Save order'}</button><button type="button" className="secondary" onClick={() => setEdit(null)}>Cancel</button></div></fieldset></form>}
      </section>
       {edit === 'spec' ? <form className="export-edit-form" onSubmit={save}><fieldset className="export-fieldset" disabled={busy || !!pendingMutation}><LimitsEditor rows={limits} onChange={rows => { setLimits(rows); touch(); }} errors={errors} /><div className="admin-form-actions"><button>{busy ? 'Saving…' : 'Save new specification version'}</button><button type="button" className="secondary" onClick={() => setEdit(null)}>Cancel</button></div></fieldset></form>
         : <section className="panel export-section"><div className="section-toolbar"><h2>Quality specification · version {selectedVersion ?? order.specificationVersion}</h2>{canWrite && order.canEdit && !pendingMutation && !busy && <button className="secondary" onClick={() => { setEdit('spec'); touch(); }}>New version</button>}</div>{specifications ? <><label>View version<select value={selectedVersion ?? specifications.items[0]?.versionNo ?? ''} onChange={event => setSelectedVersion(Number(event.target.value))}>{specifications.items.map(spec => <option key={spec.versionNo} value={spec.versionNo}>Version {spec.versionNo} · {new Date(spec.recordedAt).toLocaleString()}</option>)}</select></label><div className="form-footer"><span>Versions page {specificationPage} · {specifications.totalCount} total</span><div><button className="secondary" disabled={specificationPage === 1} onClick={() => setSpecificationPage(page => page - 1)}>Previous</button> <button className="secondary" disabled={!specifications.hasMore} onClick={() => setSpecificationPage(page => page + 1)}>Next</button></div></div></> : <p role="status">Loading specifications…</p>}<div className="table-scroll"><table><thead><tr><th>Attribute</th><th>Minimum</th><th>Maximum</th><th>Unit</th></tr></thead><tbody>{(specifications?.items.find(spec => spec.versionNo === selectedVersion)?.limits ?? (specificationPage === 1 ? order.limits : [])).map(row => <tr key={row.attributeId}><td>{row.attributeName || row.attributeCode}</td><td>{row.minValue ?? '—'}</td><td>{row.maxValue ?? '—'}</td><td>{row.unit}</td></tr>)}</tbody></table></div></section>}
       <section className="panel export-section"><h2>Container targets</h2><p className="muted">Target mass records the planned allocation. Loading is recorded later.</p>{!containers && <p role="status">Loading container targets…</p>}<div className="table-scroll"><table><thead><tr><th>Internal code</th><th>Target</th><th>Status</th><th /></tr></thead><tbody>{containers?.items.map(row => <tr key={row.id}><td>{row.internalCode}</td><td>{row.targetMassKg} kg</td><td>{row.status}</td><td><button className="text-button" onClick={() => onNavigate(`/export/containers/${row.id}`)}>Open</button></td></tr>)}</tbody></table></div><div className="form-footer"><span>Page {containerPage}{containers ? ` · ${containers.totalCount} targets` : ''}</span><div><button className="secondary" disabled={!containers || containerPage === 1} onClick={() => setContainerPage(page => page - 1)}>Previous</button> <button className="secondary" disabled={!containers?.hasMore} onClick={() => setContainerPage(page => page + 1)}>Next</button></div></div></section>
       {canWrite && order.canEdit && (edit === 'containers' ? <form className="export-edit-form" onSubmit={save}><fieldset className="export-fieldset" disabled={busy || !!pendingMutation}><TargetsEditor rows={targets} onChange={rows => { setTargets(rows); touch(); }} errors={errors} massUnits={massUnits} /><div className="admin-form-actions"><button>{busy ? 'Saving…' : 'Add targets'}</button><button type="button" className="secondary" onClick={() => setEdit(null)}>Cancel</button></div></fieldset></form> : !pendingMutation && !busy && <button onClick={() => { setEdit('containers'); touch(); }}>+ Add container target</button>)}
       <section className="panel export-section"><h2>History</h2>{!history ? <p role="status">Loading history…</p> : history.items.length ? <ul>{history.items.map((row, index) => <li key={`${row.recordedAt}-${index}`}><strong>{row.actorName}</strong> · {new Date(row.recordedAt).toLocaleString()}<br />{historyDescription(row)}</li>)}</ul> : <p className="empty">No history.</p>}<div className="form-footer"><span>Page {historyPage}{history ? ` · ${history.totalCount} events` : ''}</span><div><button className="secondary" disabled={!history || historyPage === 1} onClick={() => setHistoryPage(page => page - 1)}>Previous</button> <button className="secondary" disabled={!history?.hasMore} onClick={() => setHistoryPage(page => page + 1)}>Next</button></div></div></section>
    </>}
  </>;
}

function ContainerDetail({ id, userId, canWrite, onNavigate }: { id: string; userId: string; canWrite: boolean; onNavigate: (path: string) => void }) {
  const massUnits = useMassUnits();
  const [row, setRow] = useState<Container | null>(null); const [etag, setEtag] = useState<string | null>(null);
  const [mass, setMass] = useState(''); const [unit, setUnit] = useState('');
  const [operationId, setOperationId] = useState(makeId); const [edit, setEdit] = useState(false);
  const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const mutationKey = `grainledger.export.pending-container.${userId}.${id}`;
  const [pendingMutation, setPendingMutation] = useState<PendingMutation | null>(() => readPendingMutation(mutationKey));
  const [reload, setReload] = useState(0);
  useEffect(() => {
    const controller = new AbortController(); setError('');
    void apiWithEtag<Container>(`/api/export-containers/${id}`, { signal: controller.signal })
      .then(result => { setRow(result.data); setEtag(result.etag); setMass(result.data.originalMass); setUnit(result.data.originalUnit); })
      .catch(cause => { if (!controller.signal.aborted) setError(message(cause)); });
    return () => controller.abort();
  }, [id, reload]);
  async function save(event?: FormEvent) {
    event?.preventDefault(); if (busy || !etag && !pendingMutation) return; setBusy(true); setError(''); setErrors({});
    const request = pendingMutation ?? { route: `/api/export-containers/${id}`, method: 'PATCH' as const,
      etag: etag!, body: JSON.stringify({ targetMass: mass, unit, operationId }) };
    sessionStorage.setItem(mutationKey, JSON.stringify(request));
    try {
      await api<Container>(request.route, { method: request.method, headers: { 'If-Match': request.etag },
        body: request.body });
      sessionStorage.removeItem(mutationKey); setPendingMutation(null);
      setEdit(false); setOperationId(makeId()); setReload(n => n + 1);
    } catch (cause) {
      if (cause instanceof ApiError) { setErrors(cause.errors ?? {}); if (cause.errors) focusFirstFieldError(); }
      if (isUncertain(cause)) setPendingMutation(request);
      else { sessionStorage.removeItem(mutationKey); setPendingMutation(null); }
      setError(uncertain(cause));
    } finally { setBusy(false); }
  }
  return <><button className="text-button" onClick={() => row ? onNavigate(`/export/orders/${row.orderId}`) : onNavigate('/export/orders')}>← Back to order</button>
    {error && <div className="notice error" role="alert">{error}</div>}
    {pendingMutation && <div className="notice error" role="alert">A previous change has an uncertain result. <button disabled={busy} onClick={() => void save()}>Retry same change</button></div>}
    {row ? <><div className="page-heading"><span className="eyebrow">PLANNED CONTAINER</span><h1>{row.internalCode}</h1></div><section className="panel export-section"><dl className="export-facts"><div><dt>Target mass</dt><dd>{row.targetMassKg} kg</dd></div><div><dt>Status</dt><dd>{row.status}</dd></div></dl>
      {canWrite && row.status === 'PLANNED' && (edit ? <form className="export-edit-form" onSubmit={save}><fieldset className="export-fieldset" disabled={busy || !!pendingMutation}><div className="form-columns"><label>Target mass<input value={mass} onChange={event => { setMass(event.target.value); setOperationId(makeId()); setErrors({}); }} required /><FieldError errors={errors} name="targetMass" /></label><MassUnitSelect value={unit} onChange={value => { setUnit(value); setOperationId(makeId()); setErrors({}); }} catalog={massUnits} errors={errors} name="unit" /></div><div className="admin-form-actions"><button>{busy ? 'Saving…' : 'Save target'}</button><button type="button" className="secondary" onClick={() => setEdit(false)}>Cancel</button></div></fieldset></form> : !pendingMutation && !busy && <button className="secondary" onClick={() => setEdit(true)}>Edit target</button>)}
    </section></> : !error && <p role="status">Loading container…</p>}
  </>;
}
