import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError, clearLocalSession, errorMessage, hasLocalSession, roleLabel, type User } from './api';
import { Administration } from './Administration';
import { googleLoginError } from './auth-errors';
import { LocalAuth } from './LocalAuth';

export function App() {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [tab, setTab] = useState<'profile' | 'administration'>('profile');
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
    const expired = () => { setUser(null); setTab('profile'); };
    window.addEventListener('session-ended', expired);
    return () => window.removeEventListener('session-ended', expired);
  }, []);

  async function logout(all = false) {
    setSigningOut(true);
    setError('');
    try {
      await api<void>(all ? '/api/auth/logout-all' : '/api/auth/logout', { method: 'POST' });
      clearLocalSession();
      setUser(null);
      setTab('profile');
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

  return <div className="workspace">
    <aside className="sidebar">
      <Brand />
      <p className="nav-caption">WORKSPACE</p>
      <nav aria-label="Main navigation">
        <button aria-current={tab === 'profile' ? 'page' : undefined} onClick={() => setTab('profile')}><span aria-hidden="true">◉</span> My profile</button>
        {user.role === 'Administrator' && <button aria-current={tab === 'administration' ? 'page' : undefined} onClick={() => setTab('administration')}><span aria-hidden="true">▦</span> Administration</button>}
      </nav>
      <div className="sidebar-bottom"><span className="avatar">{user.displayName.slice(0, 1).toUpperCase()}</span><div><strong>{user.displayName}</strong><span>{roleLabel(user.role)}</span></div></div>
    </aside>
    <div className="workspace-body">
      <header className="topbar"><span>{user.organizationName || 'Personal account'} <span className="separator">/</span> {tab === 'profile' ? 'My profile' : 'Administration'}</span><button className="text-button" disabled={signingOut} onClick={() => void logout()}>Sign out <span aria-hidden="true">↗</span></button></header>
      <main className="content">
        {error && <div className="notice error" role="alert">{error}</div>}
        {tab === 'administration' && user.role === 'Administrator' ? <Administration currentUser={user} />
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
