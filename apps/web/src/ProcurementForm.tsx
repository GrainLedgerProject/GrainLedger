import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { discardPending, newRequest, pendingRequest, submitCapture, wasRejected, type Envelope } from './captureWrite';
import { loadAllPages, type FarmerOption, type Page, type PlotOption, type PlotSeasonOption, type Procurement, type ProcurementReference, type ReceivingOption } from './procurementTypes';

type LineDraft = { plotSeasonId: string; measuredValue: string; measuredUnit: string; moisturePercent: string };
type LinePayload = LineDraft & { itemId: string; lineNo: number; lotId: string; sourceLotId: string };
type ProcurementPayload = { entityId: string; farmerId: string; collectionSiteId: string; receivingLocationId: string; commodityId: string; receivedAt: string; items: LinePayload[] };
function localDateTime(value: string) { const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16); }
const decimal = /^\d+(?:\.\d{1,12})?$/;
type FieldErrors = Record<string, string>;

export function ProcurementForm({ userId, companyId, onBack, onCreated }: { userId: string; companyId: string; onBack: () => void; onCreated: (id: string) => void }) {
  const key = `grainledger:procurement-create:${userId}:${companyId}`;
  const [pending, setPending] = useState<Envelope<ProcurementPayload> | null>(() => pendingRequest<ProcurementPayload>(key));
  const [farmers, setFarmers] = useState<FarmerOption[]>([]);
  const [farmerSearch, setFarmerSearch] = useState('');
  const [farmerId, setFarmerId] = useState(pending?.payload.farmerId ?? '');
  const [plots, setPlots] = useState<PlotOption[]>([]);
  const [plotSeasons, setPlotSeasons] = useState<PlotSeasonOption[]>([]);
  const [sourceError, setSourceError] = useState('');
  const [sourceRetry, setSourceRetry] = useState(0);
  const [reference, setReference] = useState<ProcurementReference | null>(null);
  const [receivingOptions, setReceivingOptions] = useState<ReceivingOption[]>([]);
  const [referenceError, setReferenceError] = useState('');
  const [referenceRetry, setReferenceRetry] = useState(0);
  const [siteId, setSiteId] = useState(pending?.payload.collectionSiteId ?? '');
  const [locationId, setLocationId] = useState(pending?.payload.receivingLocationId ?? '');
  const [receivedAt, setReceivedAt] = useState(pending ? localDateTime(pending.payload.receivedAt) : localDateTime(new Date().toISOString()));
  const [lines, setLines] = useState<LineDraft[]>(pending?.payload.items.map(item => ({ plotSeasonId: item.plotSeasonId, measuredValue: item.measuredValue, measuredUnit: item.measuredUnit, moisturePercent: item.moisturePercent })) ?? [{ plotSeasonId: '', measuredValue: '', measuredUnit: 'KG', moisturePercent: '' }]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [showValidation, setShowValidation] = useState(false);

  useEffect(() => {
    let active = true; const controller = new AbortController(); setSourceError('');
    void loadAllPages(page => api<Page<FarmerOption>>(`/api/farmers?page=${page}&pageSize=100`, { signal: controller.signal }))
      .then(value => { if (active) setFarmers(value); }).catch(cause => { if (active) setSourceError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, [sourceRetry]);

  useEffect(() => {
    let active = true; const controller = new AbortController(); setReferenceError('');
    void (async () => {
      const options: ReceivingOption[] = [];
      let first: ProcurementReference | null = null;
      for (let page = 1; page <= 100; page++) {
        const result = await api<ProcurementReference>(`/api/procurement-reference-data?page=${page}&pageSize=100`, { signal: controller.signal });
        first ??= result; options.push(...result.receivingOptions);
        if (!result.hasMore) return { first, options };
      }
      throw new Error('Too many receiving locations to load.');
    })().then(value => { if (active) { setReference(value.first); setReceivingOptions(value.options); } })
      .catch(cause => { if (active) setReferenceError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, [referenceRetry]);

  useEffect(() => {
    if (!farmerId) { setPlots([]); setPlotSeasons([]); return; }
    let active = true; const controller = new AbortController(); setSourceError(''); setPlotSeasons([]);
    void (async () => {
      const farmerPlots = await loadAllPages(page => api<Page<PlotOption>>(`/api/plots?farmerId=${encodeURIComponent(farmerId)}&page=${page}&pageSize=100`, { signal: controller.signal }));
      const plotIds = new Set(farmerPlots.map(plot => plot.id));
      const allSeasons = await loadAllPages(page => api<Page<PlotSeasonOption>>(`/api/plot-seasons?page=${page}&pageSize=100`, { signal: controller.signal }));
      return { farmerPlots, farmerSeasons: allSeasons.filter(item => plotIds.has(item.plotId)) };
    })().then(value => { if (active) { setPlots(value.farmerPlots); setPlotSeasons(value.farmerSeasons); } })
      .catch(cause => { if (active) setSourceError(errorMessage(cause)); });
    return () => { active = false; controller.abort(); };
  }, [farmerId, sourceRetry]);

  const sites = [...new Map(receivingOptions.map(option => [option.collectionSiteId, option])).values()];
  const locations = receivingOptions.filter(option => option.collectionSiteId === siteId);
  const visibleFarmers = farmers.filter(item => `${item.name} ${item.code}`.toLocaleLowerCase().includes(farmerSearch.toLocaleLowerCase()) || item.id === farmerId);
  const fieldErrors: FieldErrors = {};
  if (!farmerId) fieldErrors.farmer = 'Select a farmer.';
  if (!siteId) fieldErrors.site = 'Select a collection site.';
  if (!locationId || !locations.some(option => option.receivingLocationId === locationId)) fieldErrors.location = 'Select a receiving location for this site.';
  const receivedTime = receivedAt ? new Date(receivedAt).getTime() : NaN;
  if (!Number.isFinite(receivedTime)) fieldErrors.receivedAt = 'Enter a valid arrival time.';
  else if (receivedTime > Date.now()) fieldErrors.receivedAt = 'Arrival time cannot be in the future.';
  if (reference && (lines.length < 1 || lines.length > reference.maxLinesPerReceipt)) fieldErrors.lines = `Enter 1–${reference.maxLinesPerReceipt} receipt lines.`;
  lines.forEach((line, index) => {
    if (!plotSeasons.some(item => item.id === line.plotSeasonId)) fieldErrors[`plotSeason-${index}`] = `Line ${index + 1}: select a plot season for this farmer.`;
    if (!decimal.test(line.measuredValue) || Number(line.measuredValue) <= 0) fieldErrors[`mass-${index}`] = `Line ${index + 1}: enter a positive mass as a number.`;
    if (reference && !reference.massUnits.some(unit => unit.code === line.measuredUnit)) fieldErrors[`unit-${index}`] = `Line ${index + 1}: select an available mass unit.`;
    if (!decimal.test(line.moisturePercent) || Number(line.moisturePercent) > 100) fieldErrors[`moisture-${index}`] = `Line ${index + 1}: enter moisture from 0 to 100 as a number, without the % sign.`;
  });
  const validation = (key: string) => showValidation && fieldErrors[key] ? <span className="field-validation" role="alert">{fieldErrors[key]}</span> : null;
  function updateLine(index: number, patch: Partial<LineDraft>) { setLines(rows => rows.map((row, i) => i === index ? { ...row, ...patch } : row)); }
  async function save(event: FormEvent) {
    event.preventDefault(); if (busy) return;
    if (!pending) {
      if (!reference || Object.keys(fieldErrors).length > 0) {
        setShowValidation(true); setError(''); return;
      }
    }
    setBusy(true); setError('');
    const request = pending ?? newRequest<ProcurementPayload>({ entityId: crypto.randomUUID(), farmerId, collectionSiteId: siteId,
      receivingLocationId: locationId, commodityId: reference!.commodity.id, receivedAt: new Date(receivedAt).toISOString(),
      items: lines.map((line, index) => ({ ...line, itemId: crypto.randomUUID(), lineNo: index + 1, lotId: crypto.randomUUID(), sourceLotId: crypto.randomUUID() })) }, 2);
    if (!pending) setPending(request);
    try {
      const created = await submitCapture<ProcurementPayload, Procurement>(key, '/api/procurements', request);
      onCreated(created.id);
    } catch (cause) {
      if (wasRejected(cause) || cause instanceof ApiError && cause.status === 503) {
        discardPending(key); setPending(null);
      }
      setError(cause instanceof ApiError && cause.status === 503
        ? 'Procurement cannot be registered yet. The Ledger service or its reference data is unavailable. No source receipt or stock was created.'
        : errorMessage(cause));
    } finally { setBusy(false); }
  }

  return <><div className="page-heading"><span className="eyebrow">PROCUREMENT</span><h1>Record paddy intake</h1><p className="muted">Register expected quantities for one farmer delivery. Mill staff confirm the physical arrival before stock becomes available.</p></div><button className="text-button" onClick={onBack}>← Back to procurements</button>
    {referenceError && <div className="notice error" role="alert">Receiving options are unavailable: {referenceError} <button className="text-button" onClick={() => setReferenceRetry(n => n + 1)}>Try again</button></div>}
    {sourceError && <div className="notice error" role="alert">Source records are unavailable: {sourceError} <button className="text-button" onClick={() => setSourceRetry(n => n + 1)}>Try again</button></div>}
    <section className="panel capture-form-page"><form onSubmit={save} noValidate>
      <h2>Delivery</h2>
      {farmers.length > 10 && <label>Find farmer<input value={farmerSearch} onChange={e => setFarmerSearch(e.target.value)} disabled={busy || !!pending} placeholder="Name or code" /></label>}
      <label>Farmer<select value={farmerId} onChange={e => { setFarmerId(e.target.value); setLines([{ plotSeasonId: '', measuredValue: '', measuredUnit: 'KG', moisturePercent: '' }]); }} required aria-invalid={showValidation && !!fieldErrors.farmer} disabled={busy || !!pending}><option value="">Select farmer</option>{visibleFarmers.map(item => <option key={item.id} value={item.id}>{item.name} · {item.code}</option>)}</select>{validation('farmer')}</label>
      <div className="form-columns"><label>Collection site<select value={siteId} onChange={e => { setSiteId(e.target.value); setLocationId(''); }} required aria-invalid={showValidation && !!fieldErrors.site} disabled={busy || !!pending}><option value="">Select site</option>{sites.map(site => <option key={site.collectionSiteId} value={site.collectionSiteId}>{site.collectionSiteName} · {site.collectionSiteCode}</option>)}</select>{validation('site')}</label>
        <label>Receiving location<select value={locationId} onChange={e => setLocationId(e.target.value)} required aria-invalid={showValidation && !!fieldErrors.location} disabled={busy || !!pending || !siteId}><option value="">Select location</option>{locations.map(option => <option key={option.receivingLocationId} value={option.receivingLocationId}>{option.receivingLocationCode}</option>)}</select>{validation('location')}</label></div>
      <label>Received at<input type="datetime-local" value={receivedAt} onChange={e => setReceivedAt(e.target.value)} required aria-invalid={showValidation && !!fieldErrors.receivedAt} disabled={busy || !!pending} />{validation('receivedAt')}</label>
      <h2>Receipt lines</h2>
      {lines.map((line, index) => <div className="capture-item-row" key={index}><strong>Line {index + 1}</strong><label>Plot season<select value={line.plotSeasonId} onChange={e => updateLine(index, { plotSeasonId: e.target.value })} required aria-invalid={showValidation && !!fieldErrors[`plotSeason-${index}`]} disabled={busy || !!pending || !farmerId}><option value="">Select plot season</option>{plotSeasons.map(item => <option key={item.id} value={item.id}>{plots.find(plot => plot.id === item.plotId)?.code ?? item.plotId} · {item.riceVariety}</option>)}</select>{validation(`plotSeason-${index}`)}</label>
        <div className="form-columns"><label>Measured mass<input inputMode="decimal" value={line.measuredValue} onChange={e => updateLine(index, { measuredValue: e.target.value })} required aria-invalid={showValidation && !!fieldErrors[`mass-${index}`]} disabled={busy || !!pending} placeholder="600" />{validation(`mass-${index}`)}</label><label>Unit<select value={line.measuredUnit} onChange={e => updateLine(index, { measuredUnit: e.target.value })} aria-invalid={showValidation && !!fieldErrors[`unit-${index}`]} disabled={busy || !!pending}>{(reference?.massUnits ?? [{ code: 'KG', name: 'Kilogram' }]).map(unit => <option key={unit.code} value={unit.code}>{unit.name} ({unit.code})</option>)}</select>{validation(`unit-${index}`)}</label></div>
        <label>Moisture (%)<input inputMode="decimal" value={line.moisturePercent} onChange={e => updateLine(index, { moisturePercent: e.target.value })} required aria-invalid={showValidation && !!fieldErrors[`moisture-${index}`]} disabled={busy || !!pending} placeholder="22.5 (number only)" />{validation(`moisture-${index}`)}</label>
        {lines.length > 1 && !pending && <button className="text-button" type="button" onClick={() => setLines(rows => rows.filter((_, i) => i !== index))}>Remove line</button>}</div>)}
      {validation('lines')}
      {!pending && <button className="secondary" type="button" disabled={!reference || lines.length >= reference.maxLinesPerReceipt} onClick={() => setLines(rows => [...rows, { plotSeasonId: '', measuredValue: '', measuredUnit: reference?.massUnits[0]?.code ?? 'KG', moisturePercent: '' }])}>+ Add line</button>}
      {pending && !busy && <div className="notice pending">The outcome is uncertain. Retry sends the same receipt and lot IDs; do not create a replacement request.</div>}
      {showValidation && Object.keys(fieldErrors).length > 0 && <div className="notice error" role="alert">Correct the fields marked above before registering this intake.</div>}
      {error && <div className="notice error" role="alert">{error}</div>}
      <div className="form-footer"><button className="secondary" type="button" onClick={onBack}>Cancel</button><button type="submit" disabled={busy || (!pending && (!reference || receivingOptions.length === 0 || plotSeasons.length === 0))}>{busy ? 'Registering…' : pending ? 'Retry registration' : 'Register procurement'}</button></div>
    </form></section>
  </>;
}
