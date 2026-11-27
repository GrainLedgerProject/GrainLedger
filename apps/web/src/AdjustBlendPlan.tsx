import { useState } from 'react';

export type AdjustablePosition = {
  lotId: string; locationId: string; lotCode: string; locationName: string; availableKg: string;
};
export type AdjustedAllocation = { lotId: string; locationId: string; lotCode: string; quantityKg: string };
const key = (row: { lotId: string; locationId: string }) => `${row.lotId}:${row.locationId}`;
const wholeKg = (value: string) => /^\d{1,14}$/.test(value) && BigInt(value) > 0n;
const floorKg = (value: string) => value.split('.')[0];

export function AdjustBlendPlan({ allocations, totalKg, disabled, busy, loadPositions, onEditing, onCheck, onCancel }: {
  allocations: AdjustedAllocation[]; totalKg: string; disabled: boolean; busy: boolean;
  loadPositions: () => Promise<AdjustablePosition[]>;
  onEditing: (editing: boolean) => void; onCheck: (rows: AdjustedAllocation[]) => void; onCancel: () => void;
}) {
  const [editing, setEditing] = useState(false);
  const [loading, setLoading] = useState(false);
  const [positions, setPositions] = useState<AdjustablePosition[]>([]);
  const [rows, setRows] = useState(allocations);
  const [addition, setAddition] = useState('');
  const [error, setError] = useState('');
  const total = rows.reduce((sum, row) => sum + (wholeKg(row.quantityKg) ? BigInt(row.quantityKg) : 0n), 0n);
  const valid = rows.length > 0 && rows.length <= 100 && rows.every(row => {
    const available = positions.find(p => key(p) === key(row))?.availableKg;
    return wholeKg(row.quantityKg) && available !== undefined && BigInt(row.quantityKg) <= BigInt(floorKg(available));
  }) && total.toString() === floorKg(totalKg);
  async function open() {
    setLoading(true); setError('');
    try {
      const options = await loadPositions();
      if ([totalKg, ...allocations.map(row => row.quantityKg)].some(value => !/^\d+(?:\.0+)?$/.test(value)))
        throw new Error('Calculate a new whole-kg plan before adjusting.');
      setPositions(options); setRows(allocations.map(row => ({ ...row, quantityKg: floorKg(row.quantityKg) })));
      setEditing(true); onEditing(true);
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Stock could not load. Refresh stock and try again.'); }
    finally { setLoading(false); }
  }
  return <section className="f14-ratio-card" aria-label="Adjust blend plan">
    <h3>Adjust blend plan</h3>
    {error && <p role="alert">{error}</p>}
    {!editing ? <><p>Change quantities, remove lots or add another lot at a stock location. All changes require a new F14 check.</p>
      <button className="secondary" disabled={disabled || busy || loading} onClick={() => void open()}>{loading ? 'Loading stock…' : 'Edit allocations'}</button></> : <>
      <p className="notice pending">Unchecked adjustments. The previous PASS does not apply to these quantities.</p>
      <div className="table-scroll"><table><thead><tr><th>Lot</th><th>Location</th><th>Available (kg)</th><th>Take (kg)</th><th /></tr></thead>
        <tbody>{rows.map(row => { const position = positions.find(p => key(p) === key(row)); return <tr key={key(row)}>
          <td>{row.lotCode}</td><td>{position?.locationName ?? 'Location unavailable'}</td><td>{position?.availableKg ?? 'Unavailable'}</td>
          <td><input type="number" min="1" step="1" max={position && floorKg(position.availableKg)} aria-label={`Quantity for ${row.lotCode} at ${row.locationId}`}
            value={row.quantityKg} disabled={busy || disabled} onChange={event => { const quantityKg = event.target.value;
              setRows(rows.map(x => key(x) === key(row) ? { ...x, quantityKg } : x)); onEditing(true); }} /></td>
          <td><button className="secondary" disabled={busy || disabled} onClick={() => { setRows(rows.filter(x => key(x) !== key(row))); onEditing(true); }}>Remove {row.lotCode}</button></td>
        </tr>; })}</tbody></table></div>
      <label>Add stock position<select aria-label="Add stock position" value={addition} disabled={busy || disabled || rows.length >= 100} onChange={event => setAddition(event.target.value)}>
        <option value="">Select lot and location</option>{positions.filter(p => !rows.some(row => key(row) === key(p))).map(p =>
          <option key={key(p)} value={key(p)}>{p.lotCode} · {p.locationName} · {p.availableKg} kg available</option>)}
      </select></label>
      <button className="secondary" disabled={busy || disabled || !addition || rows.length >= 100} onClick={() => {
        const position = positions.find(p => key(p) === addition);
        if (position) setRows([...rows, { lotId: position.lotId, locationId: position.locationId, lotCode: position.lotCode, quantityKg: '1' }]);
        setAddition(''); onEditing(true);
      }}>Add lot</button>
      <p role="status">Adjusted input: {total.toString()} kg · Required input: {totalKg} kg</p>
      <p className="muted">Use positive whole kilograms within available stock. Keep the input total unchanged. Lab, MRL, quality and compatibility are checked by the server.</p>
      <div className="f14-actions"><button disabled={busy || disabled || !valid} onClick={() => onCheck(rows)}>Check adjusted plan</button>
        <button className="secondary" disabled={busy} onClick={() => { setEditing(false); onEditing(false); onCancel(); }}>Cancel adjustment</button></div>
    </>}
  </section>;
}
