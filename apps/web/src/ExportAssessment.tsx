import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, errorMessage } from './api';
import { LedgerLotPicker } from './LedgerLotPicker';
import { Evidence, Feedback, id, localTime, type Lot, type Lookup, type Page, useAction } from './ledger-ui';

type Item = { id: string; code: string; name: string; unit?: string };
type Rule = { attributeId: string; unit: string; calculation: 'MASS_WEIGHTED_MEAN' };
type Policy = { id: string; versionNo: number; effectiveFrom: string };
type Observation = { id: string; attributeId: string; versionNo: number; measuredValue: string; unit: string; evidenceId: string };
const finished = new Set(['HEAD_RICE', 'BROKEN_RICE', 'BLENDED_RICE', 'BAGGED_RICE']);
const decimal = /^(?:0|[1-9]\d*)(?:\.\d{1,12})?$/;
const date = (value: string) => new Date(value).toISOString();
async function all(path: string): Promise<Item[]> {
  const items: Item[] = [];
  for (let page = 1; ; page++) {
    const result = await api<Page<Item>>(`${path}?page=${page}&pageSize=100`);
    items.push(...result.items);
    if (!result.hasMore) return items;
  }
}
function Select({ label, rows, value, onChange }: { label: string; rows: Item[]; value: string; onChange: (value: string) => void }) {
  return <label>{label}<select required value={value} onChange={event => onChange(event.target.value)}><option value="">Select {label.toLowerCase()}</option>{rows.map(row => <option key={row.id} value={row.id}>{row.code} · {row.name}</option>)}</select></label>;
}

