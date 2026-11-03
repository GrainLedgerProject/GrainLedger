import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { loadCropSeasons, type CropSeason } from './cropSeasons';
import { SpraySection } from './Spray';
import { RiceVarietyInput } from './RiceVarietyInput';

type PlotSeason = { id: string; plotId: string; seasonId: string; riceVariety: string; version?: number; canEdit?: boolean; canDelete?: boolean };
type Plot = { id: string; code: string; farmerId: string };

export function PlotSeasonDetail({ id, onBack, onOpenSpray, canCreate, userId, companyId }: { id: string; onBack: (plotId: string | null) => void; onOpenSpray: (id: string) => void; canCreate: boolean; userId: string; companyId: string }) {
  const [record, setRecord] = useState<PlotSeason | null>(null);
  const [plot, setPlot] = useState<Plot | null>(null);
  const [season, setSeason] = useState<CropSeason | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [retry, setRetry] = useState(0);
  const [seasons, setSeasons] = useState<CropSeason[]>([]);
  const [editOpen, setEditOpen] = useState(false); const [archiveOpen, setArchiveOpen] = useState(false);
  const [editSeasonId, setEditSeasonId] = useState(''); const [editVariety, setEditVariety] = useState('');
  const [actionError, setActionError] = useState(''); const [busy, setBusy] = useState(false); const [notice, setNotice] = useState('');

  function actionMessage(cause: unknown) {
    return cause instanceof ApiError && cause.status === 412 ? 'This plot season changed. Close the form and reload the record before trying again.' : errorMessage(cause);
  }
  async function save(event: FormEvent) {
    event.preventDefault(); if (!record?.version || busy) return;
    setBusy(true); setActionError('');
    try {
      await api(`/api/plot-seasons/${record.id}`, { method: 'PUT', body: JSON.stringify({ plotId: record.plotId, seasonId: editSeasonId, riceVariety: editVariety.trim(), expectedVersion: record.version }) });
      setEditOpen(false); setNotice('Plot season updated.'); setRetry(value => value + 1);
    } catch (cause) { setActionError(actionMessage(cause)); }
    finally { setBusy(false); }
  }
  async function archive() {
    if (!record?.version || busy) return;
    setBusy(true); setActionError('');
    try { await api(`/api/plot-seasons/${record.id}`, { method: 'DELETE', body: JSON.stringify({ version: record.version }) }); onBack(record.plotId); }
    catch (cause) { setActionError(actionMessage(cause)); }
    finally { setBusy(false); }
  }

  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    setRecord(null);
    setPlot(null);
    setSeason(null);
    setLoading(true);
    setError('');
    void api<PlotSeason>(`/api/plot-seasons/${encodeURIComponent(id)}`, { signal: controller.signal })
      .then(value => {
        if (!active) return;
        setRecord(value);
        void api<Plot>(`/api/plots/${encodeURIComponent(value.plotId)}`, { signal: controller.signal })
          .then(found => { if (active) setPlot(found); })
          .catch(() => { /* The plot-season record remains readable without its label. */ });
        void loadCropSeasons(controller.signal)
          .then(items => { if (active) { setSeasons(items); setSeason(items.find(item => item.id === value.seasonId) ?? null); } })
          .catch(() => { /* The season ID remains available if its catalog cannot be loaded. */ });
      })
      .catch(cause => { if (active) setError(errorMessage(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [id, retry]);

  return <>
    <div className="page-heading"><span className="eyebrow">CROP RECORDS</span><h1>Plot season</h1><p className="muted">The crop season and rice variety registered for this plot.</p></div>
    <button className="text-button" onClick={() => onBack(record?.plotId ?? null)}>← {record ? 'Back to plot' : 'Back to farmers'}</button>
    {notice && <div className="notice" role="status">{notice}</div>}
    {loading && <p role="status">Loading plot season…</p>}
    {error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(value => value + 1)}>Try again</button></div>}
    {record && <section className="panel plot-season-record">
      <div className="plot-season-record-hero"><span className="eyebrow">REGISTERED SEASON</span><h2>{season?.name ?? 'Crop season'}</h2><p className="muted">{plot ? `Plot ${plot.code}` : `Plot ID: ${record.plotId}`}</p><div className="admin-form-actions">{record.canEdit && <button className="secondary" type="button" onClick={() => { setEditSeasonId(record.seasonId); setEditVariety(record.riceVariety); setActionError(''); setEditOpen(true); }}>Edit registration</button>}{record.canDelete && <button className="secondary" type="button" onClick={() => { setActionError(''); setArchiveOpen(true); }}>Archive registration</button>}</div></div>
      <div className="plot-season-facts">
        <div><span>Rice variety</span><strong>{record.riceVariety}</strong></div>
        <div><span>Season</span><strong>{season?.name ?? record.seasonId}</strong></div>
        {season && <><div><span>Starts on</span><strong>{season.startsOn}</strong></div><div><span>Ends on</span><strong>{season.endsOn}</strong></div></>}
      </div>
    </section>}
    {record && <SpraySection plotSeasonId={record.id} canCreate={canCreate} userId={userId} companyId={companyId} onOpen={onOpenSpray} />}
    {record && editOpen && <div className="farmer-modal-backdrop"><section className="farmer-modal" role="dialog" aria-modal="true" aria-label="Edit plot season"><div className="farmer-modal-heading"><h2>Edit plot season</h2><button className="farmer-modal-close" type="button" disabled={busy} aria-label="Close" onClick={() => setEditOpen(false)}>×</button></div><form onSubmit={save}><label>Crop season<select value={editSeasonId} onChange={event => setEditSeasonId(event.target.value)} required disabled={busy}><option value="">Select a season</option>{seasons.map(item => <option key={item.id} value={item.id}>{item.name} ({item.startsOn} – {item.endsOn})</option>)}{editSeasonId && !seasons.some(item => item.id === editSeasonId) && <option value={editSeasonId}>Current season</option>}</select></label><RiceVarietyInput value={editVariety} onChange={setEditVariety} disabled={busy} />{actionError && <div className="notice error" role="alert">{actionError}</div>}<div className="admin-form-actions"><button className="secondary" type="button" disabled={busy} onClick={() => setEditOpen(false)}>Cancel</button><button disabled={busy || !editSeasonId || !editVariety.trim()}>{busy ? 'Saving…' : 'Save changes'}</button></div></form></section></div>}
    {record && archiveOpen && <div className="farmer-modal-backdrop"><section className="farmer-modal" role="dialog" aria-modal="true" aria-label="Archive plot season"><div className="farmer-modal-heading"><h2>Archive plot season</h2><button className="farmer-modal-close" type="button" disabled={busy} aria-label="Close" onClick={() => setArchiveOpen(false)}>×</button></div><p>Archive the registration for plot <strong>{plot?.code ?? record.plotId}</strong>? Historical records remain available.</p>{actionError && <div className="notice error" role="alert">{actionError}</div>}<div className="admin-form-actions"><button className="secondary" type="button" disabled={busy} onClick={() => setArchiveOpen(false)}>Cancel</button><button type="button" disabled={busy} onClick={() => void archive()}>{busy ? 'Archiving…' : 'Archive registration'}</button></div></section></div>}
  </>;
}
