import { lazy, Suspense, useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { parsePlotPoint, type PlotPoint } from './plotCoordinates';
import { useCurrentLocation } from './useCurrentLocation';
import { PlotDuplicateNotice } from './DuplicateNotice';
import { usePlotDuplicateCheck } from './useDuplicateCheck';

const PlotMap = lazy(() => import('./PlotMap'));

type RegisterPlotRequest = {
  deviceId: string;
  clientOperationId: string;
  capturedAt: string;
  payloadSchemaVersion: 1;
  payload: {
    entityId: string;
    farmerId: string;
    code: string | null;
    geometry: { type: 'Point'; coordinates: [number, number] };
    areaHa: number;
  };
};

type CreatedPlot = { id: string };

function readPending(key: string, farmerId: string): RegisterPlotRequest | null {
  try {
    const raw = window.sessionStorage.getItem(key);
    if (!raw) return null;
    const request = JSON.parse(raw) as RegisterPlotRequest;
    return request.deviceId && request.clientOperationId && request.payload?.entityId && request.payload.farmerId === farmerId
      && request.payload.geometry?.type === 'Point' ? request : null;
  } catch { return null; }
}

function deviceId() {
  const key = 'grainledger:capture:device-id';
  try {
    const existing = window.localStorage.getItem(key);
    if (existing) return existing;
    const created = `web-${crypto.randomUUID()}`;
    window.localStorage.setItem(key, created);
    return created;
  } catch { return `web-${crypto.randomUUID()}`; }
}

export function PlotCreate({ farmerId, farmerName, userId, companyId, onCancel, onCreated }: {
  farmerId: string;
  farmerName: string;
  userId: string;
  companyId: string;
  onCancel: () => void;
  onCreated: (id: string) => void;
}) {
  const key = `grainledger:plot-create:${userId}:${companyId}:${farmerId}`;
  const [pending, setPending] = useState<RegisterPlotRequest | null>(() => readPending(key, farmerId));
  const [code, setCode] = useState(() => readPending(key, farmerId)?.payload.code ?? '');
  const [longitude, setLongitude] = useState(() => String(readPending(key, farmerId)?.payload.geometry.coordinates[0] ?? ''));
  const [latitude, setLatitude] = useState(() => String(readPending(key, farmerId)?.payload.geometry.coordinates[1] ?? ''));
  const [areaHa, setAreaHa] = useState(() => String(readPending(key, farmerId)?.payload.areaHa ?? ''));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const sending = useRef(false);
  const dialog = useRef<HTMLElement>(null);
  const location = useCurrentLocation();
  const selectedPoint = parsePlotPoint(longitude, latitude);
  const duplicates = usePlotDuplicateCheck(selectedPoint, undefined, !!pending || busy);

  useEffect(() => { dialog.current?.focus(); }, []);

  function selectPoint([lon, lat]: PlotPoint) {
    location.invalidate();
    setLongitude(String(lon));
    setLatitude(String(lat));
    setError('');
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    if (sending.current) return;
    const coordinates = parsePlotPoint(longitude, latitude);
    const area = Number(areaHa);
    if (!pending && (!longitude.trim() || !latitude.trim() || !areaHa.trim() ||
      !coordinates ||
      !Number.isFinite(area) || area <= 0 || area >= 1_000_000_000_000 ||
      !/^\d+(?:\.\d{1,12})?$/.test(areaHa.trim()))) {
      setError('Enter a longitude from -180 to 180, a latitude from -90 to 90, and a positive area in hectares (up to 12 decimal places).');
      return;
    }

    sending.current = true;
    setBusy(true);
    setError('');
    const request: RegisterPlotRequest = pending ?? {
      deviceId: deviceId(),
      clientOperationId: crypto.randomUUID(),
      capturedAt: new Date().toISOString(),
      payloadSchemaVersion: 1,
      payload: { entityId: crypto.randomUUID(), farmerId, code: code.trim() || null,
        geometry: { type: 'Point', coordinates: coordinates! }, areaHa: area },
    };
    if (!pending) {
      setPending(request);
      try { window.sessionStorage.setItem(key, JSON.stringify(request)); } catch { /* In-memory retry remains available. */ }
    }
    try {
      const plot = await api<CreatedPlot>('/api/plots', { method: 'POST', body: JSON.stringify(request) });
      try { window.sessionStorage.removeItem(key); } catch { /* Storage may be disabled. */ }
      setPending(null);
      onCreated(plot.id);
    } catch (cause) {
      if (cause instanceof ApiError && [400, 409].includes(cause.status)) {
        try { window.sessionStorage.removeItem(key); } catch { /* Storage may be disabled. */ }
        setPending(null);
      }
      setError(errorMessage(cause));
    } finally {
      sending.current = false;
      setBusy(false);
    }
  }

  return <div className="farmer-modal-backdrop">
    <section ref={dialog} tabIndex={-1} className="farmer-modal capture-modal-wide" role="dialog" aria-modal="true" aria-labelledby="add-plot-title" onKeyDown={event => { if (event.key === 'Escape' && !busy) onCancel(); }}>
      <div className="farmer-modal-heading"><div><span className="eyebrow">FIELD RECORDS</span><h2 id="add-plot-title">Add plot</h2></div><button className="farmer-modal-close" type="button" onClick={onCancel} disabled={busy} aria-label="Close add plot form">×</button></div>
      <p className="muted">Register a plot for <strong>{farmerName}</strong>. Choose a point or enter WGS84 coordinates. Check that the selected point is on the plot.</p>
      <form onSubmit={save}>
        <label htmlFor="new-plot-code">Plot code <span className="optional">(optional)</span><input id="new-plot-code" value={code} onChange={event => setCode(event.target.value)} maxLength={40} disabled={busy || !!pending} placeholder="Leave blank to generate a code" /></label>
        <div className="plot-map-heading">
          <div><strong>Plot location</strong><span>Click the map or drag the marker. The starting view is not a saved location.</span></div>
          <button type="button" className="secondary" onClick={() => location.locate(selectPoint)} disabled={busy || !!pending || location.locating}>{location.locating ? 'Finding location…' : 'Use device location'}</button>
        </div>
        <Suspense fallback={<div className="plot-map-loading" role="status">Loading map…</div>}>
          <PlotMap point={selectedPoint} onChange={selectPoint} disabled={busy || !!pending} />
        </Suspense>
        {location.locationError && <p className="plot-location-feedback" role="status">{location.locationError}</p>}
        {location.accuracy !== null && <p className="plot-location-feedback" role="status">Device estimate: within approximately {Math.round(location.accuracy)} m. Confirm this is the plot location.</p>}
        <div className="form-columns">
          <label htmlFor="new-plot-longitude">Longitude <span className="required-mark">*</span><input id="new-plot-longitude" type="number" inputMode="decimal" step="any" min="-180" max="180" value={longitude} onChange={event => { location.invalidate(); setLongitude(event.target.value); }} required disabled={busy || !!pending} placeholder="106.35" /></label>
          <label htmlFor="new-plot-latitude">Latitude <span className="required-mark">*</span><input id="new-plot-latitude" type="number" inputMode="decimal" step="any" min="-90" max="90" value={latitude} onChange={event => { location.invalidate(); setLatitude(event.target.value); }} required disabled={busy || !!pending} placeholder="10.4" /></label>
        </div>
        <PlotDuplicateNotice state={duplicates} />
        <label htmlFor="new-plot-area">Area (hectares) <span className="required-mark">*</span><input id="new-plot-area" type="number" inputMode="decimal" step="any" min="0.000000000001" value={areaHa} onChange={event => setAreaHa(event.target.value)} required disabled={busy || !!pending} placeholder="1.25" /></label>
        {pending && !busy && <div className="notice pending">The previous save did not return a result. Retry save sends the same plot request to prevent a duplicate.</div>}
        {error && <div className="notice error" role="alert">{error}</div>}
        <div className="form-footer"><button type="button" className="secondary" onClick={onCancel} disabled={busy}>Cancel</button><button type="submit" disabled={busy}>{busy ? 'Saving…' : pending ? 'Retry save' : 'Save plot'}</button></div>
      </form>
    </section>
  </div>;
}
