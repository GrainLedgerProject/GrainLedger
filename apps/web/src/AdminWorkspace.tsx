import { type User } from './api';
import { Administration } from './Administration';
import { AdminCatalogs } from './AdminCatalogs';
import { AdminMrl } from './AdminMrl';
import { AdminCropSeasons } from './AdminCropSeasons';
import { MrlLookup } from './MrlLookup';

export type AdminSection = 'people' | 'catalogs' | 'mrl' | 'crop-seasons' | 'mrl-lookup';
export function AdminWorkspace({ user, section, onSelect }: { user: User; section: AdminSection; onSelect: (section: AdminSection) => void }) {
  const canConfigure = user.permissions.includes('platform.configure');
  return <div className="admin-workspace">
    <nav className="admin-subnav admin-main-nav" aria-label="Administration sections">
      <button type="button" aria-current={section === 'people' ? 'page' : undefined} onClick={() => onSelect('people')}>People & organizations</button>
      {canConfigure && <><button type="button" aria-current={section === 'catalogs' ? 'page' : undefined} onClick={() => onSelect('catalogs')}>Catalogs</button>
      <button type="button" aria-current={section === 'mrl' ? 'page' : undefined} onClick={() => onSelect('mrl')}>MRL tables</button></>}
      {canConfigure && <button type="button" aria-current={section === 'crop-seasons' ? 'page' : undefined} onClick={() => onSelect('crop-seasons')}>Crop seasons</button>}
      <button type="button" aria-current={section === 'mrl-lookup' ? 'page' : undefined} onClick={() => onSelect('mrl-lookup')}>Applicable MRL</button>
    </nav>
    {!canConfigure && section !== 'people' ? <div className="notice error" role="alert">This account cannot configure catalogs or MRL tables.</div>
      : section === 'people' ? <Administration currentUser={user} /> : section === 'catalogs' ? <AdminCatalogs /> : section === 'crop-seasons' ? <AdminCropSeasons /> : section === 'mrl-lookup' ? <MrlLookup /> : <AdminMrl onOpenCatalogs={() => onSelect('catalogs')} />}
  </div>;
}