export function ExportAssessment({ companyId }: { companyId: string }) {
  const [markets, setMarkets] = useState<Item[]>([]), [commodities, setCommodities] = useState<Item[]>([]);
  const [substances, setSubstances] = useState<Item[]>([]), [attributes, setAttributes] = useState<Item[]>([]);
  const [lookups, setLookups] = useState<Lookup>({ owners: [], facilities: [], locations: [], commodities: [], equipment: [] });
  const [loading, setLoading] = useState(true), [error, setError] = useState('');
  useEffect(() => {
    let active = true;
    void Promise.all([all('/api/markets'), all('/api/commodities'), all('/api/active-substances'), all('/api/quality-attributes'), api<Lookup>('/api/ledger/lookups')])
      .then(([m, c, s, a, l]) => { if (active) { setMarkets(m); setCommodities(c); setSubstances(s); setAttributes(a); setLookups(l); } })
      .catch(cause => { if (active) setError(errorMessage(cause)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);
  return <><header className="page-heading"><span className="eyebrow">EXPORT · CUSTOMER MANAGER</span><h1>Export assessment</h1><p className="muted">Publish reviewed market rules and confirm measured quality for finished lots.</p></header>
    {loading && <p role="status">Loading catalogs and facilities…</p>}{error && <div className="notice error" role="alert">{error}</div>}
    {!loading && !error && <><PolicyForm markets={markets} commodities={commodities} substances={substances} attributes={attributes} /><QualityForm companyId={companyId} attributes={attributes} lookups={lookups} /></>}
  </>;
}

function PolicyForm({ markets, commodities, substances, attributes }: { markets: Item[]; commodities: Item[]; substances: Item[]; attributes: Item[] }) {
  const [marketId, setMarketId] = useState(''), [commodityId, setCommodityId] = useState('');
  const [current, setCurrent] = useState<Policy | null>(null), [lookupError, setLookupError] = useState('');
  const [versionNo, setVersionNo] = useState(1), [effectiveFrom, setEffectiveFrom] = useState(''), [effectiveTo, setEffectiveTo] = useState('');
  const [requiredSubstanceIds, setRequiredSubstanceIds] = useState<string[]>([]), [substanceSearch, setSubstanceSearch] = useState('');
  const [qualityRules, setQualityRules] = useState<Rule[]>([]), [mrlMarginFraction, setMargin] = useState(''), [residueBasis, setBasis] = useState('');
  const [evidenceReference, setEvidenceReference] = useState(''), [reviewedBy, setReviewedBy] = useState(''), [reviewAuthority, setReviewAuthority] = useState(''), [reviewedAt, setReviewedAt] = useState(''), [confirmed, setConfirmed] = useState(false);
  const action = useAction();
  useEffect(() => {
    setCurrent(null); setLookupError(''); setVersionNo(1); setConfirmed(false);
    if (!marketId || !commodityId) return;
    let active = true;
    void api<Policy>(`/api/blend-assessment-policies/applicable?${new URLSearchParams({ marketId, commodityId })}`)
      .then(row => { if (active) { setCurrent(row); setVersionNo(row.versionNo + 1); } })
      .catch(cause => { if (active && !(cause instanceof ApiError && cause.status === 404)) setLookupError(errorMessage(cause)); });
    return () => { active = false; };
  }, [marketId, commodityId]);
  const toggle = (value: string) => setRequiredSubstanceIds(rows => rows.includes(value) ? rows.filter(id => id !== value) : [...rows, value]);
  function addRule(value: string) {
    const item = attributes.find(row => row.id === value);
    if (item && !qualityRules.some(row => row.attributeId === value)) setQualityRules(rows => [...rows, { attributeId: value, unit: item.unit || '', calculation: 'MASS_WEIGHTED_MEAN' }]);
  }
  async function publish(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!requiredSubstanceIds.length || !decimal.test(mrlMarginFraction) || Number(mrlMarginFraction) >= 1 || qualityRules.some(row => !row.unit)) { action.setError('Select required substances, a margin fraction below 1 and valid quality rules.'); return; }
    if (effectiveTo && new Date(effectiveTo) <= new Date(effectiveFrom)) { action.setError('Effective to must follow effective from.'); return; }
    await action.run(async () => {
      const policyId = id();
      await api('/api/blend-assessment-policies', { method: 'POST', body: JSON.stringify({ id: policyId, marketId, commodityId, versionNo, effectiveFrom: date(effectiveFrom), effectiveTo: effectiveTo ? date(effectiveTo) : null, requiredSubstanceIds, mrlMarginFraction, residueBasis: residueBasis.trim(), qualityRules, evidenceReference: evidenceReference.trim(), reviewedBy: reviewedBy.trim(), reviewAuthority: reviewAuthority.trim(), reviewedAt: date(reviewedAt) }) });
      if (new Date(effectiveFrom) <= new Date() && (!effectiveTo || new Date(effectiveTo) > new Date())) setCurrent({ id: policyId, versionNo, effectiveFrom: date(effectiveFrom) });
      setVersionNo(value => value + 1); setConfirmed(false);
    }, 'Policy published. Recalculate the export plan to see remaining checks.');
  }
  return <section className="panel export-section"><h2>Market assessment policy</h2><p className="muted">Enter the reviewed scope and scientific rules. Publication is immutable; it does not mark a plan as passing.</p>
    <div className="form-columns"><Select label="Export market" rows={markets} value={marketId} onChange={setMarketId} /><Select label="Commodity" rows={commodities} value={commodityId} onChange={setCommodityId} /></div>
    {lookupError && <div className="notice error" role="alert">Current policy could not be checked: {lookupError}</div>}
    {marketId && commodityId && !lookupError && <p className="muted">{current ? `Applicable policy v${current.versionNo}, effective ${new Date(current.effectiveFrom).toLocaleString()}.` : 'No policy is currently applicable. Check prior publication history before choosing a version.'}</p>}
    <form onSubmit={event => void publish(event)}>
      <div className="form-columns"><label>Version number<input type="number" min="1" step="1" required value={versionNo} onChange={event => setVersionNo(Number(event.target.value))} /></label><label>MRL safety margin fraction<input required inputMode="decimal" placeholder="0.1 means 10%" value={mrlMarginFraction} onChange={event => setMargin(event.target.value)} /></label></div>
      <div className="form-columns"><label>Effective from<input required type="datetime-local" value={effectiveFrom} onChange={event => setEffectiveFrom(event.target.value)} /></label><label>Effective to (optional)<input type="datetime-local" value={effectiveTo} onChange={event => setEffectiveTo(event.target.value)} /></label></div>
      <label>Comparable residue material basis<input required maxLength={100} value={residueBasis} onChange={event => setBasis(event.target.value)} /></label>
      <fieldset><legend>Required active substances</legend><label>Find substance<input type="search" value={substanceSearch} onChange={event => setSubstanceSearch(event.target.value)} /></label><div className="assessment-checklist">{substances.filter(row => requiredSubstanceIds.includes(row.id) || `${row.code} ${row.name}`.toLowerCase().includes(substanceSearch.toLowerCase())).map(row => <label key={row.id}><input type="checkbox" checked={requiredSubstanceIds.includes(row.id)} onChange={() => toggle(row.id)} /> {row.code} · {row.name}</label>)}</div><p>{requiredSubstanceIds.length} selected</p></fieldset>
      <fieldset><legend>Quality calculation rules</legend><p className="muted">Add each required buyer attribute. The supported calculation is mass weighted mean.</p><label>Add attribute<select value="" onChange={event => addRule(event.target.value)}><option value="">Select attribute</option>{attributes.filter(row => !qualityRules.some(rule => rule.attributeId === row.id)).map(row => <option key={row.id} value={row.id}>{row.code} · {row.name} ({row.unit})</option>)}</select></label>{qualityRules.map(row => <div className="assessment-rule" key={row.attributeId}>{attributes.find(item => item.id === row.attributeId)?.name} · {row.unit} · mass weighted mean <button type="button" className="secondary" onClick={() => setQualityRules(rows => rows.filter(item => item.attributeId !== row.attributeId))}>Remove</button></div>)}</fieldset>
      <label>Approved source or evidence reference<textarea required maxLength={1000} value={evidenceReference} onChange={event => setEvidenceReference(event.target.value)} /></label>
      <div className="form-columns"><label>Reviewed by<input required value={reviewedBy} onChange={event => setReviewedBy(event.target.value)} /></label><label>Review authority<input required value={reviewAuthority} onChange={event => setReviewAuthority(event.target.value)} /></label></div>
      <label>Reviewed at<input required type="datetime-local" max={localTime(new Date().toISOString())} value={reviewedAt} onChange={event => setReviewedAt(event.target.value)} /></label>
      <label className="assessment-confirm"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} /> I checked the approved source, substance scope and calculation rules.</label>
      <Feedback {...action} /><button disabled={action.busy || !confirmed || !marketId || !commodityId || !!lookupError || !requiredSubstanceIds.length}>{action.busy ? 'Publishing…' : 'Publish policy'}</button>
    </form>
  </section>;
}

function QualityForm({ companyId, attributes, lookups }: { companyId: string; attributes: Item[]; lookups: Lookup }) {
  const [facilityId, setFacilityId] = useState(''), [lotId, setLotId] = useState(''), [lot, setLot] = useState<Lot>();
  const [attributeId, setAttributeId] = useState(''), [measuredValue, setMeasuredValue] = useState(''), [measuredAt, setMeasuredAt] = useState('');
  const [evidenceId, setEvidenceId] = useState(''), [reportReference, setReportReference] = useState(''), [reviewedBy, setReviewedBy] = useState(''), [reviewAuthority, setReviewAuthority] = useState(''), [reviewedAt, setReviewedAt] = useState(''), [subjectConfirmed, setSubjectConfirmed] = useState(false);
  const [observations, setObservations] = useState<Observation[]>([]), [readError, setReadError] = useState(''), [reload, setReload] = useState(0);
  const action = useAction();
  useEffect(() => {
    setObservations([]); setReadError('');
    if (!lotId || !facilityId || !companyId) return;
    let active = true;
    void api<Observation[]>(`/api/ledger/planning-quality-observations?${new URLSearchParams({ ownerId: companyId, facilityId, lotIds: lotId })}`)
      .then(rows => { if (active) setObservations(rows); })
      .catch(cause => { if (active) setReadError(errorMessage(cause)); });
    return () => { active = false; };
  }, [companyId, facilityId, lotId, reload]);
  const attribute = attributes.find(row => row.id === attributeId), previous = observations.find(row => row.attributeId === attributeId);
  const validLot = !!lot && lot.id === lotId && finished.has(lot.stage) && lot.ownerId === companyId;
  async function confirm(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!validLot || !attribute?.unit || !decimal.test(measuredValue) || !evidenceId || !subjectConfirmed) { action.setError('Choose a finished lot, measured value (up to 12 decimals), stored evidence and confirm its identity.'); return; }
    if (new Date(reviewedAt) < new Date(measuredAt)) { action.setError('Review time must follow measurement time.'); return; }
    await action.run(async () => {
      await api('/api/ledger/planning-quality-observations', { method: 'POST', body: JSON.stringify({ id: id(), ownerId: companyId, facilityId, lotId, attributeId, versionNo: (previous?.versionNo || 0) + 1, measuredValue, unit: attribute.unit, measuredAt: date(measuredAt), evidenceId, reportReference: reportReference.trim(), reviewedBy: reviewedBy.trim(), reviewAuthority: reviewAuthority.trim(), reviewedAt: date(reviewedAt), subjectConfirmed: true }) });
      setReload(value => value + 1); setEvidenceId(''); setSubjectConfirmed(false);
    }, 'Quality observation confirmed. Recalculate the export plan to see remaining checks.');
  }
  return <section className="panel export-section"><h2>Finished-lot quality evidence</h2><p className="muted">Record a real measurement for one finished lot and upload its supporting report. Previous confirmations remain immutable.</p>
    <label>Facility<select value={facilityId} onChange={event => { setFacilityId(event.target.value); setLotId(''); setLot(undefined); setEvidenceId(''); }}><option value="">Select facility</option>{lookups.facilities.map(row => <option key={row.id} value={row.id}>{row.name}</option>)}</select></label>
    {facilityId && <LedgerLotPicker label="Finished lot" value={lotId} onChange={(value, selected) => { setLotId(value); setLot(selected); setEvidenceId(''); }} onSelected={setLot} look={lookups} ownerId={companyId} facilityId={facilityId} availableOnly={false} />}
    {lotId && lot && !validLot && <div className="notice error" role="alert">Choose a Head, Broken, Blended or Bagged rice lot owned by your company.</div>}
    {readError && <div className="notice error" role="alert">Existing observations could not be read: {readError}</div>}
    {validLot && <><h3>Confirmed measurements for {lot.code}</h3>{observations.length ? <div className="table-scroll"><table><thead><tr><th>Attribute</th><th>Value</th><th>Version</th><th>Evidence</th></tr></thead><tbody>{observations.map(row => <tr key={row.id}><td>{attributes.find(item => item.id === row.attributeId)?.name || row.attributeId}</td><td>{row.measuredValue} {row.unit}</td><td>v{row.versionNo}</td><td><a href={`/api/ledger/evidence/${row.evidenceId}`} target="_blank" rel="noreferrer">View evidence</a></td></tr>)}</tbody></table></div> : <p className="muted">No confirmed measurements for this lot yet.</p>}</>}
    <form onSubmit={event => void confirm(event)}><Select label="Quality attribute" rows={attributes} value={attributeId} onChange={value => { setAttributeId(value); setEvidenceId(''); }} />
      {attribute && <p className="muted">Unit: {attribute.unit}. Next version: {(previous?.versionNo || 0) + 1}.</p>}
      <div className="form-columns"><label>Measured value{attribute?.unit ? ` (${attribute.unit})` : ''}<input required inputMode="decimal" value={measuredValue} onChange={event => setMeasuredValue(event.target.value)} /></label><label>Measured at<input required type="datetime-local" max={localTime(new Date().toISOString())} value={measuredAt} onChange={event => setMeasuredAt(event.target.value)} /></label></div>
      {validLot && <Evidence ownerId={companyId} facilityId={facilityId} value={evidenceId} onChange={setEvidenceId} />}
      <label>Report reference<input required value={reportReference} onChange={event => setReportReference(event.target.value)} /></label>
      <div className="form-columns"><label>Reviewed by<input required value={reviewedBy} onChange={event => setReviewedBy(event.target.value)} /></label><label>Review authority<input required value={reviewAuthority} onChange={event => setReviewAuthority(event.target.value)} /></label></div>
      <label>Reviewed at<input required type="datetime-local" max={localTime(new Date().toISOString())} value={reviewedAt} onChange={event => setReviewedAt(event.target.value)} /></label>
      <label className="assessment-confirm"><input type="checkbox" checked={subjectConfirmed} onChange={event => setSubjectConfirmed(event.target.checked)} /> I verified that the report measures this lot and attribute.</label>
      <Feedback {...action} /><button disabled={action.busy || !validLot || !attributeId || !evidenceId || !subjectConfirmed || !!readError}>{action.busy ? 'Confirming…' : 'Confirm quality observation'}</button>
    </form>
  </section>;
}
