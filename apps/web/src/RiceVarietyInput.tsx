import { useEffect, useState } from 'react';
import { api, errorMessage } from './api';
import { type Page } from './procurementTypes';

type Variety = { id: string; code: string; name: string };
export function RiceVarietyInput({ value, onChange, disabled = false }: { value: string; onChange: (value: string) => void; disabled?: boolean }) {
  const [varieties, setVarieties] = useState<Variety[]>([]);
  const [error, setError] = useState('');
  useEffect(() => {
    let active = true; const controller = new AbortController();
    void (async () => {
      const rows: Variety[] = [];
      for (let page = 1; page <= 100; page++) {
        const result = await api<Page<Variety>>(`/api/rice-varieties?page=${page}&pageSize=100`, { signal: controller.signal });
        rows.push(...result.items);
        if (!result.hasMore) { if (active) setVarieties(rows); return; }
      }
      throw new Error('Too many rice varieties to load.');
    })().catch(cause => { if (active) setError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, []);
  return <><label>Rice variety <span className="required-mark">*</span><input list="rice-variety-options" value={value} onChange={event => onChange(event.target.value)} maxLength={100} required disabled={disabled} placeholder="Select a variety or enter its name" /></label><datalist id="rice-variety-options">{varieties.map(item => <option key={item.id} value={item.name} label={item.code} />)}</datalist><p className="field-help">Suggestions use the rice variety catalog. The registration stores the entered name.</p>{error && <p className="muted small" role="status">Variety suggestions unavailable: {error}</p>}</>;
}
