import { useEffect, useState } from 'react';
import { api, errorMessage } from './api';
import { loadCropSeasons, type CropSeason } from './cropSeasons';
import { SpraySection } from './Spray';

type PlotSeason = { id: string; plotId: string; seasonId: string; riceVariety: string };
type Plot = { id: string; code: string; farmerId: string };

export function PlotSeasonDetail({ id, onBack, onOpenSpray, canCreate, userId, companyId }: { id: string; onBack: (plotId: string | null) => void; onOpenSpray: (id: string) => void; canCreate: boolean; userId: string; companyId: string }) {
  const [record, setRecord] = useState<PlotSeason | null>(null);
  const [plot, setPlot] = useState<Plot | null>(null);
  const [season, setSeason] = useState<CropSeason | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [retry, setRetry] = useState(0);

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
          .then(seasons => { if (active) setSeason(seasons.find(item => item.id === value.seasonId) ?? null); })
          .catch(() => { /* The season ID remains available if its catalog cannot be loaded. */ });
      })
      .catch(cause => { if (active) setError(errorMessage(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [id, retry]);

  return <>
    <div className="page-heading"><span className="eyebrow">CROP RECORDS</span><h1>Plot season</h1><p className="muted">The crop season and rice variety registered for this plot.</p></div>
    <button className="text-button" onClick={() => onBack(record?.plotId ?? null)}>← {record ? 'Back to plot' : 'Back to farmers'}</button>
    {loading && <p role="status">Loading plot season…</p>}
    {error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(value => value + 1)}>Try again</button></div>}
    {record && <section className="panel plot-season-record">
      <div className="plot-season-record-hero"><span className="eyebrow">REGISTERED SEASON</span><h2>{season?.name ?? 'Crop season'}</h2><p className="muted">{plot ? `Plot ${plot.code}` : `Plot ID: ${record.plotId}`}</p></div>
      <div className="plot-season-facts">
        <div><span>Rice variety</span><strong>{record.riceVariety}</strong></div>
        <div><span>Season</span><strong>{season?.name ?? record.seasonId}</strong></div>
        {season && <><div><span>Starts on</span><strong>{season.startsOn}</strong></div><div><span>Ends on</span><strong>{season.endsOn}</strong></div></>}
      </div>
    </section>}
    {record && <SpraySection plotSeasonId={record.id} canCreate={canCreate} userId={userId} companyId={companyId} onOpen={onOpenSpray} />}
  </>;
}
