import type { FarmerDuplicate, PlotDuplicate } from './useDuplicateCheck';

type State<T> = { status: 'idle' | 'checking' | 'ready' | 'failed'; items: T[]; hasMore: boolean };

export function FarmerDuplicateNotice({ state }: { state: State<FarmerDuplicate> }) {
  if (state.status === 'idle') return null;
  if (state.status === 'checking') return <p className="duplicate-check" role="status">Checking for similar farmers…</p>;
  if (state.status === 'failed') return <div className="notice pending" role="status">Could not check for similar farmers. You can still save, but check the farmer list first.</div>;
  if (!state.items.length) return null;
  return <div className="notice pending duplicate-notice" role="status">
    <strong>Possible existing farmer{state.items.length > 1 ? 's' : ''}</strong>
    <ul>{state.items.map(item => <li key={item.id}><a href={`/capture/farmers/${item.id}`} target="_blank" rel="noopener noreferrer">{item.name} ({item.code})</a>{item.contact ? ` · ${item.contact}` : ''} · {item.reasons.includes('SAME_NAME') ? 'same name' : 'same contact'}</li>)}</ul>
    {state.hasMore && <p>More matches may exist in the farmer list.</p>}
    <p>Check these records before saving. This warning does not block registration.</p>
  </div>;
}

export function PlotDuplicateNotice({ state }: { state: State<PlotDuplicate> }) {
  if (state.status === 'idle') return null;
  if (state.status === 'checking') return <p className="duplicate-check" role="status">Checking nearby plot points…</p>;
  if (state.status === 'failed') return <div className="notice pending" role="status">Could not check nearby plot points. You can still save, but verify the location first.</div>;
  if (!state.items.length) return null;
  return <div className="notice pending duplicate-notice" role="status">
    <strong>Possible nearby plot{state.items.length > 1 ? 's' : ''} (within 20 m)</strong>
    <ul>{state.items.map(item => <li key={item.id}><a href={`/capture/plots/${item.id}`} target="_blank" rel="noopener noreferrer">{item.code}</a> · about {item.distanceMeters} m away</li>)}</ul>
    {state.hasMore && <p>More nearby points may exist.</p>}
    <p>This compares saved points only, not plot boundaries. Confirm the location before saving.</p>
  </div>;
}
