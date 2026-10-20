import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api } from './api';
import { Feedback, Pagination, Panel, mutate, useAction, type Page } from './ledger-ui';

type Scope = 'SYSTEM_DEFAULT' | 'COMPANY_OVERRIDE';
type Settings = { systemDefault: { percentage: string; version: string }; companyOverrides: { companyId: string; companyName: string; percentage: string; version: string }[] };
type Company = { id: string; name: string };
type HistoryItem = { revision: string; companyId: string | null; companyName: string | null; scope: Scope; action: string; oldPercentage: string | null; newPercentage: string | null; actorId: string | null; actorName: string | null; recordedAt: string; reason: string; migrationId: string | null };
type OwnThreshold = { companyId: string; percentage: string; source: Scope; revision: string };
const emptyPage = <T,>(): Page<T> => ({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false });
const pctPattern = '[0-9]+(\\.[0-9]{1,6})?';
const sourceName = (source: Scope) => source === 'COMPANY_OVERRIDE' ? 'Company override' : 'System default';

export function OwnReceivingThreshold({ active }: { active: boolean }) {
  const action = useAction();
  const [threshold, setThreshold] = useState<OwnThreshold | null>(null);
  useEffect(() => {
    if (!active) return;
    let live = true;
    void action.run(async () => { const value = await api<OwnThreshold>('/api/ledger/receiving-thresholds/own-company'); if (live) setThreshold(value); });
    return () => { live = false; };
  }, [active]);
  return <Panel title="Your receiving loss threshold"><Feedback {...action} />{threshold ? <><p><strong>{threshold.percentage}%</strong> · {sourceName(threshold.source)}</p><p className="small">Revision {threshold.revision}. Applies per delivery line; this setting is advisory and does not guarantee arrival acceptance.</p></> : !action.error && <p role="status">Loading your company threshold…</p>}</Panel>;
}

