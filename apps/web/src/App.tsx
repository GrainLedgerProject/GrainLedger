import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, clearLocalSession, errorMessage, hasLocalSession, roleLabel, type User } from './api';
import { Administration } from './Administration';
import { Farmers } from './Farmers';
import { PlotDetail } from './PlotDetail';
import { PlotSeasonDetail } from './PlotSeasonDetail';
import { SprayDetail } from './Spray';
import { Procurements, ProcurementDetail, SourceLotDetail } from './Procurement';
import { ProcurementForm } from './ProcurementForm';
import { SampleDetail, SampleList } from './Samples';
import { googleLoginError } from './auth-errors';
import { LocalAuth } from './LocalAuth';

type Tab = 'profile' | 'administration' | 'farmers' | 'procurements' | 'samples';
function tabForPath(path: string): Tab {
  if (path.startsWith('/capture/procurements') || path.startsWith('/capture/source-lots/')) return 'procurements';
  if (path.startsWith('/capture/samples')) return 'samples';
  if (path.startsWith('/capture/')) return 'farmers';
  return 'profile';
}

export function App() {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [tab, setTab] = useState<Tab>(tabForPath(window.location.pathname));
  const [path, setPath] = useState(window.location.pathname);
  const [signingOut, setSigningOut] = useState(false);

  async function loadSession() {
    setLoading(true);
    setError('');
    try { setUser(await api<User>('/api/auth/session')); }
    catch (e) { if (!(e instanceof ApiError && e.status === 401)) setError(errorMessage(e)); }
    finally { setLoading(false); }
  }

  useEffect(() => {
    void loadSession();
    const expired = () => { setUser(null); setTab('profile'); setPath('/'); window.history.replaceState({}, '', '/'); };
    window.addEventListener('session-ended', expired);
    const popped = () => {
      setPath(window.location.pathname);
      setTab(tabForPath(window.location.pathname));
    };
    window.addEventListener('popstate', popped);
    return () => { window.removeEventListener('session-ended', expired); window.removeEventListener('popstate', popped); };
  }, []);

  function openTab(next: Tab) {
    const nextPath = next === 'farmers' ? '/capture/farmers' : next === 'procurements' ? '/capture/procurements' : next === 'samples' ? '/capture/samples' : '/';
    window.history.pushState({}, '', nextPath);
    setPath(nextPath);
    setTab(next);
  }

  function selectFarmer(id: string | null) {
    const nextPath = id ? `/capture/farmers/${encodeURIComponent(id)}` : '/capture/farmers';
    window.history.pushState({}, '', nextPath);
    setPath(nextPath);
    setTab('farmers');
  }

  function selectPlot(id: string) {
    const nextPath = `/capture/plots/${encodeURIComponent(id)}`;
    window.history.pushState({}, '', nextPath);
    setPath(nextPath);
    setTab('farmers');
  }

  function selectPlotSeason(id: string) {
    const nextPath = `/capture/plot-seasons/${encodeURIComponent(id)}`;
    window.history.pushState({}, '', nextPath);
    setPath(nextPath);
    setTab('farmers');
  }

  function selectSpray(id: string) {
    const nextPath = `/capture/spray-declarations/${encodeURIComponent(id)}`;
    window.history.pushState({}, '', nextPath); setPath(nextPath); setTab('farmers');
  }

  function openCapturePath(nextPath: string, nextTab: Tab) {
    window.history.pushState({}, '', nextPath); setPath(nextPath); setTab(nextTab);
  }

  async function logout(all = false) {
    setSigningOut(true);
    setError('');
    try {
      await api<void>(all ? '/api/auth/logout-all' : '/api/auth/logout', { method: 'POST' });
      clearLocalSession();
      setUser(null);
      setTab('profile');
      setPath('/');
      window.history.replaceState({}, '', '/');
    } catch (e) { setError(errorMessage(e)); }
    finally { setSigningOut(false); }
  }

  const oauthError = googleLoginError(window.location.search);
  if (loading) return <div className="loading" role="status">Opening GrainLedger…</div>;

  if (!user) return <main className="login-layout">
    <section className="login-story">
      <Brand />
      <div><span className="eyebrow light">FROM FIELD TO SHIPMENT</span>
        <h1>Every grain.<br />Accounted for.</h1>
        <p>A shared record of the journey from harvest to export. One workspace for the people behind it.</p>
      </div>
      <p className="story-footer">GrainLedger <span>Traceability starts with trust.</span></p>
    </section>
    <section className="login-form">
      <LocalAuth oauthError={oauthError} connectionError={error} onAuthenticated={setUser} onRetry={() => void loadSession()} />
    </section>
  </main>;

  const canReadFarmers = user.permissions.includes('farms.read') || user.permissions.includes('farms.write');
  const canWritePurchases = user.permissions.includes('purchases.write');
  const canReadPurchases = canWritePurchases || user.permissions.includes('lots.read.tenant');
  const canReadSamples = canWritePurchases || user.permissions.includes('farms.read');
  const farmerPathId = path.match(/^\/capture\/farmers\/([^/]+)\/?$/)?.[1] ?? null;
  const selectedFarmerId = farmerPathId === 'new' ? null : farmerPathId;
  const selectedPlotId = path.match(/^\/capture\/plots\/([^/]+)\/?$/)?.[1] ?? null;
  const selectedPlotSeasonId = path.match(/^\/capture\/plot-seasons\/([^/]+)\/?$/)?.[1] ?? null;
  const selectedSprayId = path.match(/^\/capture\/spray-declarations\/([^/]+)\/?$/)?.[1] ?? null;
  const procurementPathId = path.match(/^\/capture\/procurements\/([^/]+)\/?$/)?.[1] ?? null;
  const selectedSourceLotId = path.match(/^\/capture\/source-lots\/([^/]+)\/?$/)?.[1] ?? null;
  const samplePathId = path.match(/^\/capture\/samples\/([^/]+)\/?$/)?.[1] ?? null;

  return <div className="workspace">
    <aside className="sidebar">
      <Brand />
      <p className="nav-caption">WORKSPACE</p>
      <nav aria-label="Main navigation">
        <button aria-current={tab === 'profile' ? 'page' : undefined} onClick={() => openTab('profile')}><span aria-hidden="true">◉</span> My profile</button>
        {user.role === 'Administrator' && <button aria-current={tab === 'administration' ? 'page' : undefined} onClick={() => openTab('administration')}><span aria-hidden="true">▦</span> Administration</button>}
        {canReadFarmers && <button aria-current={tab === 'farmers' ? 'page' : undefined} onClick={() => openTab('farmers')}><span aria-hidden="true">▤</span> Farmers</button>}
        {canReadPurchases && <button aria-current={tab === 'procurements' ? 'page' : undefined} onClick={() => openTab('procurements')}><span aria-hidden="true">▣</span> Paddy intake</button>}
        {canReadSamples && <button aria-current={tab === 'samples' ? 'page' : undefined} onClick={() => openTab('samples')}><span aria-hidden="true">◇</span> Samples</button>}
      </nav>
      <div className="sidebar-bottom"><span className="avatar">{user.displayName.slice(0, 1).toUpperCase()}</span><div><strong>{user.displayName}</strong><span>{roleLabel(user.role)}</span></div></div>
    </aside>
    <div className="workspace-body">
      <header className="topbar"><span>{user.organizationName || 'Personal account'} <span className="separator">/</span> {tab === 'profile' ? 'My profile' : tab === 'administration' ? 'Administration' : tab === 'farmers' ? 'Farmers' : tab === 'procurements' ? 'Paddy intake' : 'Samples'}</span><button className="text-button" disabled={signingOut} onClick={() => void logout()}>Sign out <span aria-hidden="true">↗</span></button></header>
      <main className="content">
        {error && <div className="notice error" role="alert">{error}</div>}
        {tab === 'administration' && user.role === 'Administrator' ? <Administration currentUser={user} />
          : tab === 'procurements' && selectedSourceLotId ? (canReadPurchases
            ? <SourceLotDetail id={selectedSourceLotId} canWriteSamples={canWritePurchases} userId={user.id} companyId={user.organizationId || ''} onBack={id => id ? openCapturePath(`/capture/procurements/${id}`, 'procurements') : openTab('procurements')} onOpenSample={id => openCapturePath(`/capture/samples/${id}`, 'samples')} />
            : <div className="notice error" role="alert">Your account cannot view source lots.</div>)
          : tab === 'procurements' && procurementPathId === 'new' ? (canWritePurchases
            ? <ProcurementForm userId={user.id} companyId={user.organizationId || ''} onBack={() => openTab('procurements')} onCreated={id => openCapturePath(`/capture/procurements/${id}`, 'procurements')} />
            : <div className="notice error" role="alert">Your account cannot record procurement.</div>)
          : tab === 'procurements' && procurementPathId ? (canReadPurchases
            ? <ProcurementDetail id={procurementPathId} onBack={() => openTab('procurements')} onOpenSourceLot={id => openCapturePath(`/capture/source-lots/${id}`, 'procurements')} />
            : <div className="notice error" role="alert">Your account cannot view procurement.</div>)
          : tab === 'procurements' ? (canReadPurchases
            ? <Procurements canCreate={canWritePurchases} onCreate={() => openCapturePath('/capture/procurements/new', 'procurements')} onOpen={id => openCapturePath(`/capture/procurements/${id}`, 'procurements')} />
            : <div className="notice error" role="alert">Your account cannot view procurement.</div>)
          : tab === 'samples' && samplePathId ? (canReadSamples
            ? <SampleDetail id={samplePathId} userId={user.id} companyId={user.organizationId || ''} canHandover={canWritePurchases} onBack={id => id && canReadPurchases ? openCapturePath(`/capture/source-lots/${id}`, 'procurements') : openTab('samples')} />
            : <div className="notice error" role="alert">Your account cannot view samples.</div>)
          : tab === 'samples' ? (canReadSamples
            ? <><div className="page-heading"><span className="eyebrow">SAMPLE CUSTODY</span><h1>Samples</h1><p className="muted">Reopen a sample to review or add handovers.</p></div><SampleList onOpen={id => openCapturePath(`/capture/samples/${id}`, 'samples')} /></>
            : <div className="notice error" role="alert">Your account cannot view samples.</div>)
          : tab === 'farmers' && selectedSprayId ? (canReadFarmers
            ? <SprayDetail id={selectedSprayId} onBack={plotSeasonId => plotSeasonId ? selectPlotSeason(plotSeasonId) : openTab('farmers')} />
            : <div className="notice error" role="alert">Your account does not have access to pesticide declarations.</div>)
          : tab === 'farmers' && selectedPlotSeasonId ? (canReadFarmers
            ? <PlotSeasonDetail id={selectedPlotSeasonId} onBack={plotId => plotId ? selectPlot(plotId) : openTab('farmers')} onOpenSpray={selectSpray} canCreate={user.permissions.includes('farms.write')} userId={user.id} companyId={user.organizationId || ''} />
            : <div className="notice error" role="alert">Your account does not have access to plot-season records.</div>)
          : tab === 'farmers' && selectedPlotId ? (canReadFarmers
            ? <PlotDetail id={selectedPlotId} onBack={selectFarmer} onOpenPlotSeason={selectPlotSeason} canCreate={user.permissions.includes('farms.write')} userId={user.id} companyId={user.organizationId || ''} />
            : <div className="notice error" role="alert">Your account does not have access to plot records.</div>)
          : tab === 'farmers' ? (canReadFarmers
            ? <Farmers key={`${user.id}:${user.organizationId}`} selectedId={selectedFarmerId} showCreateInitially={farmerPathId === 'new'} onSelect={selectFarmer} onOpenPlot={selectPlot} canCreate={user.permissions.includes('farms.write')} userId={user.id} companyId={user.organizationId || ''} />
            : <div className="notice error" role="alert">Your account does not have access to farmer records.</div>)
            : <Profile user={user} onUpdate={setUser} onReload={() => void loadSession()} onLogoutAll={() => void logout(true)} signingOut={signingOut} />}
      </main>
    </div>
  </div>;
}

