import { lazy, Suspense, useEffect, useState } from 'react';
import { api, errorMessage } from './api';
import { loadCropSeasons, type CropSeason } from './cropSeasons';
import { PlotSeasonCreate } from './PlotSeasonCreate';
import { PlotEdit } from './PlotEdit';

type Point = { type: 'Point'; coordinates: [number, number] };
type Polygon = { type: 'Polygon'; coordinates: number[][][] };
type Plot = { id: string; farmerId: string; code: string; areaHa: number; geometry: Point | Polygon; canEdit?: boolean };
type Farmer = { id: string; name: string; code: string };
type PlotSeason = { id: string; plotId: string; seasonId: string; riceVariety: string };
type PlotSeasonList = { items: PlotSeason[]; page: number; pageSize: number; hasMore: boolean };
const PlotMap = lazy(() => import('./PlotMap'));

export function PlotDetail({ id, onBack, onOpenPlotSeason, canCreate, userId, companyId }: { id: string; onBack: (farmerId: string | null) => void; onOpenPlotSeason: (id: string) => void; canCreate: boolean; userId: string; companyId: string }) {
  const [plot, setPlot] = useState<Plot | null>(null);
  const [farmer, setFarmer] = useState<Farmer | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [retry, setRetry] = useState(0);
  const [plotSeasons, setPlotSeasons] = useState<PlotSeasonList | null>(null);
  const [plotSeasonsPage, setPlotSeasonsPage] = useState(1);
  const [plotSeasonsRetry, setPlotSeasonsRetry] = useState(0);
  const [plotSeasonsLoading, setPlotSeasonsLoading] = useState(false);
  const [plotSeasonsError, setPlotSeasonsError] = useState('');
  const [seasonNames, setSeasonNames] = useState<Map<string, CropSeason>>(new Map());
  const [seasonNamesError, setSeasonNamesError] = useState('');
  const [showSeasonCreate, setShowSeasonCreate] = useState(false);
  const [showPlotEdit, setShowPlotEdit] = useState(false);

  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    setLoading(true);
    setPlot(null);
    setFarmer(null);
    setError('');
    void api<Plot>(`/api/plots/${encodeURIComponent(id)}`, { signal: controller.signal })
      .then(async value => {
        if (!active) return;
        setPlot(value);
        try {
          const owner = await api<Farmer>(`/api/farmers/${encodeURIComponent(value.farmerId)}`, { signal: controller.signal });
          if (active) setFarmer(owner);
        } catch { /* Plot remains usable if the farmer request fails. */ }
      })
      .catch(cause => { if (active) setError(errorMessage(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [id, retry]);

  useEffect(() => { setPlotSeasonsPage(1); }, [id]);

  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    setPlotSeasons(null);
    setPlotSeasonsLoading(true);
    setPlotSeasonsError('');
    const params = new URLSearchParams({ plotId: id, page: String(plotSeasonsPage), pageSize: '10' });
    void api<PlotSeasonList>(`/api/plot-seasons?${params}`, { signal: controller.signal })
      .then(value => { if (active) setPlotSeasons(value); })
      .catch(cause => { if (active) setPlotSeasonsError(errorMessage(cause)); })
      .finally(() => { if (active) setPlotSeasonsLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [id, plotSeasonsPage, plotSeasonsRetry]);

  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    setSeasonNames(new Map());
    setSeasonNamesError('');
    void loadCropSeasons(controller.signal)
      .then(value => { if (active) setSeasonNames(new Map(value.map(season => [season.id, season]))); })
      .catch(cause => { if (active) setSeasonNamesError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, [id]);

  const point = plot?.geometry?.type === 'Point' && Array.isArray(plot.geometry.coordinates) &&
    plot.geometry.coordinates.length === 2 && plot.geometry.coordinates.every(Number.isFinite) &&
    Math.abs(plot.geometry.coordinates[0]) <= 180 && Math.abs(plot.geometry.coordinates[1]) <= 90
    ? plot.geometry.coordinates : null;
  const polygon = plot?.geometry?.type === 'Polygon' && Array.isArray(plot.geometry.coordinates) &&
    plot.geometry.coordinates.length > 0 && plot.geometry.coordinates.every(ring => Array.isArray(ring) &&
      ring.length >= 4 && ring.every(vertex => Array.isArray(vertex) && vertex.length >= 2 &&
        Number.isFinite(vertex[0]) && Number.isFinite(vertex[1]) &&
        Math.abs(vertex[0]) <= 180 && Math.abs(vertex[1]) <= 90))
    ? plot.geometry.coordinates : null;

  return <>
    <div className="page-heading"><span className="eyebrow">FIELD RECORDS</span><h1>Plot record</h1><p className="muted">Location and area recorded for this plot.</p></div>
    <button className="text-button" onClick={() => onBack(plot?.farmerId ?? null)}>← {plot ? 'Back to farmer' : 'Back to farmers'}</button>
    {loading && <p role="status">Loading plot…</p>}
    {error && <div className="notice error" role="alert">{error} <button className="text-button" onClick={() => setRetry(value => value + 1)}>Try again</button></div>}
    {plot && <>
      <section className="panel plot-record">
        <div className="plot-record-hero"><div><span className="eyebrow">REGISTERED PLOT</span><h2>{plot.code}</h2><p className="muted">{farmer ? `Farmer: ${farmer.name} · ${farmer.code}` : 'Linked farmer record'}</p></div><div className="plot-hero-actions"><span className="plot-area-badge">{plot.areaHa} ha</span>{plot.canEdit && <button className="secondary" onClick={() => setShowPlotEdit(true)}>Edit plot</button>}</div></div>
        <div className="plot-record-facts">
          <div><span>Location type</span><strong>{point ? 'Point' : polygon ? 'Boundary polygon' : 'Unknown'}</strong></div>
          <div><span>Area</span><strong>{plot.areaHa} hectares</strong></div>
        </div>
      </section>
      <section className="panel plot-location">
        <span className="eyebrow">LOCATION</span><h2>Plot location</h2>
        <p className="muted small">WGS84 coordinates. Longitude is shown before latitude.</p>
        {(point || polygon) && <Suspense fallback={<div className="plot-map-loading" role="status">Loading map…</div>}>
          <PlotMap point={point} polygon={polygon} disabled />
        </Suspense>}
        {!point && !polygon && <p role="status">The location cannot be displayed as a supported point or boundary.</p>}
        {point && <div className="plot-coordinates"><div><span>Longitude</span><strong>{point[0]}</strong></div><div><span>Latitude</span><strong>{point[1]}</strong></div></div>}
        {polygon && <><p>This boundary contains {polygon.length} {polygon.length === 1 ? 'ring' : 'rings'} and {polygon.reduce((count, ring) => count + ring.length - 1, 0)} vertices.</p><details><summary>View boundary coordinates (GeoJSON)</summary><pre className="plot-geojson">{JSON.stringify(plot.geometry, null, 2)}</pre></details></>}
      </section>
      <section className="panel plot-seasons">
        <div className="plot-seasons-heading"><div><span className="eyebrow">CROP RECORDS</span><h2>Crop seasons</h2><p className="muted">Seasons registered for this plot and their rice varieties.</p></div>{canCreate && <button onClick={() => setShowSeasonCreate(true)}>+ Register season</button>}</div>
        {plotSeasonsLoading && <p role="status">Loading crop seasons…</p>}
        {plotSeasonsError && <div className="notice error" role="alert">{plotSeasonsError} <button className="text-button" onClick={() => setPlotSeasonsRetry(value => value + 1)}>Try again</button></div>}
        {seasonNamesError && <p className="muted small" role="status">Season names could not be loaded. Their IDs are shown instead.</p>}
        {plotSeasons && (plotSeasons.items.length === 0 ? <p className="empty">No crop seasons registered for this plot yet.</p> : <div className="table-scroll"><table><thead><tr><th scope="col">Crop season</th><th scope="col">Rice variety</th></tr></thead><tbody>{plotSeasons.items.map(item => <tr key={item.id}><td><button className="text-button farmer-plot-link" onClick={() => onOpenPlotSeason(item.id)}>{seasonNames.get(item.seasonId)?.name ?? item.seasonId} <span aria-hidden="true">↗</span></button></td><td>{item.riceVariety}</td></tr>)}</tbody></table></div>)}
        {plotSeasons && <div className="farmer-pagination"><span>Page {plotSeasons.page}</span><button className="secondary" disabled={plotSeasonsPage === 1 || plotSeasonsLoading} onClick={() => setPlotSeasonsPage(value => value - 1)}>Previous</button><button className="secondary" disabled={!plotSeasons.hasMore || plotSeasonsLoading} onClick={() => setPlotSeasonsPage(value => value + 1)}>Next</button></div>}
      </section>
      {showSeasonCreate && canCreate && <PlotSeasonCreate plotId={plot.id} plotCode={plot.code} userId={userId} companyId={companyId} onCancel={() => setShowSeasonCreate(false)} onCreated={createdId => { setShowSeasonCreate(false); onOpenPlotSeason(createdId); }} />}
      {showPlotEdit && <PlotEdit id={plot.id} onCancel={() => setShowPlotEdit(false)} onUpdated={() => { setShowPlotEdit(false); setRetry(value => value + 1); }} />}
    </>}
  </>;
}
