import { useEffect, useState, type FormEvent } from 'react';
import { api, errorMessage, roleLabel, type Organization, type Role, type User } from './api';

export function Administration({ currentUser }: { currentUser: User }) {
  const [users, setUsers] = useState<User[]>([]);
  const [organizations, setOrganizations] = useState<Organization[]>([]);
  const [roles, setRoles] = useState<Role[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<User | null>(null);
  const [newName, setNewName] = useState('');
  const [newKind, setNewKind] = useState<Organization['kind']>('Customer');
  const [creating, setCreating] = useState(false);
  const [notice, setNotice] = useState('');

  async function load() {
    setLoading(true); setError('');
    try {
      const [nextUsers, nextOrganizations, nextRoles] = await Promise.all([
        api<User[]>('/api/admin/users'), api<Organization[]>('/api/admin/organizations'), api<Role[]>('/api/admin/roles'),
      ]);
      setUsers(nextUsers); setOrganizations(nextOrganizations); setRoles(nextRoles); setSelected(null);
    } catch (e) { setError(errorMessage(e)); }
    finally { setLoading(false); }
  }
  useEffect(() => { void load(); }, []);

  async function createOrganization(event: FormEvent) {
    event.preventDefault(); setCreating(true); setError(''); setNotice('');
    try {
      const organization = await api<Organization>('/api/admin/organizations', { method: 'POST', body: JSON.stringify({ name: newName.trim(), kind: newKind }) });
      setOrganizations(previous => [...previous, organization]); setNewName(''); setNotice('Organization created. You can now assign accounts to it.');
    } catch (e) { setError(errorMessage(e)); }
    finally { setCreating(false); }
  }

  return <>
    <div className="page-heading"><span className="eyebrow">PLATFORM ADMINISTRATION</span><h1>People & organizations</h1><p className="muted">Give each person the right access to their organization.</p></div>
    {error && <div className="notice error" role="alert">{error}</div>}
    {notice && <div className="notice" role="status">{notice}</div>}
    <section className="panel users-panel"><div className="section-toolbar"><div><h2>Accounts</h2><p className="muted">Accounts appear after Google sign-in or email verification.</p></div><button className="secondary" disabled={loading} onClick={() => void load()}>Reload accounts</button></div>
      <label className="search-label">Find an account<input type="search" placeholder="Search by name or email" value={query} onChange={e => setQuery(e.target.value)} /></label>
      {loading ? <p role="status">Loading accounts…</p> : <div className="table-scroll"><table><thead><tr><th>Person</th><th>Organization</th><th>Role</th><th>Status</th><th><span className="sr-only">Actions</span></th></tr></thead><tbody>
        {users.filter(u => `${u.displayName} ${u.email}`.toLowerCase().includes(query.toLowerCase())).map(u => <tr key={u.id}><td><strong>{u.displayName}</strong><span className="table-subtitle">{u.email}</span></td><td>{u.organizationName || 'Not assigned'}</td><td>{roleLabel(u.role)}</td><td><span className={`badge ${!u.isActive ? 'inactive' : ''}`}>{!u.isActive ? 'Disabled' : u.role ? 'Active' : 'Pending'}</span></td><td><button className="text-button" onClick={() => setSelected(u)} aria-label={`Manage access for ${u.displayName}`}>Manage</button></td></tr>)}
      </tbody></table>{!users.some(u => `${u.displayName} ${u.email}`.toLowerCase().includes(query.toLowerCase())) && <p className="empty">No matching accounts.</p>}</div>}
    </section>
    {selected && <AccessEditor key={selected.id + selected.etag} user={selected} currentUser={currentUser} organizations={organizations} roles={roles} onCancel={() => setSelected(null)} onSaved={async () => { setNotice('Account access updated. Existing sessions for that account have been revoked.'); await load(); }} />}
    <section className="panel organization-panel"><h2>Organizations</h2><p className="muted">Mill roles belong to the mill. Customer roles belong to a customer company.</p>
      <ul className="organization-list">{organizations.map(o => <li key={o.id}><strong>{o.name}</strong><span className="badge">{o.kind}</span></li>)}</ul>
      <form className="organization-form" onSubmit={createOrganization}><label>Organization name<input value={newName} onChange={e => setNewName(e.target.value)} required maxLength={150} disabled={creating} /></label><label>Type<select value={newKind} onChange={e => setNewKind(e.target.value as Organization['kind'])} disabled={creating}><option value="Customer">Customer company</option><option value="Mill">Mill</option></select></label><button disabled={creating || !newName.trim()}>{creating ? 'Creating…' : 'Add organization'}</button></form>
    </section>
  </>;
}

function AccessEditor({ user, currentUser, organizations, roles, onCancel, onSaved }: { user: User; currentUser: User; organizations: Organization[]; roles: Role[]; onCancel: () => void; onSaved: () => Promise<void> }) {
  const [organizationId, setOrganizationId] = useState(user.organizationId || '');
  const [role, setRole] = useState(user.role || '');
  const [isActive, setIsActive] = useState(user.isActive);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const kind = organizations.find(o => o.id === organizationId)?.kind;
  const isSelf = user.id === currentUser.id;

  async function submit(event: FormEvent) {
    event.preventDefault(); setBusy(true); setError('');
    try {
      await api<User>(`/api/admin/users/${encodeURIComponent(user.id)}/access`, {
        method: 'PUT', headers: { 'If-Match': user.etag || '' },
        body: JSON.stringify({ organizationId: organizationId || null, role: role || null, isActive }),
      });
      await onSaved();
    } catch (e) { setError(errorMessage(e)); }
    finally { setBusy(false); }
  }
  return <section className="panel access-editor" aria-label={`Access settings for ${user.displayName}`}><h2>Manage access · {user.displayName}</h2><p className="muted">{user.email}</p>
    {isSelf && <p className="notice">Your own administrator access cannot be changed here. Ask another administrator if it needs to change.</p>}
    <form onSubmit={submit}><div className="form-columns"><label>Organization<select value={organizationId} disabled={busy || isSelf} onChange={e => { setOrganizationId(e.target.value); setRole(''); }}><option value="">Not assigned</option>{organizations.map(o => <option key={o.id} value={o.id}>{o.name} ({o.kind})</option>)}</select></label><label>Role<select value={role} disabled={busy || isSelf || !organizationId} onChange={e => setRole(e.target.value)}><option value="">Not assigned</option>{roles.filter(r => r.organizationKind === kind).map(r => <option key={r.name} value={r.name}>{roleLabel(r.name)}</option>)}</select></label></div>
      <label className="checkbox-label"><input type="checkbox" checked={isActive} disabled={busy || isSelf} onChange={e => setIsActive(e.target.checked)} /> Account enabled</label>
      <p className="small muted">Saving access changes signs this person out on all devices. Disabling their account also prevents new sign-ins.</p>
      {error && <div className="notice error" role="alert">{error}</div>}
      <div className="form-footer"><button type="button" className="secondary" onClick={onCancel} disabled={busy}>Cancel</button><button disabled={busy || isSelf || Boolean(organizationId) !== Boolean(role)}>{busy ? 'Saving…' : 'Save access'}</button></div>
    </form>
  </section>;
}
