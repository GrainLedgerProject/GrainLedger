import { useEffect, useState, type ReactNode } from 'react';
import { api, errorMessage } from './api';

export type Row = Record<string, any>;
export type Lookup = { owners: Row[]; facilities: Row[]; locations: Row[]; commodities: Row[]; equipment: Row[] };
export type Lot = { id: string; code: string; ownerId: string; commodityId: string; stage: string; productIdentity: string | null; positions: Row[]; restrictions: string[]; mixedBulk?: boolean; poolLocationId?: string | null; poolRevision?: number | null; isCurrentPool?: boolean; custodyDerived?: boolean; sourceComposition?: Row[]; contributions?: Row[]; history?: Page<Row> };
export type Operation = { id: string; ownerId: string; facilityId: string; kind: string; occurredAt: string; equipmentId: string; inputs: Row[]; outputs: Row[]; declaredLossKg: string; status: string; version: string; confirmedVersion?: string | null; varianceKg?: string | null; allowedVarianceKg?: string | null; createdBy?: string; canEdit?: boolean; canDelete?: boolean; boundCorrectionId?: string | null };
export type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number; hasMore: boolean };
export type PaginationProps = { page: number; pageSize: number; totalCount: number; onChange: (page: number) => void; label: string; busy?: boolean };
export type ListControlsProps = { label: string; search: string; onSearch: (value: string) => void; sortBy: string; onSortBy: (value: string) => void; sortDirection: 'asc' | 'desc'; onSortDirection: (value: 'asc' | 'desc') => void; sortOptions: { value: string; label: string }[]; children?: ReactNode };
export function Pagination({ page, pageSize, totalCount, onChange, label, busy = false }: PaginationProps) {
  const maxPage = Math.max(1, Math.ceil(totalCount / Math.max(1, pageSize))), current = Math.min(Math.max(1, page), maxPage);
  const start = Math.max(1, Math.min(current - 2, maxPage - 4)), end = Math.min(maxPage, start + 4);
  const pages = Array.from({ length: end - start + 1 }, (_, index) => start + index);
  return <nav className="list-pagination" aria-label={`${label} pagination`}><button type="button" className="list-control-button" aria-label={`First ${label} page`} disabled={busy || current <= 1} onClick={() => onChange(1)}>&lt;&lt;</button><button type="button" className="list-control-button" aria-label={`Previous ${label} page`} disabled={busy || current <= 1} onClick={() => onChange(current - 1)}>&lt;</button>{start > 1 && <span aria-hidden="true">…</span>}{pages.map(value => <button type="button" className="list-control-button" key={value} aria-label={`Page ${value} of ${label}`} aria-current={value === current ? 'page' : undefined} disabled={busy} onClick={() => onChange(value)}>{value}</button>)}{end < maxPage && <span aria-hidden="true">…</span>}<button type="button" className="list-control-button" aria-label={`Next ${label} page`} disabled={busy || current >= maxPage} onClick={() => onChange(current + 1)}>&gt;</button><button type="button" className="list-control-button" aria-label={`Last ${label} page`} disabled={busy || current >= maxPage} onClick={() => onChange(maxPage)}>&gt;&gt;</button></nav>;
}
export function ListControls({ label, search, onSearch, sortBy, onSortBy, sortDirection, onSortDirection, sortOptions, children }: ListControlsProps) {
  return <div className="list-controls"><label>Search {label}<input type="search" value={search} onKeyDown={event => { if (event.key === 'Enter') event.preventDefault(); }} onChange={event => onSearch(event.target.value)} /></label><label>Sort {label} by<select value={sortBy} onChange={event => onSortBy(event.target.value)}>{sortOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}</select></label><label>{label} sort direction<select value={sortDirection} onChange={event => onSortDirection(event.target.value as 'asc' | 'desc')}><option value="asc">Ascending</option><option value="desc">Descending</option></select></label>{children}</div>;
}
export const emptyLookup: Lookup = { owners: [], facilities: [], locations: [], commodities: [], equipment: [] };
export const kinds = ['DRYING', 'MILLING', 'MERGE', 'BLENDING', 'BAGGING'];
export const stages = ['RAW_PADDY', 'DRIED_PADDY', 'HEAD_RICE', 'BROKEN_RICE', 'BRAN', 'HUSK', 'BLENDED_RICE', 'BAGGED_RICE'];
export const products = ['HEAD_RICE', 'BROKEN_RICE', 'BLENDED_RICE'];
export const id = () => crypto.randomUUID();
export const now = () => localTime(new Date().toISOString());
export function localTime(value: string) { const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16); }
export const iso = (value: string) => new Date(value).toISOString();
export const name = (rows: Row[], value: string) => String(rows.find(row => row.id === value)?.name || value);
export const positive = (value: string) => /[1-9]/.test(value);
export const hasPositivePhysicalPosition = (lot: Lot) => lot.positions.some(position => positive(String(position.quantityKg ?? '0')));
export const isUnavailableHistoricalMixedLot = (lot: Lot) => lot.mixedBulk === true && !lot.isCurrentPool && !hasPositivePhysicalPosition(lot);
export function lotLabel(lot: Lot, locations: Row[]) {
  const positions = lot.positions.map(position => `${name(locations, position.locationId)}: physical ${position.quantityKg} kg, available ${position.availableKg} kg`).join('; ');
  return `${lot.code} [${lot.id.slice(0, 8)}] — ${lot.stage} — ${lot.restrictions.join(', ') || 'Unrestricted'} — ${positions || 'No recorded positions'}`;
}
export function useAction() {
  const [error, setError] = useState(''), [status, setStatus] = useState(''), [busy, setBusy] = useState(false);
  async function run<T>(action: () => Promise<T>, message = ''): Promise<T | undefined> {
    setBusy(true); setError(''); setStatus('');
    try { const result = await action(); setStatus(message); return result; }
    catch (e) { setError(errorMessage(e)); return undefined; }
    finally { setBusy(false); }
  }
  return { error, status, busy, run, setStatus, setError };
}
export const mutate = <T,>(path: string, body: Row, method = 'POST') => api<T>(path, { method, body: JSON.stringify(body) });
export function Feedback({ error, status }: { error?: string; status?: string }) { return <>{error && <div className="notice error" role="alert">{error}</div>}{status && <div className="notice" role="status">{status}</div>}</>; }
export function Panel({ title, children }: { title: string; children: ReactNode }) { return <section className="panel"><h2>{title}</h2>{children}</section>; }
export function Choice({ rows, value, onChange, optional = false }: { rows: Row[]; value: string; onChange: (value: string) => void; optional?: boolean }) { return <select required={!optional} value={value} onChange={event => onChange(event.target.value)}><option value="">{optional ? 'Any / none' : 'Select…'}</option>{rows.map(row => <option key={row.id} value={row.id}>{row.name || row.code || row.label || row.id}</option>)}</select>; }
export function Enum({ values, value, onChange, optional = false }: { values: string[]; value: string; onChange: (value: string) => void; optional?: boolean }) { return <Choice rows={values.map(id => ({ id, name: id }))} value={value} onChange={onChange} optional={optional} />; }
export function Mass({ value, onChange, label = 'Quantity (kg)' }: { value: string; onChange: (value: string) => void; label?: string }) { return <label>{label}<input required inputMode="decimal" pattern="[0-9]+(\.[0-9]{1,6})?" value={value} onChange={event => onChange(event.target.value)} /></label>; }
function decimalCompare(left: unknown, right: unknown): number | undefined {
  if (typeof left !== 'string' || typeof right !== 'string' || !/^-?\d+(?:\.\d+)?$/.test(left) || !/^-?\d+(?:\.\d+)?$/.test(right)) return undefined;
  const parse = (value: string) => { const negative = value.startsWith('-'), [whole, fraction = ''] = (negative ? value.slice(1) : value).split('.'), scale = 10n ** BigInt(fraction.length); return { value: (negative ? -1n : 1n) * (BigInt(whole) * scale + BigInt(fraction || '0')), scale }; };
  const a = parse(left), b = parse(right), difference = a.value * b.scale - b.value * a.scale;
  return difference < 0n ? -1 : difference > 0n ? 1 : 0;
}
function compareValues(left: unknown, right: unknown): number {
  const decimal = decimalCompare(left, right);
  if (decimal !== undefined) return decimal;
  if (left == null || right == null) return left == null ? right == null ? 0 : -1 : 1;
  if (typeof left === 'number' && typeof right === 'number') return left - right;
  return String(left).localeCompare(String(right), undefined, { numeric: true, sensitivity: 'base' });
}
export function Table({ rows, label = 'Records', controls = true, columns, actions }: { rows: Row[]; label?: string; controls?: boolean; columns?: string[]; actions?: (row: Row) => ReactNode }) {
  const [search, setSearch] = useState(''), [sortBy, setSortBy] = useState(''), [direction, setDirection] = useState<'asc' | 'desc'>('asc'), [page, setPage] = useState(1);
  const allColumns = columns || Array.from(new Set(rows.flatMap(row => Object.keys(row)))).filter(column => !['createdBy', 'canEdit', 'canDelete', 'version'].includes(column));
  const sortKey = allColumns.includes(sortBy) ? sortBy : allColumns[0] || '';
  const matching = controls ? rows.filter(row => !search || allColumns.some(column => String(row[column] == null ? '' : typeof row[column] === 'object' ? JSON.stringify(row[column]) : row[column]).toLocaleLowerCase().includes(search.toLocaleLowerCase()))) : rows;
  const sorted = controls ? [...matching].sort((a, b) => direction === 'asc' ? compareValues(a[sortKey], b[sortKey]) || String(a.id ?? '').localeCompare(String(b.id ?? '')) : compareValues(b[sortKey], a[sortKey]) || String(a.id ?? '').localeCompare(String(b.id ?? ''))) : matching;
  const pageSize = 25, totalPages = Math.max(1, Math.ceil(sorted.length / pageSize)), safePage = controls ? Math.min(page, totalPages) : 1;
  useEffect(() => { if (controls) setPage(current => Math.min(current, totalPages)); }, [controls, totalPages]);
  const shown = controls ? sorted.slice((safePage - 1) * pageSize, safePage * pageSize) : rows;
  if (!rows.length && !controls) return <p className="empty">No records.</p>;
  if (controls && !matching.length) return <><ListControls label={label} search={search} onSearch={value => { setSearch(value); setPage(1); }} sortBy={sortKey} onSortBy={value => { setSortBy(value); setPage(1); }} sortDirection={direction} onSortDirection={value => { setDirection(value); setPage(1); }} sortOptions={allColumns.map(column => ({ value: column, label: column }))} /><p className="empty">{rows.length ? `No matching ${label.toLowerCase()}.` : 'No records.'}</p></>;
  return <>{controls && <ListControls label={label} search={search} onSearch={value => { setSearch(value); setPage(1); }} sortBy={sortKey} onSortBy={value => { setSortBy(value); setPage(1); }} sortDirection={direction} onSortDirection={value => { setDirection(value); setPage(1); }} sortOptions={allColumns.map(column => ({ value: column, label: column }))} />}<div className="table-scroll"><table aria-label={label}><thead><tr>{allColumns.map(column => <th key={column} scope="col">{column}</th>)}{actions && <th scope="col"><span className="sr-only">Actions</span></th>}</tr></thead><tbody>{shown.map((row, index) => <tr key={row.id || index}>{allColumns.map(column => <td key={column}>{row[column] == null ? '—' : typeof row[column] === 'object' ? <pre>{JSON.stringify(row[column], null, 2)}</pre> : String(row[column])}</td>)}{actions && <td>{actions(row)}</td>}</tr>)}</tbody></table></div>{controls && <Pagination label={label} page={safePage} pageSize={pageSize} totalCount={sorted.length} onChange={value => setPage(Math.min(value, totalPages))} />}</>;
}
export function Evidence({ ownerId, facilityId = '', value, onChange }: { ownerId: string; facilityId?: string; value: string; onChange: (value: string) => void }) {
  const [file, setFile] = useState<File | null>(null), action = useAction();
  async function upload() {
    await action.run(async () => {
      if (!file || !file.size || file.size > 10485760) throw Error('Choose a nonempty evidence file, maximum 10 MiB.');
      const contentBase64 = await new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onerror = () => reject(Error('Evidence file could not be read.')); reader.onload = () => resolve(String(reader.result).split(',')[1]); reader.readAsDataURL(file); });
      const result = await mutate<{ id: string }>('/api/ledger/evidence', { id: id(), ownerId, facilityId, fileName: file.name, contentType: file.type || 'application/octet-stream', contentBase64 });
      onChange(result.id);
    }, 'Evidence stored immutably.');
  }
  return <fieldset className="evidence"><legend>Immutable evidence</legend><label>Evidence file<input type="file" onChange={event => { setFile(event.target.files?.[0] || null); onChange(''); }} /></label><button type="button" disabled={action.busy || !file || !ownerId || !facilityId} onClick={() => void upload()}>{action.busy ? 'Uploading…' : 'Upload evidence'}</button>{value && <p><a href={`/api/ledger/evidence/${value}`} target="_blank" rel="noreferrer">View stored evidence</a></p>}<Feedback {...action} /></fieldset>;
}
