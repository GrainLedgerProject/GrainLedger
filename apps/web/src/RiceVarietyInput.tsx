import { useEffect, useState } from 'react';
import { api, errorMessage } from './api';
import { type Page } from './procurementTypes';

type Variety = { id: string; code: string; name: string };
export function RiceVarietyInput({ value, onChange, disabled = false }: { value: string; onChange: (value: string) => void; disabled?: boolean }) {
  const [varieties, setVarieties] = useState<Variety[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true; const controller = new AbortController();
    setLoading(true); setError('');
    void (async () => {
      const rows: Variety[] = [];
      for (let page = 1; page <= 100; page++) {
        const result = await api<Page<Variety>>(`/api/rice-varieties?page=${page}&pageSize=100`, { signal: controller.signal });
        rows.push(...result.items);
        if (!result.hasMore) { if (active) setVarieties(rows); return; }
      }
      throw new Error('Too many rice varieties to load.');
    })().catch(cause => { if (active) setError(errorMessage(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [retry]);
  return <><label>Rice variety <span className="required-mark">*</span><select value={value} onChange={event => onChange(event.target.value)} required disabled={disabled || loading || !!error || varieties.length === 0}><option value="">Select a rice variety</option>{varieties.map(item => <option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}</select></label>{loading && <p role="status">Loading rice varieties…</p>}{!loading && !error && varieties.length === 0 && <p role="status">No rice varieties are configured.</p>}{error && <p role="alert">Could not load rice varieties: {error} <button type="button" className="text-button" onClick={() => setRetry(value => value + 1)}>Try again</button></p>}</>;
}