export function ReceivingThresholdSettings({ active }: { active: boolean }) {
  const action = useAction();
  const [settings, setSettings] = useState<Settings | null>(null), [defaultPercent, setDefaultPercent] = useState(''), [defaultReason, setDefaultReason] = useState('');
  const [companySearch, setCompanySearch] = useState(''), [companyPage, setCompanyPage] = useState<Page<Company>>(emptyPage()), [companyId, setCompanyId] = useState(''), [companyName, setCompanyName] = useState('');
  const [percent, setPercent] = useState(''), [reason, setReason] = useState('');
  const [historyPage, setHistoryPage] = useState<Page<HistoryItem>>(emptyPage()), [historyScope, setHistoryScope] = useState(''), [historyCompany, setHistoryCompany] = useState('');
  const requestSequence = useRef({ companies: 0, history: 0 });
  async function loadSettings() { const value = await api<Settings>('/api/ledger/receiving-thresholds'); setSettings(value); setDefaultPercent(value.systemDefault.percentage); }
  async function loadCompanies(page = companyPage.page, search = companySearch) {
    const request = ++requestSequence.current.companies, query = new URLSearchParams({ page: String(page), pageSize: '20' });
    if (search.trim()) query.set('search', search.trim());
    try {
      const result = await api<Page<Company>>(`/api/ledger/receiving-thresholds/companies?${query}`);
      if (request === requestSequence.current.companies) setCompanyPage(result);
    } catch (error) {
      if (request === requestSequence.current.companies) throw error;
    }
  }
  async function loadHistory(page = historyPage.page) {
    const request = ++requestSequence.current.history, query = new URLSearchParams({ page: String(page), pageSize: '20' });
    if (historyScope) query.set('scope', historyScope); if (historyCompany) query.set('companyId', historyCompany);
    try {
      const result = await api<Page<HistoryItem>>(`/api/ledger/receiving-thresholds/history?${query}`);
      if (request === requestSequence.current.history) setHistoryPage(result);
    } catch (error) {
      if (request === requestSequence.current.history) throw error;
    }
  }
  useEffect(() => { if (active) void action.run(loadSettings); }, [active]);
  useEffect(() => { if (active) void action.run(() => loadCompanies(1)); }, [active, companySearch]);
  useEffect(() => { if (active) void action.run(() => loadHistory(1)); }, [active, historyScope, historyCompany]);
  const selectedOverride = settings?.companyOverrides.find(row => row.companyId === companyId);
  const companyOptions = companyPage.items.slice(), seenCompanies = new Set(companyPage.items.map(company => company.id));
  for (const row of settings?.companyOverrides || []) if (!seenCompanies.has(row.companyId)) { seenCompanies.add(row.companyId); companyOptions.push({ id: row.companyId, name: row.companyName }); }
  if (companyId && !seenCompanies.has(companyId)) companyOptions.push({ id: companyId, name: selectedOverride?.companyName || companyName || companyId });
  async function saveDefault(event: FormEvent) {
    event.preventDefault();
    await action.run(async () => { await mutate('/api/ledger/receiving-thresholds/default', { percentage: defaultPercent, version: settings?.systemDefault.version, reason: defaultReason }, 'PUT'); setDefaultReason(''); await loadSettings(); await loadHistory(1); }, 'System default updated.');
  }
  async function saveOverride(event: FormEvent) {
    event.preventDefault(); if (!companyId) return;
    await action.run(async () => { const route = `/api/ledger/receiving-thresholds/companies/${encodeURIComponent(companyId)}`; await mutate(route, { percentage: percent, reason, ...(selectedOverride ? { version: selectedOverride.version } : {}) }, selectedOverride ? 'PUT' : 'POST'); setReason(''); await loadSettings(); await loadCompanies(); await loadHistory(1); }, selectedOverride ? 'Company override updated.' : 'Company override created.');
  }
  async function removeOverride(company: Settings['companyOverrides'][number]) {
    if (!window.confirm(`Remove receiving threshold override for ${company.companyName}?`)) return;
    await action.run(async () => { await mutate(`/api/ledger/receiving-thresholds/companies/${encodeURIComponent(company.companyId)}`, { version: company.version, reason }, 'DELETE'); setReason(''); await loadSettings(); await loadHistory(1); }, 'Company override removed; future arrivals use current system default.');
  }
  useEffect(() => { setPercent(selectedOverride?.percentage || ''); }, [selectedOverride?.companyId, selectedOverride?.percentage]);
  return <section aria-label="Receiving loss thresholds"><h3>Receiving loss thresholds</h3><p>Threshold applies to each delivery line. It only controls operator escalation; accepted stock always uses measured arrival mass.</p><Feedback {...action} />
    {!settings && !action.error && <p role="status">Loading receiving thresholds…</p>}
    {settings && <div className="ledger-grid"><Panel title="System default"><p>New companies and companies without an override use this value. Initial setting: 2%.</p><form onSubmit={saveDefault}><label>Default threshold (%)<input required inputMode="decimal" pattern={pctPattern} value={defaultPercent} onChange={event => setDefaultPercent(event.target.value)} /></label><p className="field-help">Enter 0 to require exact matches. Maximum 6 decimal places; must be below 100.</p><label>Reason for change<textarea required value={defaultReason} onChange={event => setDefaultReason(event.target.value)} /></label><button disabled={action.busy || !defaultReason.trim()}>Save system default</button></form></Panel>
      <Panel title="Company overrides"><label>Find customer company<input type="search" value={companySearch} onChange={event => { setCompanySearch(event.target.value); setCompanyPage(current => ({ ...current, page: 1 })); }} /></label><label>Customer company<select value={companyId} onChange={event => { setCompanyId(event.target.value); setCompanyName(companyOptions.find(company => company.id === event.target.value)?.name || ''); }}><option value="">Select customer company…</option>{companyOptions.map(company => <option key={company.id} value={company.id}>{company.name}</option>)}</select></label><Pagination page={companyPage.page} pageSize={companyPage.pageSize} totalCount={companyPage.totalCount} onChange={page => void action.run(() => loadCompanies(page))} label="customer companies" busy={action.busy} />
        {companyId && <form onSubmit={saveOverride}><label>Threshold for selected company (%)<input required inputMode="decimal" pattern={pctPattern} value={percent} onChange={event => setPercent(event.target.value)} /></label><p className="field-help">0 is valid. Maximum 6 decimal places; must be below 100.</p><label>Reason for change<textarea required value={reason} onChange={event => setReason(event.target.value)} /></label><button disabled={action.busy || !reason.trim()}>{selectedOverride ? 'Update company override' : 'Create company override'}</button>{selectedOverride && <button type="button" className="secondary" disabled={action.busy || !reason.trim()} onClick={() => void removeOverride(selectedOverride)}>Remove override</button>}</form>}
        <div className="table-scroll"><table><caption>Active company overrides</caption><thead><tr><th scope="col">Company</th><th scope="col">Threshold</th><th scope="col">Revision</th></tr></thead><tbody>{settings.companyOverrides.map(row => <tr key={row.companyId}><td>{row.companyName}</td><td>{row.percentage}%</td><td>{row.version}</td></tr>)}</tbody></table></div>
      </Panel></div>}
    <Panel title="Configuration history"><p>Committed changes only. Initial 2% default shows migration origin; entries cannot be edited or removed.</p><div className="list-controls"><label>Filter by scope<select value={historyScope} onChange={event => setHistoryScope(event.target.value)}><option value="">All scopes</option><option value="SYSTEM_DEFAULT">System default</option><option value="COMPANY_OVERRIDE">Company override</option></select></label><label>Filter by company<select value={historyCompany} onChange={event => setHistoryCompany(event.target.value)}><option value="">All companies</option>{companyOptions.map(company => <option key={company.id} value={company.id}>{company.name}</option>)}</select></label></div><div className="table-scroll"><table><caption>Receiving threshold configuration history</caption><thead><tr><th scope="col">Revision</th><th scope="col">Recorded</th><th scope="col">Scope / company</th><th scope="col">Action</th><th scope="col">Before → after</th><th scope="col">Actor</th><th scope="col">Reason / origin</th></tr></thead><tbody>{historyPage.items.map(row => { const absent = row.scope === 'COMPANY_OVERRIDE' ? 'No override' : '—'; return <tr key={row.revision}><td>{row.revision}</td><td>{new Date(row.recordedAt).toLocaleString()}</td><td>{row.scope === 'SYSTEM_DEFAULT' ? 'System default' : `Company override · ${row.companyName || 'Customer company'}`}</td><td>{row.action}</td><td>{row.oldPercentage === null ? absent : `${row.oldPercentage}%`} → {row.newPercentage === null ? absent : `${row.newPercentage}%`}</td><td>{row.actorName || (row.migrationId ? 'Migration initialization' : 'Unavailable')}{row.actorId && <span className="table-subtitle">{row.actorId}</span>}</td><td>{row.reason}{row.migrationId && <span className="table-subtitle">Migration: {row.migrationId}</span>}</td></tr>; })}</tbody></table></div>{historyPage.items.length === 0 && <p className="empty">No threshold changes match these filters.</p>}<Pagination page={historyPage.page} pageSize={historyPage.pageSize} totalCount={historyPage.totalCount} onChange={page => void action.run(() => loadHistory(page))} label="threshold history" busy={action.busy} /></Panel>
  </section>;
}
