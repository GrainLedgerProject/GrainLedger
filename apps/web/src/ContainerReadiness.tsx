export type ContainerCheck = { ready: boolean; checkedAt?: string; scope?: 'LOADING_DRAFT' | 'CLOSURE'; stockChecksPerformed?: boolean; version: string;
  findings: { code: string; message: string; lotId?: string }[]; committedLoadedKg: string; draftKg: string; targetKg: string };

const groups = [
  { title: 'Stock and pool changes', codes: ['POSITION_UNAVAILABLE', 'POOL_CHANGED', 'POOL_REVISION_NOT_RECORDED', 'INSUFFICIENT_PHYSICAL_STOCK', 'LOT_RESTRICTED'], next: 'Reload stock, resolve restrictions and reselect changed positions before saving another draft.' },
  { title: 'Order and approved outputs', codes: ['ORDER_PLAN_AUTHORITY_UNAVAILABLE', 'ORDER_REFERENCE_MISSING'], next: 'A verified order and current approved output authority must be connected.' },
  { title: 'Reserved output stock', codes: ['OUTPUT_RESERVATION_UNAVAILABLE'], next: 'The matching output reservation and atomic consumption must be connected. A draft does not hold stock.' },
  { title: 'Export evidence and assessment', codes: ['EXPORT_ASSESSMENT_UNAVAILABLE'], next: 'Current loading or closure assessment is required. A residue interval alone is not export approval.' },
  { title: 'Container restrictions', codes: ['CONTAINER_RESTRICTIONS_UNAVAILABLE'], next: 'Container HOLD and blocking-state checks must be connected in addition to lot restrictions.' },
  { title: 'Container details and loaded mass', codes: ['SEAL_METADATA_MISSING', 'EXACT_TARGET_NOT_LOADED'], next: 'Complete required closure details. Only committed loading counts toward the target.' },
];

export function ContainerReadiness({ check }: { check: ContainerCheck }) {
  const known = new Set(groups.flatMap(group => group.codes));
  const other = check.findings.filter(finding => !known.has(finding.code));
  return <section role="status" aria-label="Container readiness" className="notice">
    {check.scope && <h3>{check.scope === 'LOADING_DRAFT' ? 'Saved loading draft check' : 'Closure readiness check'}</h3>}
    <p><strong>Blocked</strong> · Committed loaded: {check.committedLoadedKg} / {check.targetKg} kg</p>
    {check.checkedAt && <p>Checked: {new Date(check.checkedAt).toLocaleString()}. This observation does not reserve stock or authorize loading.</p>}
    {check.stockChecksPerformed && !check.findings.some(finding => groups[0].codes.includes(finding.code)) && <p>No stock, pool or lot-restriction findings were reported for this saved draft at this check. The required integrations below still block loading.</p>}
    {groups.map(group => {
      const findings = check.findings.filter(finding => group.codes.includes(finding.code));
      return findings.length ? <section key={group.title} aria-label={group.title}><h3>{group.title}</h3><ul>{findings.map((finding, index) => <li key={`${finding.code}/${index}`}>{finding.message}{finding.lotId && <small>Lot: {finding.lotId}</small>}</li>)}</ul><p>{group.next}</p></section> : null;
    })}
    {!!other.length && <section aria-label="Other checks"><h3>Other checks</h3><ul>{other.map((finding, index) => <li key={index}>{finding.message}</li>)}</ul></section>}
  </section>;
}
