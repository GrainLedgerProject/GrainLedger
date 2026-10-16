import { apiFetch } from './client';

export interface CreateOrganizationRequest {
  name: string;
  kind: 'Mill' | 'Customer';
}

export interface OrganizationDto {
  id: string;
  name: string;
  kind: 'Mill' | 'Customer';
  createdAt?: string;
}

export interface AdminUserDto {
  id: string;
  email: string;
  displayName?: string;
  role?: string | null;
  organizationId?: string | null;
  organizationName?: string | null;
  isActive: boolean;
  etag?: string;
  updatedAt?: string;
}

export interface UpdateAccessRequest {
  role?: string | null;
  organizationId?: string | null;
  isActive: boolean;
}

export const adminEndpoints = {
  getRoles: () => apiFetch<unknown>('/api/admin/roles'),

  getOrganizations: () => apiFetch<unknown>('/api/admin/organizations'),
  createOrganization: (req: CreateOrganizationRequest) =>
    apiFetch<unknown>('/api/admin/organizations', {
      method: 'POST',
      body: JSON.stringify(req),
    }),

  getUsers: () => apiFetch<unknown>('/api/admin/users'),
  updateUserAccess: (id: string, ifMatchEtag: string, req: UpdateAccessRequest) =>
    apiFetch<unknown>(`/api/admin/users/${id}/access`, {
      method: 'PUT',
      headers: {
        'If-Match': ifMatchEtag,
      },
      body: JSON.stringify(req),
    }),
};
