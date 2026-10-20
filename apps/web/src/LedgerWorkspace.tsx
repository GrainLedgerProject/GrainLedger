import { useState } from 'react';
import { type User } from './api';
import { LedgerOperations } from './LedgerOperations';
import { LedgerReceiving } from './LedgerReceiving';
import { LedgerSetup } from './LedgerSetup';
import { LedgerStock } from './LedgerStock';
import { LedgerTraceability } from './LedgerTraceability';
import { OwnReceivingThreshold } from './ReceivingThresholds';

export const ledgerSections = [
  { id: 'receiving', label: 'Receive stock', description: 'Procurement & mill arrivals', permissions: ['farms.read', 'farms.write', 'purchases.write', 'platform.configure', 'lots.receive', 'receiving.reconcile'] },
  { id: 'inventory', label: 'Stock & HOLDs', description: 'Available stock, alerts & history', permissions: ['lots.read.all', 'lots.read.facility', 'lots.read.tenant', 'purchases.write'] },
  { id: 'traceability', label: 'Traceability', description: 'Lot origins & affected lots', permissions: ['trace.read.tenant', 'trace.read.all'] },
  { id: 'operations', label: 'Process & correct', description: 'Processing, transfers & corrections', permissions: ['conversions.read', 'conversions.write', 'conversions.confirm', 'corrections.reconcile'] },
  { id: 'setup', label: 'Setup', description: 'Catalog, facilities & policies', permissions: ['platform.read', 'platform.configure'] },
] as const;
export function LedgerWorkspace({ user }: { user: User }) {
  const allowed = ledgerSections.filter(section => section.permissions.some(permission => user.permissions.includes(permission)));
  const preferred = user.permissions.includes('platform.configure') ? 'setup'
    : user.permissions.some(permission => ['purchases.write', 'lots.receive', 'receiving.reconcile'].includes(permission)) ? 'receiving' : 'inventory';
  const initial = allowed.some(section => section.id === preferred) ? preferred : allowed[0]?.id || '';
  const [selected, setSelected] = useState<string>(initial);
  const [visited, setVisited] = useState<string[]>([initial]);
  const tab = allowed.some(section => section.id === selected) ? selected : allowed[0]?.id;
  function selectSection(section: string) {
    setSelected(section);
    setVisited(previous => previous.includes(section) ? previous : [...previous, section]);
  }
  return <div className="ledger-workspace">
    <header className="page-heading"><span className="eyebrow">OPERATIONS</span><h1>Ledger</h1><p className="muted">Receive, find and move stock. Choose a task to get started.</p></header>
    {['CustomerManager', 'PurchasingStaff', 'ExportStaff'].includes(user.role || '') && <OwnReceivingThreshold active />}
    <nav className="ledger-nav" aria-label="Ledger sections">{allowed.map(section => <button key={section.id} aria-label={section.label} aria-current={tab === section.id ? 'page' : undefined} aria-controls={`ledger-${section.id}`} onClick={() => selectSection(section.id)}><strong>{section.label}</strong><small>{section.description}</small></button>)}</nav>
    {allowed.map(section => <div key={section.id} id={`ledger-${section.id}`} hidden={tab !== section.id}>
      {(visited.includes(section.id) || tab === section.id) && (section.id === 'traceability' ? <LedgerTraceability user={user} active={tab === section.id} /> : section.id === 'inventory' ? <LedgerStock user={user} active={tab === section.id} /> : section.id === 'receiving' ? <LedgerReceiving user={user} active={tab === section.id} /> : section.id === 'operations' ? <LedgerOperations user={user} active={tab === section.id} /> : <LedgerSetup user={user} active={tab === section.id} />)}
    </div>)}
    {!allowed.length && <p className="notice">No ledger permissions assigned.</p>}
  </div>;
}
