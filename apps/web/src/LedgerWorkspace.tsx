import { CatalogScreen } from './CatalogScreen';
import { useState } from 'react';
import { type User } from './api';
import { LedgerOperations } from './LedgerOperations';
import { LedgerReceiving } from './LedgerReceiving';
import { LedgerSetup } from './LedgerSetup';
import { LedgerStock } from './LedgerStock';

export const ledgerSections = [
  { id: 'inventory', label: 'Stock & HOLD inbox', permissions: ['lots.read.all', 'lots.read.facility', 'lots.read.tenant', 'purchases.write'] },
  { id: 'receiving', label: 'Sources & receiving', permissions: ['farms.read', 'farms.write', 'purchases.write', 'lots.receive', 'receiving.reconcile', 'platform.configure'] },
  { id: 'operations', label: 'Processing & corrections', permissions: ['conversions.read', 'conversions.write', 'conversions.confirm', 'corrections.reconcile'] },
  { id: 'catalog', label: 'Catalog', permissions: ['platform.configure'] },
  { id: 'setup', label: 'Facility setup', permissions: ['platform.read', 'platform.configure'] },
] as const;
export function LedgerWorkspace({ user }: { user: User }) {
  const allowed = ledgerSections.filter(section => section.permissions.some(permission => user.permissions.includes(permission)));
  const [selected, setSelected] = useState<string>(allowed[0]?.id || '');
  const tab = allowed.some(section => section.id === selected) ? selected : allowed[0]?.id;
  return <div className="ledger-workspace"><header className="page-heading"><span className="eyebrow">OPERATIONS</span><h1>Ledger</h1><p className="muted">Immutable stock, source evidence, movements and restrictions.</p></header><nav className="ledger-nav" aria-label="Ledger sections">{allowed.map(section => <button key={section.id} aria-current={tab === section.id ? 'page' : undefined} onClick={() => setSelected(section.id)}>{section.label}</button>)}</nav>
    {tab === 'inventory' ? <LedgerStock user={user} /> : tab === 'receiving' ? <LedgerReceiving user={user} /> : tab === 'operations' ? <LedgerOperations user={user} /> : tab === 'catalog' ? <CatalogScreen /> : tab === 'setup' ? <LedgerSetup user={user} /> : <p className="notice">No ledger permissions assigned.</p>}
  </div>;
}
