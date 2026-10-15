import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';

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
  const longitudeInput = useRef<HTMLInputElement>(null);

  useEffect(() => { longitudeInput.current?.focus(); }, []);

  async function save(event: FormEvent) {
    event.preventDefault();
    if (sending.current) return;
    const lon = Number(longitude);
    const lat = Number(latitude);
    const area = Number(areaHa);
    if (!pending && (!longitude.trim() || !latitude.trim() || !areaHa.trim() ||
      !Number.isFinite(lon) || lon < -180 || lon > 180 ||
      !Number.isFinite(lat) || lat < -90 || lat > 90 ||
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
        geometry: { type: 'Point', coordinates: [lon, lat] }, areaHa: area },
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
    <section className="farmer-modal" role="dialog" aria-modal="true" aria-labelledby="add-plot-title" onKeyDown={event => { if (event.key === 'Escape' && !busy) onCancel(); }}>
      <div className="farmer-modal-heading"><div><span className="eyebrow">FIELD RECORDS</span><h2 id="add-plot-title">Add plot</h2></div><button className="farmer-modal-close" type="button" onClick={onCancel} disabled={busy} aria-label="Close add plot form">×</button></div>
      <p className="muted">Register a plot for <strong>{farmerName}</strong>. Coordinates use WGS84; enter longitude before latitude.</p>
      <form onSubmit={save}>
        <label htmlFor="new-plot-code">Plot code <span className="optional">(optional)</span><input id="new-plot-code" value={code} onChange={event => setCode(event.target.value)} maxLength={40} disabled={busy || !!pending} placeholder="Leave blank to generate a code" /></label>
        <div className="form-columns">
          <label htmlFor="new-plot-longitude">Longitude <span className="required-mark">*</span><input ref={longitudeInput} id="new-plot-longitude" type="number" inputMode="decimal" step="any" min="-180" max="180" value={longitude} onChange={event => setLongitude(event.target.value)} required disabled={busy || !!pending} placeholder="106.35" /></label>
          <label htmlFor="new-plot-latitude">Latitude <span className="required-mark">*</span><input id="new-plot-latitude" type="number" inputMode="decimal" step="any" min="-90" max="90" value={latitude} onChange={event => setLatitude(event.target.value)} required disabled={busy || !!pending} placeholder="10.4" /></label>
        </div>
        <label htmlFor="new-plot-area">Area (hectares) <span className="required-mark">*</span><input id="new-plot-area" type="number" inputMode="decimal" step="any" min="0.000000000001" value={areaHa} onChange={event => setAreaHa(event.target.value)} required disabled={busy || !!pending} placeholder="1.25" /></label>
        {pending && <div className="notice pending">The request may already have reached the server. Retry sends the same plot request to prevent a duplicate.</div>}
        {error && <div className="notice error" role="alert">{error}</div>}
        <div className="form-footer"><button type="button" className="secondary" onClick={onCancel} disabled={busy}>Cancel</button><button type="submit" disabled={busy}>{busy ? 'Saving…' : pending ? 'Retry save' : 'Save plot'}</button></div>
      </form>
    </section>
  </div>;
}