function Brand() { return <div className="brand"><span className="brand-icon" aria-hidden="true">❧</span> GrainLedger<span className="brand-dot">.</span></div>; }

function Profile({ user, onUpdate, onReload, onLogoutAll, signingOut }: { user: User; onUpdate: (user: User) => void; onReload: () => void; onLogoutAll: () => void; signingOut: boolean }) {
  const [name, setName] = useState(user.displayName);
  const [phone, setPhone] = useState(user.phoneNumber || '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [saved, setSaved] = useState(false);

  async function save(event: FormEvent) {
    event.preventDefault();
    setBusy(true); setError(''); setSaved(false);
    try {
      const updated = await api<User>('/api/profile', { method: 'PUT', body: JSON.stringify({ displayName: name.trim(), phoneNumber: phone.trim() || null }) });
      onUpdate(updated); setName(updated.displayName); setPhone(updated.phoneNumber || ''); setSaved(true);
    } catch (e) { setError(errorMessage(e)); }
    finally { setBusy(false); }
  }

  return <>
    <div className="page-heading"><span className="eyebrow">ACCOUNT SETTINGS</span><h1>My profile</h1><p className="muted">Manage your personal details and review your workspace access.</p></div>
    {!user.role && <div className="notice pending"><strong>Your account is ready. Workspace access is next.</strong><p>Ask your administrator to assign your organization and role. You can update your profile while you wait.</p><button className="text-button" onClick={onReload}>Refresh access</button></div>}
    <div className="profile-grid">
      <section className="panel"><div className="panel-heading"><h2>Personal details</h2><p className="muted">How your team sees you in GrainLedger.</p></div>
        <form onSubmit={save}>
          <div className="identity-preview"><span className="avatar large">{user.displayName.slice(0, 1).toUpperCase()}</span><div><strong>{user.displayName}</strong><span className="muted">GrainLedger account</span></div></div>
          <label>Full name<input autoComplete="name" value={name} onChange={e => { setName(e.target.value); setSaved(false); }} required maxLength={100} disabled={busy} /></label>
          <label>Email address<input type="email" value={user.email} readOnly aria-describedby="email-help" /></label>
          <p className="field-help" id="email-help">This is the verified address for this GrainLedger account.</p>
          <label>Phone number <span className="optional">(optional)</span><input type="tel" autoComplete="tel" placeholder="+84 …" value={phone} onChange={e => { setPhone(e.target.value); setSaved(false); }} maxLength={30} disabled={busy} /></label>
          {error && <div className="notice error" role="alert">{error}</div>}
          <div className="form-footer"><span role="status" className="success">{saved ? 'Changes saved.' : ''}</span><button type="submit" disabled={busy || !name.trim()}>{busy ? 'Saving…' : 'Save changes'}</button></div>
        </form>
      </section>
      <div className="stack">
        <section className="panel"><div className="panel-heading"><h2>Workspace access</h2><p className="muted">Assigned by your administrator.</p></div><dl><dt>Organization</dt><dd>{user.organizationName || 'Not assigned'}</dd><dt>Role</dt><dd><span className="badge">{roleLabel(user.role)}</span></dd><dt>Account status</dt><dd><span className="status-dot" /> {user.role ? 'Active' : 'Pending assignment'}</dd></dl>
          {user.permissions.length > 0 && <details><summary>Your permissions ({user.permissions.length})</summary><ul className="permissions">{user.permissions.map(p => <li key={p}>{p}</li>)}</ul></details>}
        </section>
        <section className="panel security"><h2>Sign-in & security</h2><p className="muted">Your current session uses {hasLocalSession() ? 'email and password with rotating security tokens' : 'Google sign-in'}. Use the button below if you no longer trust another signed-in device.</p><button className="secondary" disabled={signingOut} onClick={onLogoutAll}>{signingOut ? 'Signing out…' : 'Sign out on all devices'}</button></section>
      </div>
    </div>
  </>;
}
