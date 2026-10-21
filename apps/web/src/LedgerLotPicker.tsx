import { useEffect, useRef, useState } from 'react';
import { api } from './api';
import { Feedback, isUnavailableHistoricalMixedLot, Pagination, name, useAction, type Lookup, type Lot, type Page } from './ledger-ui';

type Props = { label: string; value: string; onChange: (id: string, selectedLot?: Lot) => void; onSelected?: (lot: Lot | undefined) => void; look: Lookup; ownerId?: string; facilityId?: string; disabled?: boolean; lockedLotId?: string; pinnedLots?: Lot[]; availableOnly?: boolean; required?: boolean; allowHistorical?: boolean };
export function LedgerLotPicker({ label, value, onChange, onSelected, look, ownerId, facilityId, disabled = false, lockedLotId, pinnedLots = [], availableOnly = true, required = true, allowHistorical = false }: Props) {
  const [search, setSearch] = useState(''), [page, setPage] = useState(1), [reload, setReload] = useState(0), [result, setResult] = useState<Page<Lot>>({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false }), [selected, setSelected] = useState<Lot | undefined>(), action = useAction(), requestId = useRef(0), pinnedRef = useRef(pinnedLots), selectedCallback = useRef(onSelected);
  pinnedRef.current = pinnedLots;
  selectedCallback.current = onSelected;
  useEffect(() => { setPage(1); }, [ownerId, facilityId]);
  useEffect(() => {
    const request = ++requestId.current;
    setResult({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
    if (disabled || lockedLotId) return () => { requestId.current++; };
    void action.run(async () => {
      const query = new URLSearchParams({ page: String(page), pageSize: '25', sortBy: 'code', sortDirection: 'asc' });
      if (search) query.set('search', search);
      if (ownerId) query.set('ownerId', ownerId);
      if (facilityId) query.set('facilityId', facilityId);
      if (availableOnly) query.set('availableOnly', 'true');
      const next = await api<Page<Lot>>(`/api/ledger/lots?${query}`);
      if (request === requestId.current) setResult(next);
    });
    return () => { requestId.current++; };
  }, [page, search, ownerId, facilityId, disabled, lockedLotId, reload, availableOnly]);
  useEffect(() => {
    if (!value) { setSelected(undefined); selectedCallback.current?.(undefined); return; }
    const scopedSelection = selected && selected.id === value && (!ownerId || selected.ownerId === ownerId) && (!facilityId || selected.positions.some(position => position.facilityId === facilityId));
    if (scopedSelection) { selectedCallback.current?.(selected); return; }
    const current = [...result.items, ...pinnedRef.current].find(lot => lot.id === value && (!ownerId || lot.ownerId === ownerId) && (!facilityId || lot.positions.some(position => position.facilityId === facilityId)));
    if (current) { setSelected(current); selectedCallback.current?.(current); return; }
    if (disabled || lockedLotId && !ownerId && !facilityId) { setSelected(undefined); selectedCallback.current?.(undefined); return; }
    let live = true;
    void action.run(async () => {
      const query = new URLSearchParams({ page: '1', pageSize: '1', ids: value });
      if (ownerId) query.set('ownerId', ownerId);
      if (facilityId) query.set('facilityId', facilityId);
      const result = await api<Page<Lot>>(`/api/ledger/lots?${query}`);
      const lot = result.items.find(item => item.id === value);
      if (live) { setSelected(lot); selectedCallback.current?.(lot); }
    });
    return () => { live = false; };
  }, [value, result.items, ownerId, facilityId, disabled, lockedLotId, selected]);
  const options = new Map<string, Lot>();
  for (const lot of [...result.items, ...pinnedRef.current, ...(selected ? [selected] : [])]) {
    if (lot.id !== value && (!ownerId || lot.ownerId === ownerId) && (!facilityId || lot.positions.some(position => position.facilityId === facilityId))) options.set(lot.id, lot);
  }
  if (selected && selected.id === value && (!ownerId || selected.ownerId === ownerId) && (!facilityId || selected.positions.some(position => position.facilityId === facilityId))) options.set(selected.id, selected);
  const lots = [...options.values()];
  return <><Feedback {...action} /><label>{label}<select required={required} value={value} onChange={event => { const lot = lots.find(row => row.id === event.target.value); onChange(lockedLotId || event.target.value, lot); setSelected(lot); selectedCallback.current?.(lot); }} disabled={disabled || !!lockedLotId}><option value="">Select…</option>{lots.map(lot => <option key={lot.id} value={lot.id} disabled={!allowHistorical && isUnavailableHistoricalMixedLot(lot)}>{lot.code} — {lot.stage} — {lot.restrictions.join(', ') || 'Unrestricted'} — {lot.positions.map(position => `${name(look.locations, position.locationId)}: ${position.quantityKg} kg`).join('; ')}{lot.isCurrentPool ? ` — current pool revision ${lot.poolRevision}` : isUnavailableHistoricalMixedLot(lot) ? allowHistorical ? ' — historical generation' : ' — historical generation; unavailable' : lot.custodyDerived ? ' — custody-derived stock at current position' : ''}</option>)}</select></label>{!allowHistorical && selected?.id === value && selected.isCurrentPool && <p role="status">Observed current pool revision {selected.poolRevision}. If stale, reload, reselect lot and save draft again; saved draft is not silently rewritten.</p>}{selected?.id === value && isUnavailableHistoricalMixedLot(selected) && <p role="status">{allowHistorical ? 'Historical mixed-bulk generation has no positive physical stock; recorded history remains traceable.' : 'Saved historical mixed-bulk generation has no positive physical stock. Reload options and reselect before saving.'}</p>}{!disabled && !lockedLotId && <><button type="button" className="secondary" disabled={action.busy} onClick={() => { onChange('', undefined); setSelected(undefined); selectedCallback.current?.(undefined); setResult({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false }); setPage(1); setSearch(''); setReload(value => value + 1); }}>Reload stock options and reselect</button><label>Search {label}<input type="search" value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} onKeyDown={event => { if (event.key === 'Enter') event.preventDefault(); }} /></label><Pagination page={result.page} pageSize={result.pageSize} totalCount={result.totalCount} onChange={setPage} label={label} busy={action.busy} /></>}</>;
}
