import { useEffect, useState, type FormEvent } from 'react';
import { api, type User } from './api';
import { Choice, Enum, Evidence, Feedback, Mass, Panel, Table, emptyLookup, id, iso, kinds, mutate, now, products, useAction, type Lookup, type Row } from './ledger-ui';

type Setup = Lookup & { assignments: Row[]; policies: Row[]; compatibilities: Row[] };
export function LedgerSetup({ user }: { user: User }) {
  const [data, setData] = useState<Setup>({ ...emptyLookup, assignments: [], policies: [], compatibilities: [] });
  const [users, setUsers] = useState<Row[]>([]), [organizations, setOrganizations] = useState<Row[]>([]);
  const action = useAction(), configure = user.permissions.includes('platform.configure');
  const [facility, setFacility] = useState(''), [facilityName, setFacilityName] = useState(''), [locationName, setLocationName] = useState(''), [locationKind, setLocationKind] = useState('STORE'), [equipmentName, setEquipmentName] = useState(''), [commodityName, setCommodityName] = useState(''), [staff, setStaff] = useState('');
  const [owner, setOwner] = useState(''), [evidence, setEvidence] = useState(''), [policyKind, setPolicyKind] = useState('DRYING'), [tolerance, setTolerance] = useState(''), [effective, setEffective] = useState(now());
  const [blendInputs, setBlendInputs] = useState([{ commodityId: '', product: 'HEAD_RICE' }]), [blendOutput, setBlendOutput] = useState(''), [location, setLocation] = useState(''), [clearReason, setClearReason] = useState(''), [clearEvidence, setClearEvidence] = useState('');
  async function load() {
    const [setup, admin] = await Promise.all([
      api<Setup>('/api/ledger/setup'),
      configure ? Promise.all([api<Row[]>('/api/admin/users'), api<Row[]>('/api/admin/organizations')]) : Promise.resolve(undefined),
    ]);
    setData(setup);
    if (admin) { const [u, o] = admin; setUsers(u); setOrganizations(o); }
  }
  useEffect(() => { void action.run(load); }, []);
  async function save(event: FormEvent, path: string, body: Row) { event.preventDefault(); await action.run(async () => { await mutate('/api/ledger/' + path, body); await load(); }, 'Setup record saved.'); }
  const clearLocation = data.locations.find(row => row.id === location);
  return <><Feedback {...action} /><Panel title="Facilities, locations and occupancy"><Table rows={data.facilities} /><Table rows={data.locations} /></Panel><Panel title="Equipment and commodities"><Table rows={data.equipment} /><Table rows={data.commodities} /></Panel><Panel title="Assignments and versioned rules"><Table rows={data.assignments} /><Table rows={data.policies} /><Table rows={data.compatibilities} /><p>No default tolerance or compatibility. Rules require confirmed evidence.</p></Panel>
    {configure && <div className="ledger-grid"><Panel title="Register prerequisites">
      <form onSubmit={e => void save(e, 'facilities', { id: id(), name: facilityName })}><label>Facility name<input required value={facilityName} onChange={e => setFacilityName(e.target.value)} /></label><button disabled={action.busy}>Add facility</button></form>
      <label>Facility for setup<Choice rows={data.facilities} value={facility} onChange={value => { setFacility(value); setEvidence(''); }} /></label>
      <form onSubmit={e => void save(e, 'locations', { id: id(), facilityId: facility, name: locationName, kind: locationKind })}><label>Location name<input required value={locationName} onChange={e => setLocationName(e.target.value)} /></label><label>Location kind<Enum values={['STORE', 'SILO', 'COMPARTMENT', 'ISOLATION']} value={locationKind} onChange={setLocationKind} /></label><button disabled={action.busy || !facility}>Add location</button></form>
      <form onSubmit={e => void save(e, 'equipment', { id: id(), facilityId: facility, name: equipmentName })}><label>Equipment name<input required value={equipmentName} onChange={e => setEquipmentName(e.target.value)} /></label><button disabled={action.busy || !facility}>Add equipment</button></form>
      <form onSubmit={e => void save(e, 'commodities', { id: id(), name: commodityName })}><label>Commodity name<input required value={commodityName} onChange={e => setCommodityName(e.target.value)} /></label><button disabled={action.busy}>Add commodity</button></form>
      <form onSubmit={e => void save(e, 'assignments', { userId: staff, facilityId: facility })}><label>Active mill staff<Choice rows={users.filter(row => row.isActive && ['MillOperator', 'MillManager'].includes(row.role)).map(row => ({ ...row, name: `${row.displayName} — ${row.email} (${row.role})` }))} value={staff} onChange={setStaff} /></label><button disabled={action.busy || !facility}>Assign facility</button></form>
    </Panel><Panel title="Confirmed balance policy">
      <label>Evidence owner<Choice rows={organizations} value={owner} onChange={value => { setOwner(value); setEvidence(''); setClearEvidence(''); }} /></label><Evidence ownerId={owner} facilityId={facility} value={evidence} onChange={setEvidence} />
      <form onSubmit={e => void save(e, 'policies', { id: id(), facilityId: facility, kind: policyKind, tolerancePercent: tolerance, effectiveFrom: iso(effective), evidenceId: evidence })}><label>Operation kind<Enum values={kinds} value={policyKind} onChange={setPolicyKind} /></label><Mass label="Confirmed tolerance (%)" value={tolerance} onChange={setTolerance} /><label>Effective from<input required type="datetime-local" value={effective} onChange={e => setEffective(e.target.value)} /></label><button disabled={action.busy || !facility || !evidence}>Record policy version</button></form>
    </Panel><Panel title="Confirmed blend compatibility"><p>Evidence and effective time selected in policy panel apply here.</p><form onSubmit={e => void save(e, 'compatibilities', { id: id(), facilityId: facility, inputProducts: blendInputs.map(row => `${row.commodityId}:${row.product}`), outputCommodityId: blendOutput, effectiveFrom: iso(effective), evidenceId: evidence })}>
      {blendInputs.map((row, index) => <fieldset key={index}><legend>Approved input product {index + 1}</legend><label>Commodity<Choice rows={data.commodities} value={row.commodityId} onChange={value => setBlendInputs(rows => rows.map((x, i) => i === index ? { ...x, commodityId: value } : x))} /></label><label>Product identity<Enum values={products} value={row.product} onChange={value => setBlendInputs(rows => rows.map((x, i) => i === index ? { ...x, product: value } : x))} /></label><button type="button" disabled={blendInputs.length === 1} onClick={() => setBlendInputs(rows => rows.filter((_, i) => i !== index))}>Remove product</button></fieldset>)}<button type="button" onClick={() => setBlendInputs(rows => [...rows, { commodityId: '', product: 'HEAD_RICE' }])}>Add approved product</button><label>Output commodity<Choice rows={data.commodities} value={blendOutput} onChange={setBlendOutput} /></label><button disabled={action.busy || !facility || !evidence}>Record compatibility version</button></form>
    </Panel><Panel title="Recorded compartment clearance"><p>Only empty compartments can clear ownership. Occupied stock never changes owner here.</p><form onSubmit={e => void save(e, `locations/${location}/clearance`, { evidenceId: clearEvidence, reason: clearReason })}><label>Compartment<Choice rows={data.locations.filter(row => row.kind === 'COMPARTMENT')} value={location} onChange={value => { setLocation(value); setClearEvidence(''); }} /></label><Evidence ownerId={owner} facilityId={clearLocation?.facilityId || ''} value={clearEvidence} onChange={setClearEvidence} /><label>Clearance reason<textarea required value={clearReason} onChange={e => setClearReason(e.target.value)} /></label><button disabled={action.busy || !clearEvidence}>Record clearance</button></form></Panel></div>}
  </>;
}
