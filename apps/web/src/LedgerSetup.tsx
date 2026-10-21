import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, type User } from './api';
import { Choice, Enum, Evidence, Feedback, Mass, Panel, Table, emptyLookup, id, iso, kinds, mutate, name, now, products, useAction, type Lookup, type Row } from './ledger-ui';
import { CatalogScreen } from './CatalogScreen';
import { ReceivingThresholdSettings } from './ReceivingThresholds';
type Setup = Lookup & { assignments: Row[]; policies: Row[]; compatibilities: Row[]; mixedBulkPolicies: Row[] };
export function LedgerSetup({ user, active = true }: { user: User; active?: boolean }) {
  const [data, setData] = useState<Setup>({ ...emptyLookup, assignments: [], policies: [], compatibilities: [], mixedBulkPolicies: [] });
  const [users, setUsers] = useState<Row[]>([]), [organizations, setOrganizations] = useState<Row[]>([]);
  const action = useAction(), configure = user.permissions.includes('platform.configure');
  const [facility, setFacility] = useState(''), [facilityName, setFacilityName] = useState(''), [locationName, setLocationName] = useState(''), [locationKind, setLocationKind] = useState('STORE'), [locationMixedBulk, setLocationMixedBulk] = useState(false), [equipmentName, setEquipmentName] = useState(''), [staff, setStaff] = useState('');
  const [editing, setEditing] = useState<{ path: string; id: string; version: string; row: Row } | null>(null);
  const editForm = useRef<HTMLFormElement>(null);
  const [policyFacility, setPolicyFacility] = useState(''), [owner, setOwner] = useState(''), [evidence, setEvidence] = useState(''), [policyKind, setPolicyKind] = useState('DRYING'), [tolerance, setTolerance] = useState(''), [effective, setEffective] = useState(now());
  const [poolPolicyLocation, setPoolPolicyLocation] = useState(''), [poolPolicyOwner, setPoolPolicyOwner] = useState(''), [poolPolicyCommodity, setPoolPolicyCommodity] = useState(''), [poolPolicyStage, setPoolPolicyStage] = useState('RAW_PADDY'), [poolPolicyProduct, setPoolPolicyProduct] = useState('RAW_PADDY'), [poolPolicyEffective, setPoolPolicyEffective] = useState(now()), [poolPolicyEvidence, setPoolPolicyEvidence] = useState(''), [identityScopeConfirmed, setIdentityScopeConfirmed] = useState(false);
  const [blendInputs, setBlendInputs] = useState([{ commodityId: '', product: 'HEAD_RICE' }]), [blendOutput, setBlendOutput] = useState(''), [location, setLocation] = useState(''), [clearReason, setClearReason] = useState(''), [clearEvidence, setClearEvidence] = useState('');
  async function load() {
    const [setup, admin] = await Promise.all([
      api<Setup>('/api/ledger/setup'),
      configure ? Promise.all([api<Row[]>('/api/admin/users'), api<Row[]>('/api/admin/organizations')]) : Promise.resolve(undefined),
    ]);
    setData({ ...setup, mixedBulkPolicies: setup.mixedBulkPolicies || [] });
    if (admin) { const [u, o] = admin; setUsers(u); setOrganizations(o); }
  }
  useEffect(() => { if (active) void action.run(load); }, [active]);
  useEffect(() => {
    if (!editing) return;
    const form = editForm.current;
    form?.scrollIntoView?.({ block: 'center' });
    form?.querySelector<HTMLInputElement | HTMLSelectElement>('input, select')?.focus();
  }, [editing]);
  async function save(event: FormEvent, path: string, body: Row) { event.preventDefault(); await action.run(async () => { await mutate('/api/ledger/' + path, body, body.version ? 'PUT' : 'POST'); await load(); setEditing(null); }, 'Setup record saved.'); }
  async function remove(path: string, label: string, version: string) {
    if (!window.confirm(`Remove ${label} from active records? Stored record/history will be retained.`)) return;
    await action.run(async () => { await mutate(`/api/ledger/${path}`, { version }, 'DELETE'); await load(); }, 'Setup record deleted.');
  }
  function resourceTable(label: string, rows: Row[], cols: string[], editable = true) {
    const resourcePath = label === 'Facilities' ? 'facilities' : label === 'Locations' ? 'locations' : label === 'Equipment' ? 'equipment' : '';
    return <section aria-label={label}><h4>{label}</h4>{configure && rows.some(row => row.createdBy === null) && <p className="empty">Legacy setup records are read-only.</p>}<Table rows={rows} label={label} columns={cols} actions={row => editable && configure && <>{row.canEdit && <button type="button" onClick={() => { setEditing({ path: label === 'Staff assignments' ? 'assignments' : resourcePath, id: row.id || '', version: row.version, row }); if (label === 'Facilities') setFacilityName(row.name); if (label === 'Locations') { setFacility(row.facilityId); setLocationName(row.name); setLocationKind(row.kind); setLocationMixedBulk(row.mixedBulk === true); } if (label === 'Equipment') { setFacility(row.facilityId); setEquipmentName(row.name); } if (label === 'Staff assignments') { setFacility(row.facilityId); setStaff(row.userId); } }}>Edit {row.name || label}</button>}{row.canDelete && <button type="button" onClick={() => void remove(label === 'Staff assignments' ? `assignments/${row.userId}/${row.facilityId}` : `${resourcePath}/${row.id}`, row.name || `${name(users, row.userId)} — ${name(data.facilities, row.facilityId)}`, row.version)}>Delete {row.name || label}</button>}</> } /></section>;
  }
  const clearLocation = data.locations.find(row => row.id === location);
  return <><Feedback {...action} /><h2>Setup</h2><p className="ledger-task-intro">{configure ? 'Start with Catalog, then create facilities and assign mill staff. Configure evidence-backed policies only when required.' : 'View facilities, locations, equipment and staff within your assigned scope.'}</p>
    <details className="ledger-disclosure" open>
      <summary>{configure ? 'Step 1 — Catalog and basic facility setup' : 'Facilities and resources'}</summary>
      {configure && <CatalogScreen active={active} />}
      <Panel title="Facilities, locations, equipment and staff">
        {configure && <p>Create a facility first. Then add its locations, equipment, and mill staff assignment.</p>}
        {configure && <>
          <div className="ledger-form-grid">
            <form ref={editing?.path === 'facilities' ? editForm : null} onSubmit={e => void save(e, editing?.path === 'facilities' ? `facilities/${editing.id}` : 'facilities', { ...(editing?.path === 'facilities' ? { name: facilityName, version: editing.version } : { id: id(), name: facilityName }) })}>
              <label>Facility name<input required value={facilityName} onChange={e => setFacilityName(e.target.value)} /></label>
              <button disabled={action.busy}>{editing?.path === 'facilities' ? 'Save facility' : 'Add facility'}</button>{editing?.path === 'facilities' && <button type="button" onClick={() => setEditing(null)}>Cancel edit</button>}
            </form>
            <label>Facility for setup<Choice rows={data.facilities} value={facility} onChange={setFacility} /></label>
            <form ref={editing?.path === 'locations' ? editForm : null} onSubmit={e => void save(e, editing?.path === 'locations' ? `locations/${editing.id}` : 'locations', { ...(editing?.path === 'locations' ? { name: locationName, facilityId: facility, kind: locationKind, mixedBulk: locationMixedBulk, version: editing.version } : { id: id(), facilityId: facility, name: locationName, kind: locationKind, mixedBulk: locationMixedBulk }) })}>
              <label>Location name<input required value={locationName} onChange={e => setLocationName(e.target.value)} /></label>
              <label>Location kind<Enum values={['STORE', 'SILO', 'COMPARTMENT', 'ISOLATION']} value={locationKind} onChange={setLocationKind} /></label>
              <label><input type="checkbox" checked={locationMixedBulk} onChange={e => setLocationMixedBulk(e.target.checked)} /> Enable mixed-bulk storage</label>
              <button disabled={action.busy || !facility}>{editing?.path === 'locations' ? 'Save location' : 'Add location'}</button>{editing?.path === 'locations' && <button type="button" onClick={() => setEditing(null)}>Cancel edit</button>}
            </form>
            <form ref={editing?.path === 'equipment' ? editForm : null} onSubmit={e => void save(e, editing?.path === 'equipment' ? `equipment/${editing.id}` : 'equipment', { ...(editing?.path === 'equipment' ? { name: equipmentName, facilityId: facility, version: editing.version } : { id: id(), facilityId: facility, name: equipmentName }) })}>
              <label>Equipment name<input required value={equipmentName} onChange={e => setEquipmentName(e.target.value)} /></label>
              <button disabled={action.busy || !facility}>{editing?.path === 'equipment' ? 'Save equipment' : 'Add equipment'}</button>{editing?.path === 'equipment' && <button type="button" onClick={() => setEditing(null)}>Cancel edit</button>}
            </form>
          </div>
          <details open={editing?.path === 'assignments'}>
            <summary>{editing?.path === 'assignments' ? 'Edit staff assignment' : 'Assign staff to facility'}</summary>
            <form ref={editing?.path === 'assignments' ? editForm : null} onSubmit={e => void save(e, editing?.path === 'assignments' ? `assignments/${editing.row.userId}/${editing.row.facilityId}` : 'assignments', { userId: staff, facilityId: facility, ...(editing?.path === 'assignments' ? { version: editing.version, newUserId: staff, newFacilityId: facility } : {}) })}>
              <label>Active mill staff<Choice rows={users.filter(row => row.isActive && ['MillOperator', 'MillManager'].includes(row.role)).map(row => ({ ...row, name: `${row.displayName} — ${row.email} (${row.role})` }))} value={staff} onChange={setStaff} /></label>
              <button disabled={action.busy || !facility}>{editing?.path === 'assignments' ? 'Save assignment' : 'Assign facility'}</button>{editing?.path === 'assignments' && <button type="button" onClick={() => setEditing(null)}>Cancel edit</button>}
            </form>
          </details>
        </>}
        {!configure && <p className="empty">Setup resources are visible as read-only in this account scope.</p>}
        {resourceTable('Facilities', data.facilities, ['name'])}
        {resourceTable('Locations', data.locations.map(row => ({ ...row, mixedBulkMode: row.mixedBulk ? 'Enabled' : 'Disabled', facility: name(data.facilities, row.facilityId) })), ['name', 'facility', 'kind', 'mixedBulkMode'])}
        {resourceTable('Staff assignments', data.assignments.map(row => ({ ...row, user: row.userName || row.displayName || users.find(candidate => candidate.id === row.userId)?.displayName || (row.userId === user.id ? user.displayName : 'Staff member'), facility: name(data.facilities, row.facilityId), name: `${row.userName || row.displayName || users.find(candidate => candidate.id === row.userId)?.displayName || (row.userId === user.id ? user.displayName : 'Staff member')} — ${name(data.facilities, row.facilityId)}` })), ['name', 'user', 'facility'])}
        {data.commodities.length > 0 && <details><summary>Commodity identities used by Ledger</summary>{resourceTable('Commodity identities', data.commodities.map(row => ({ ...row, code: row.code || 'Legacy commodity' })), ['code', 'name'], false)}<p>Catalog commodity identity is read-only here; manage names in Catalog.</p></details>}
      </Panel>
    </details>
    {!configure && <div className="ledger-grid"><Panel title="Balance policies">{resourceTable('Balance policies', data.policies.map(row => ({ ...row, tolerance: `${row.tolerancePercent}%`, facility: name(data.facilities, row.facilityId), evidence: row.evidenceId ? 'Attached' : 'Missing' })), ['kind', 'tolerance', 'effectiveFrom', 'facility', 'evidence'], false)}</Panel><Panel title="Blend compatibilities">{resourceTable('Blend compatibilities', data.compatibilities.map(row => ({ ...row, facility: name(data.facilities, row.facilityId), output: name(data.commodities, row.outputCommodityId), evidence: row.evidenceId ? 'Attached' : 'Missing' })), ['facility', 'output', 'effectiveFrom', 'evidence'], false)}</Panel></div>}
    {configure && <details className="ledger-disclosure"><summary>Step 2 — Advanced policy, compatibility and compartment clearance</summary><p>Use only after basic setup. Each policy or compatibility needs evidence scoped to facility and owner. Changing either scope requires re-uploading evidence.</p><div className="ledger-grid"><Panel title="Confirmed balance policy">
      <p>Choose a facility and evidence owner. Choose a file, then Upload evidence, then Record. Effective time must be at or before processing occurred time. Zero tolerance is valid only when explicitly confirmed; there is no default.</p>
      <label>Facility for policy<Choice rows={data.facilities} value={policyFacility} onChange={value => { setPolicyFacility(value); setEvidence(''); }} /></label>
      <label>Evidence owner<Choice rows={organizations} value={owner} onChange={value => { setOwner(value); setEvidence(''); setClearEvidence(''); }} /></label><Evidence ownerId={owner} facilityId={policyFacility} value={evidence} onChange={setEvidence} />
      <form onSubmit={e => void save(e, 'policies', { id: id(), facilityId: policyFacility, kind: policyKind, tolerancePercent: tolerance, effectiveFrom: iso(effective), evidenceId: evidence })}><label>Operation kind<Enum values={kinds} value={policyKind} onChange={setPolicyKind} /></label><Mass label="Confirmed tolerance (%)" value={tolerance} onChange={setTolerance} /><label>Effective from<input required type="datetime-local" value={effective} onChange={e => setEffective(e.target.value)} /></label><button disabled={action.busy || !policyFacility || !owner || !evidence}>Record policy version</button>{!policyFacility && <p>Choose facility to record policy.</p>}{policyFacility && !owner && <p>Choose evidence owner to record policy.</p>}{policyFacility && owner && !evidence && <p>Upload evidence for selected owner and facility.</p>}</form>
      {resourceTable('Balance policies', data.policies.map(row => ({ ...row, tolerance: `${row.tolerancePercent}%`, facility: name(data.facilities, row.facilityId), evidence: row.evidenceId ? 'Attached' : 'Missing' })), ['kind', 'tolerance', 'effectiveFrom', 'facility', 'evidence'], false)}
      <p>Balance policies and blend compatibilities are immutable; record add-new-version changes only.</p>
      </Panel><Panel title="Mixed-bulk location policies">
        <p>Location activation and compatibility policy are separate. Policy is append-only, requires uploaded evidence for this owner and facility, and applies only to exact owner, commodity, stage and product identity. Confirm no unlisted identity attributes are being assumed compatible.</p>
        <label>Enabled mixed-bulk location<Choice rows={data.locations.filter(row => row.mixedBulk === true).map(row => ({ ...row, name: `${row.name} — ${name(data.facilities, row.facilityId)}` }))} value={poolPolicyLocation} onChange={value => { setPoolPolicyLocation(value); setPoolPolicyEvidence(''); setIdentityScopeConfirmed(false); }} /></label>
        {poolPolicyLocation && <p>Scope binds persisted location kind {data.locations.find(row => row.id === poolPolicyLocation)?.kind || '—'} at facility {name(data.facilities, data.locations.find(row => row.id === poolPolicyLocation)?.facilityId)}. Changing kind later does not broaden this policy.</p>}
        <p>Stock policy owner must be a customer organization, not an operating mill.</p>
        <label>Policy owner<Choice rows={organizations.filter(row => row.kind === 'Customer')} value={poolPolicyOwner} onChange={value => { setPoolPolicyOwner(value); setPoolPolicyEvidence(''); setIdentityScopeConfirmed(false); }} /></label>
        <label>Commodity<Choice rows={data.commodities} value={poolPolicyCommodity} onChange={value => { setPoolPolicyCommodity(value); setIdentityScopeConfirmed(false); }} /></label>
        <label>Stage<Enum values={['RAW_PADDY', 'DRIED_PADDY', 'HEAD_RICE', 'BROKEN_RICE', 'BRAN', 'HUSK', 'BLENDED_RICE', 'BAGGED_RICE']} value={poolPolicyStage} onChange={value => { setPoolPolicyStage(value); setIdentityScopeConfirmed(false); }} /></label>
        <label>Product identity<Enum values={['RAW_PADDY', 'DRIED_PADDY', 'HEAD_RICE', 'BROKEN_RICE', 'BRAN', 'HUSK', 'BLENDED_RICE', 'BAGGED_RICE']} value={poolPolicyProduct} onChange={value => { setPoolPolicyProduct(value); setIdentityScopeConfirmed(false); }} /></label>
        <label>Effective from<input required type="datetime-local" value={poolPolicyEffective} onChange={e => { setPoolPolicyEffective(e.target.value); setIdentityScopeConfirmed(false); }} /></label>
        <Evidence ownerId={poolPolicyOwner} facilityId={data.locations.find(row => row.id === poolPolicyLocation)?.facilityId || ''} value={poolPolicyEvidence} onChange={value => { setPoolPolicyEvidence(value); setIdentityScopeConfirmed(false); }} />
        <label><input type="checkbox" checked={identityScopeConfirmed} onChange={e => setIdentityScopeConfirmed(e.target.checked)} /> I confirm this evidence explicitly approves this exact owner, commodity, stage and product identity for this location; no other identity scope is implied.</label>
        <button type="button" disabled={action.busy || !poolPolicyLocation || !poolPolicyOwner || !poolPolicyCommodity || !poolPolicyEvidence || !identityScopeConfirmed} onClick={() => void action.run(async () => { await mutate('/api/ledger/mixed-bulk-policies', { id: id(), locationId: poolPolicyLocation, ownerId: poolPolicyOwner, commodityId: poolPolicyCommodity, stage: poolPolicyStage, productIdentity: poolPolicyProduct, effectiveFrom: iso(poolPolicyEffective), evidenceId: poolPolicyEvidence, identityScopeConfirmed: true }); setIdentityScopeConfirmed(false); await load(); }, 'Mixed-bulk policy recorded as immutable evidence-backed scope.')}>Record mixed-bulk policy</button>
        {resourceTable('Mixed-bulk policies', data.mixedBulkPolicies.map(row => ({ ...row, location: name(data.locations, row.locationId), owner: name(organizations, row.ownerId), commodity: name(data.commodities, row.commodityId), evidence: row.evidenceId ? 'Attached' : 'Missing' })), ['location', 'owner', 'commodity', 'stage', 'productIdentity', 'effectiveFrom', 'evidence'], false)}
      </Panel>
    <Panel title="Confirmed blend compatibility"><p>Blend compatibility shares evidence owner, facility, uploaded evidence and effective time selected in policy panel. Effective time must be at or before processing occurred time. Compatibilities are immutable; record add-new-version changes only.</p><form onSubmit={e => void save(e, 'compatibilities', { id: id(), facilityId: policyFacility, inputProducts: blendInputs.map(row => `${row.commodityId}:${row.product}`), outputCommodityId: blendOutput, effectiveFrom: iso(effective), evidenceId: evidence })}>
      {blendInputs.map((row, index) => <fieldset key={index}><legend>Approved input product {index + 1}</legend><label>Commodity<Choice rows={data.commodities} value={row.commodityId} onChange={value => setBlendInputs(rows => rows.map((x, i) => i === index ? { ...x, commodityId: value } : x))} /></label><label>Product identity<Enum values={products} value={row.product} onChange={value => setBlendInputs(rows => rows.map((x, i) => i === index ? { ...x, product: value } : x))} /></label><button type="button" disabled={blendInputs.length === 1} onClick={() => setBlendInputs(rows => rows.filter((_, i) => i !== index))}>Remove product</button></fieldset>)}<button type="button" onClick={() => setBlendInputs(rows => [...rows, { commodityId: '', product: 'HEAD_RICE' }])}>Add approved input</button><label>Output commodity<Choice rows={data.commodities} value={blendOutput} onChange={setBlendOutput} /></label><label>Effective from<input required type="datetime-local" value={effective} onChange={e => setEffective(e.target.value)} /></label><button disabled={action.busy || !policyFacility || !owner || !evidence || blendInputs.some(row => !row.commodityId) || !blendOutput}>Record compatibility</button></form><h3>Recorded blend compatibilities</h3>{resourceTable('Blend compatibilities', data.compatibilities.map(row => ({ ...row, facility: name(data.facilities, row.facilityId), output: name(data.commodities, row.outputCommodityId), evidence: row.evidenceId ? 'Attached' : 'Missing' })), ['facility', 'output', 'effectiveFrom', 'evidence'], false)}</Panel>
      <Panel title="Recorded compartment clearance"><p>Only empty compartments can clear ownership. Occupied stock never changes owner here.</p><form onSubmit={e => void save(e, `locations/${location}/clearance`, { evidenceId: clearEvidence, reason: clearReason })}><label>Compartment<Choice rows={data.locations.filter(row => row.kind === 'COMPARTMENT')} value={location} onChange={value => { setLocation(value); setClearEvidence(''); }} /></label><Evidence ownerId={owner} facilityId={clearLocation?.facilityId || ''} value={clearEvidence} onChange={setClearEvidence} /><label>Clearance reason<textarea required value={clearReason} onChange={e => setClearReason(e.target.value)} /></label><button disabled={action.busy || !clearEvidence}>Record clearance</button></form></Panel>
    </div></details>}
    {configure && <details className="ledger-disclosure"><summary>Advanced setup — Receiving loss thresholds</summary><ReceivingThresholdSettings active={active && configure} /></details>}
  </>;
}
