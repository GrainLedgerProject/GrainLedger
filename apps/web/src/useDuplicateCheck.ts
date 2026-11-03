import { useEffect, useState } from 'react';
import { api } from './api';
import type { PlotPoint } from './plotCoordinates';

export type FarmerDuplicate = { id: string; code: string; name: string; contact: string | null; reasons: string[] };
export type PlotDuplicate = { id: string; farmerId: string; code: string; distanceMeters: number; reasons: string[] };
type DuplicateState<T> = { status: 'idle' | 'checking' | 'ready' | 'failed'; items: T[]; hasMore: boolean };
const idle = <T,>(): DuplicateState<T> => ({ status: 'idle', items: [], hasMore: false });

function useCheck<T>(path: string, payload: object | null, key: string): DuplicateState<T> {
  const [state, setState] = useState<DuplicateState<T>>(idle);
  useEffect(() => {
    if (!payload) { setState(idle()); return; }
    let active = true;
    setState({ status: 'checking', items: [], hasMore: false });
    const timer = window.setTimeout(() => {
      void api<{ items: T[]; hasMore: boolean }>(path, { method: 'POST', body: JSON.stringify(payload) })
        .then(result => { if (active) setState({ status: 'ready', items: result.items, hasMore: result.hasMore }); })
        .catch(() => { if (active) setState({ status: 'failed', items: [], hasMore: false }); });
    }, 400);
    return () => { active = false; window.clearTimeout(timer); };
  // key represents the serialized request. Do not recheck on unrelated component renders.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [path, key]);
  return state;
}

export function useFarmerDuplicateCheck(name: string, contact: string, excludeId?: string, disabled = false) {
  const normalizedName = name.trim().replace(/\s+/g, ' ');
  const normalizedContact = contact.trim();
  const key = JSON.stringify([normalizedName, normalizedContact, excludeId]);
  return useCheck<FarmerDuplicate>('/api/farmers/duplicate-check',
    disabled || normalizedName.length < 2 ? null : { name: normalizedName, contact: normalizedContact || null, excludeId: excludeId ?? null },
    disabled ? '' : key);
}

export function usePlotDuplicateCheck(point: PlotPoint | null, excludeId?: string, disabled = false) {
  const key = JSON.stringify([point, excludeId, disabled]);
  return useCheck<PlotDuplicate>('/api/plots/duplicate-check',
    disabled || !point ? null : { geometry: { type: 'Point', coordinates: point }, excludeId: excludeId ?? null }, key);
}
