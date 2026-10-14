import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { CatalogScreen } from './CatalogScreen';

const fetchMock = vi.fn<typeof fetch>();
const response = (body: unknown) => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } });
beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock); vi.stubGlobal('confirm', vi.fn(() => true)); vi.stubGlobal('prompt', vi.fn(() => 'Updated name')); fetchMock.mockReset();
  fetchMock.mockImplementation(async (url, init) => {
    const path = String(url).split('?')[0];
    if (init?.method && init.method !== 'GET') return new Response(null, { status: 204 });
    if (path === '/api/auth/csrf') return response({ token: 'csrf' });
    if (path === '/api/commodities') {
      const page = Number(new URLSearchParams(String(url).split('?')[1]).get('page') || '1');
      return response({ items: [{ id: `c${page}`, code: page === 1 ? 'PADDY' : 'RICE', name: page === 1 ? 'Paddy' : 'Rice', version: 3, createdBy: 'owner', canEdit: true, canDelete: true }], page, pageSize: 1, totalCount: 2, hasMore: page === 1 });
    }
    if (path === '/api/measurement-units') return response({ items: [], page: 1, pageSize: 25, totalCount: 0, hasMore: false });
    return response({});
  });
});
it('filters and sorts catalog list, edits/deletes with If-Match and renders server page count', async () => {
  const browser = userEvent.setup();
  render(<CatalogScreen />);
  expect(await screen.findByText('PADDY')).toBeInTheDocument();
  expect(screen.getByRole('navigation', { name: 'commodities pagination' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Page 1 of commodities' })).toHaveAttribute('aria-current', 'page');
  await browser.click(screen.getByRole('button', { name: 'Next commodities page' }));
  expect(await screen.findByText('RICE')).toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: 'Previous commodities page' }));
  await browser.type(screen.getByLabelText('Search commodities'), 'PADDY');
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).includes('q=PADDY'))).toBe(true));
  await browser.click(screen.getByRole('button', { name: 'Edit Paddy' }));
  await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => String(url) === '/api/commodities/c1' && init?.method === 'PATCH' && new Headers(init.headers).get('If-Match') === '"3"')).toBe(true));
  await browser.click(screen.getByRole('button', { name: 'Remove Paddy' }));
  await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => String(url) === '/api/commodities/c1' && init?.method === 'DELETE' && new Headers(init.headers).get('If-Match') === '"3"')).toBe(true));
  await browser.selectOptions(screen.getByLabelText('Sort commodities by'), 'name');
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).includes('sortBy=name'))).toBe(true));
});
