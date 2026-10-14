import { useState, type ReactNode } from 'react';
import { api, errorMessage } from './api';

export type Row = Record<string, any>;
export type Lookup = { owners: Row[]; facilities: Row[]; locations: Row[]; commodities: Row[]; equipment: Row[] };
export type Lot = { id: string; code: string; ownerId: string; commodityId: string; stage: string; productIdentity: string | null; positions: Row[]; restrictions: string[]; contributions?: Row[]; history?: Row[] };
export type Operation = { id: string; ownerId: string; facilityId: string; kind: string; occurredAt: string; equipmentId: string; inputs: Row[]; outputs: Row[]; declaredLossKg: string; status: string; version: string; confirmedVersion?: string | null; varianceKg?: string | null; allowedVarianceKg?: string | null };
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
  return { error, status, busy, run, setStatus };
}
export const mutate = <T,>(path: string, body: Row, method = 'POST') => api<T>(path, { method, body: JSON.stringify(body) });
export function Feedback({ error, status }: { error?: string; status?: string }) { return <>{error && <div className="notice error" role="alert">{error}</div>}{status && <div className="notice" role="status">{status}</div>}</>; }
export function Panel({ title, children }: { title: string; children: ReactNode }) { return <section className="panel"><h2>{title}</h2>{children}</section>; }
export function Choice({ rows, value, onChange, optional = false }: { rows: Row[]; value: string; onChange: (value: string) => void; optional?: boolean }) { return <select required={!optional} value={value} onChange={e => onChange(e.target.value)}><option value="">{optional ? 'Any / none' : 'Select…'}</option>{rows.map(row => <option key={row.id} value={row.id}>{row.name || row.code || row.label || row.id}</option>)}</select>; }
export function Enum({ values, value, onChange, optional = false }: { values: string[]; value: string; onChange: (value: string) => void; optional?: boolean }) { return <Choice rows={values.map(id => ({ id, name: id }))} value={value} onChange={onChange} optional={optional} />; }
export function Mass({ value, onChange, label = 'Quantity (kg)' }: { value: string; onChange: (value: string) => void; label?: string }) { return <label>{label}<input required inputMode="decimal" pattern="[0-9]+(\.[0-9]{1,6})?" value={value} onChange={e => onChange(e.target.value)} /></label>; }
export function Table({ rows }: { rows: Row[] }) {
  if (!rows.length) return <p className="empty">No records.</p>;
  const columns = Array.from(new Set(rows.flatMap(row => Object.keys(row))));
  return <div className="table-scroll"><table><thead><tr>{columns.map(column => <th key={column} scope="col">{column}</th>)}</tr></thead><tbody>{rows.map((row, index) => <tr key={row.id || index}>{columns.map(column => <td key={column}>{row[column] == null ? '—' : typeof row[column] === 'object' ? <pre>{JSON.stringify(row[column], null, 2)}</pre> : String(row[column])}</td>)}</tr>)}</tbody></table></div>;
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
  return <fieldset className="evidence"><legend>Immutable evidence</legend><label>Evidence file<input type="file" onChange={e => { setFile(e.target.files?.[0] || null); onChange(''); }} /></label><button type="button" disabled={action.busy || !file || !ownerId || !facilityId} onClick={() => void upload()}>{action.busy ? 'Uploading…' : 'Upload evidence'}</button>{value && <p><a href={`/api/ledger/evidence/${value}`} target="_blank" rel="noreferrer">View stored evidence</a></p>}<Feedback {...action} /></fieldset>;
}
