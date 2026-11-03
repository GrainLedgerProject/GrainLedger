import { lazy, Suspense, useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { parsePlotPoint, type PlotPoint } from './plotCoordinates';
import { PlotDuplicateNotice } from './DuplicateNotice';
import { usePlotDuplicateCheck } from './useDuplicateCheck';

const PlotMap = lazy(() => import('./PlotMap'));

type PlotGeometry = { type: 'Point'; coordinates: [number, number] } |
  { type: 'Polygon'; coordinates: number[][][] };
type PlotReference = {
  id: string;
  farmerId: string;
  code: string;
  geometry: PlotGeometry;
  areaHa: number;
  version: number;
  canEdit: boolean;
};

export function PlotEdit({ id, onCancel, onUpdated }: {
  id: string;
  onCancel: () => void;
  onUpdated: () => void;
}) {
  const [original, setOriginal] = useState<PlotReference | null>(null);
  const [longitude, setLongitude] = useState('');
  const [latitude, setLatitude] = useState('');
  const [areaHa, setAreaHa] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const sending = useRef(false);
  const dialog = useRef<HTMLElement>(null);

  useEffect(() => {
    dialog.current?.focus();
    const controller = new AbortController();
    void api<PlotReference>(`/api/plots/${encodeURIComponent(id)}`, { signal: controller.signal })
      .then(value => {
        setOriginal(value);
        setAreaHa(String(value.areaHa));
        if (value.geometry.type === 'Point') {
          setLongitude(String(value.geometry.coordinates[0]));
          setLatitude(String(value.geometry.coordinates[1]));
        }
        if (!value.canEdit) setError('You cannot edit this plot record.');
      })
      .catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [id]);

  const point = original?.geometry.type === 'Point' ? parsePlotPoint(longitude, latitude) : null;
  const polygon = original?.geometry.type === 'Polygon' ? original.geometry.coordinates : null;
  const duplicates = usePlotDuplicateCheck(point, id, loading || busy || !original?.canEdit);

  function selectPoint([lon, lat]: PlotPoint) {
    setLongitude(String(lon));
    setLatitude(String(lat));
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    if (sending.current || !original?.canEdit) return;
    const nextPoint = original.geometry.type === 'Point' ? parsePlotPoint(longitude, latitude) : null;
    const area = Number(areaHa);
    if ((original.geometry.type === 'Point' && !nextPoint) || !areaHa.trim() ||
      !Number.isFinite(area) || area <= 0 || area >= 1_000_000_000_000 ||
      !/^\d+(?:\.\d{1,12})?$/.test(areaHa.trim())) {
      setError('Enter valid WGS84 coordinates and a positive area in hectares (up to 12 decimal places).');
      return;
    }
    const geometry: PlotGeometry = nextPoint ? { type: 'Point', coordinates: nextPoint } : original.geometry;
    sending.current = true;
    setBusy(true);
    setError('');
    try {
      await api<PlotReference>(`/api/plots/${encodeURIComponent(id)}`, {
        method: 'PUT', body: JSON.stringify({
          farmerId: original.farmerId, code: original.code, geometry, areaHa: area,
          expectedVersion: original.version,
        }),
      });
      onUpdated();
    } catch (cause) {
      if (cause instanceof ApiError && cause.code === 'REFERENCE_IN_USE') {
        setError('This plot already has a crop season and cannot be edited.');
      } else if (cause instanceof ApiError && cause.code === 'REFERENCE_STALE') {
        setError('This plot changed while you were editing. Close this form, reopen it, and review the current values.');
      } else if (!(cause instanceof ApiError)) {
        try {
          const latest = await api<PlotReference>(`/api/plots/${encodeURIComponent(id)}`);
          if (latest.version === original.version + 1 && latest.areaHa === area &&
              JSON.stringify(latest.geometry) === JSON.stringify(geometry)) {
            onUpdated();
            return;
          }
        } catch { /* Keep the draft if the readback also fails. */ }
        setError('The save result is unknown. Close and reopen this form to check the latest record before trying again.');
      } else setError(errorMessage(cause));
    } finally {
      sending.current = false;
      setBusy(false);
    }
  }

  return <div className="farmer-modal-backdrop">
    <section ref={dialog} tabIndex={-1} className="farmer-modal capture-modal-wide" role="dialog" aria-modal="true" aria-labelledby="edit-plot-title"
      onKeyDown={event => { if (event.key === 'Escape' && !busy) onCancel(); }}>
      <div className="farmer-modal-heading"><div><span className="eyebrow">FIELD RECORDS</span><h2 id="edit-plot-title">Edit plot</h2></div><button className="farmer-modal-close" type="button" onClick={onCancel} disabled={busy} aria-label="Close edit plot form">×</button></div>
      {loading && <p role="status">Loading current plot record…</p>}
      {original && <form onSubmit={save}>
        <p className="muted">Plot code: <strong>{original.code}</strong>. Linked farmer stays unchanged.</p>
        {original.geometry.type === 'Point' && <>
          <p className="muted">Click the map, drag the marker, or edit the coordinates.</p>
          <Suspense fallback={<div className="plot-map-loading" role="status">Loading map…</div>}>
            <PlotMap point={point} onChange={selectPoint} disabled={busy || !original.canEdit} />
          </Suspense>
          <div className="form-columns">
            <label htmlFor="edit-plot-longitude">Longitude <span className="required-mark">*</span><input id="edit-plot-longitude" type="number" inputMode="decimal" step="any" min="-180" max="180" value={longitude} onChange={event => setLongitude(event.target.value)} required disabled={busy || !original.canEdit} /></label>
            <label htmlFor="edit-plot-latitude">Latitude <span className="required-mark">*</span><input id="edit-plot-latitude" type="number" inputMode="decimal" step="any" min="-90" max="90" value={latitude} onChange={event => setLatitude(event.target.value)} required disabled={busy || !original.canEdit} /></label>
          </div>
          <PlotDuplicateNotice state={duplicates} />
        </>}
        {original.geometry.type === 'Polygon' && <>
          <p className="notice pending">The recorded boundary is preserved. Boundary drawing is not available in this form.</p>
          <Suspense fallback={<div className="plot-map-loading" role="status">Loading map…</div>}>
            <PlotMap point={null} polygon={polygon} disabled />
          </Suspense>
        </>}
        <label htmlFor="edit-plot-area">Area (hectares) <span className="required-mark">*</span><input id="edit-plot-area" type="number" inputMode="decimal" step="any" min="0.000000000001" value={areaHa} onChange={event => setAreaHa(event.target.value)} required disabled={busy || !original.canEdit} /></label>
        {error && <div className="notice error" role="alert">{error}</div>}
        <div className="form-footer"><button type="button" className="secondary" onClick={onCancel} disabled={busy}>Cancel</button><button type="submit" disabled={busy || !original.canEdit}>{busy ? 'Saving…' : 'Save changes'}</button></div>
      </form>}
      {!original && error && <div className="notice error" role="alert">{error}</div>}
    </section>
  </div>;
}
