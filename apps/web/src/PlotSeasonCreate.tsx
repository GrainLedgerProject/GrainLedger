import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { loadCropSeasons, type CropSeason } from './cropSeasons';

type RegisterPlotSeasonRequest = {
  deviceId: string;
  clientOperationId: string;
  capturedAt: string;
  payloadSchemaVersion: 1;
  payload: { entityId: string; plotId: string; seasonId: string; riceVariety: string };
};

function readPending(key: string, plotId: string): RegisterPlotSeasonRequest | null {
  try {
    const raw = window.sessionStorage.getItem(key);
    if (!raw) return null;
    const request = JSON.parse(raw) as RegisterPlotSeasonRequest;
    return request.deviceId && request.clientOperationId && request.payload?.entityId &&
      request.payload.plotId === plotId && request.payload.seasonId && request.payload.riceVariety
      ? request : null;
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

export function PlotSeasonCreate({ plotId, plotCode, userId, companyId, onCancel, onCreated }: {
  plotId: string;
  plotCode: string;
  userId: string;
  companyId: string;
  onCancel: () => void;
  onCreated: (id: string) => void;
}) {
  const key = `grainledger:plot-season-create:${userId}:${companyId}:${plotId}`;
  const [pending, setPending] = useState<RegisterPlotSeasonRequest | null>(() => readPending(key, plotId));
  const [seasonId, setSeasonId] = useState(() => readPending(key, plotId)?.payload.seasonId ?? '');
  const [riceVariety, setRiceVariety] = useState(() => readPending(key, plotId)?.payload.riceVariety ?? '');
  const [seasonSearch, setSeasonSearch] = useState('');
  const [seasons, setSeasons] = useState<CropSeason[]>([]);
  const [seasonsLoading, setSeasonsLoading] = useState(true);
  const [seasonsError, setSeasonsError] = useState('');
  const [seasonsRetry, setSeasonsRetry] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const sending = useRef(false);
  const seasonInput = useRef<HTMLSelectElement>(null);

  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    setSeasonsLoading(true);
    setSeasonsError('');
    void loadCropSeasons(controller.signal)
      .then(value => { if (active) setSeasons(value); })
      .catch(cause => { if (active) setSeasonsError(errorMessage(cause)); })
      .finally(() => { if (active) setSeasonsLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [seasonsRetry]);

  useEffect(() => { if (!seasonsLoading && !pending) seasonInput.current?.focus(); }, [seasonsLoading, pending]);

  async function save(event: FormEvent) {
    event.preventDefault();
    if (sending.current || (!pending && (!seasonId || !riceVariety.trim() || !seasons.some(item => item.id === seasonId)))) return;
    sending.current = true;
    setBusy(true);
    setError('');
    const request: RegisterPlotSeasonRequest = pending ?? {
      deviceId: deviceId(),
      clientOperationId: crypto.randomUUID(),
      capturedAt: new Date().toISOString(),
      payloadSchemaVersion: 1,
      payload: { entityId: crypto.randomUUID(), plotId, seasonId, riceVariety: riceVariety.trim() },
    };
    if (!pending) {
      setPending(request);
      try { window.sessionStorage.setItem(key, JSON.stringify(request)); } catch { /* In-memory retry remains available. */ }
    }
    try {
      const created = await api<{ id: string }>('/api/plot-seasons', { method: 'POST', body: JSON.stringify(request) });
      try { window.sessionStorage.removeItem(key); } catch { /* Storage may be disabled. */ }
      setPending(null);
      onCreated(created.id);
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

  const visibleSeasons = seasons.filter(item => item.name.toLocaleLowerCase().includes(seasonSearch.trim().toLocaleLowerCase()) || item.id === seasonId);

  return <div className="farmer-modal-backdrop">
    <section className="farmer-modal" role="dialog" aria-modal="true" aria-labelledby="register-plot-season-title" onKeyDown={event => { if (event.key === 'Escape' && !busy) onCancel(); }}>
      <div className="farmer-modal-heading"><div><span className="eyebrow">CROP RECORDS</span><h2 id="register-plot-season-title">Register crop season</h2></div><button className="farmer-modal-close" type="button" onClick={onCancel} disabled={busy} aria-label="Close crop season form">×</button></div>
      <p className="muted">Choose an existing company season for plot <strong>{plotCode}</strong>, then enter the rice variety grown in that season.</p>
      {seasonsLoading && <p role="status">Loading company crop seasons…</p>}
      {seasonsError && <div className="notice error" role="alert">{seasonsError} <button className="text-button" onClick={() => setSeasonsRetry(value => value + 1)}>Try again</button></div>}
      {!seasonsLoading && !seasonsError && seasons.length === 0 && <div className="notice pending">No crop seasons are configured for your company. Ask an administrator to add one before registering this plot.</div>}
      <form onSubmit={save}>
        {seasons.length > 10 && <label htmlFor="crop-season-search">Find a crop season<input id="crop-season-search" value={seasonSearch} onChange={event => setSeasonSearch(event.target.value)} disabled={busy || !!pending} placeholder="Search by season name" /></label>}
        <label htmlFor="new-plot-season">Crop season <span className="required-mark">*</span><select ref={seasonInput} id="new-plot-season" value={seasonId} onChange={event => setSeasonId(event.target.value)} required disabled={busy || !!pending || seasonsLoading || seasons.length === 0}>
          <option value="">Select a season</option>
          {visibleSeasons.map(item => <option key={item.id} value={item.id}>{item.name} ({item.startsOn} – {item.endsOn})</option>)}
        </select></label>
        <label htmlFor="new-rice-variety">Rice variety <span className="required-mark">*</span><input id="new-rice-variety" value={riceVariety} onChange={event => setRiceVariety(event.target.value)} required disabled={busy || !!pending} placeholder="e.g. ST25" /></label>
        {pending && <div className="notice pending">The request may already have reached the server. Retry sends the same registration to prevent a duplicate.</div>}
        {error && <div className="notice error" role="alert">{error}</div>}
        <div className="form-footer"><button type="button" className="secondary" onClick={onCancel} disabled={busy}>Cancel</button><button type="submit" disabled={busy || (!pending && (seasonsLoading || seasons.length === 0 || !seasonId || !riceVariety.trim()))}>{busy ? 'Saving…' : pending ? 'Retry save' : 'Register season'}</button></div>
      </form>
    </section>
  </div>;
}
