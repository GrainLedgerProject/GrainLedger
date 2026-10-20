import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { AdminCatalogs } from './AdminCatalogs';

const fetchMock = vi.fn<typeof fetch>();
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  fetchMock.mockImplementation(async (url, init) => {
    if (url === '/api/auth/csrf') return response({ token: 'csrf' });
    if (init?.method === 'POST') return response({ id: 'created' }, 201);
    return response({ items: [], page: 1, pageSize: 20, hasMore: false });
  });
});

it('opens Add as a modal for each admin catalog and keeps the list in place', async () => {
  const browser = userEvent.setup();
  render(<AdminCatalogs />);
  for (const [tab, singular] of [
    ['Active substances', 'active substance'],
    ['Export markets', 'market'],
    ['Rice varieties', 'rice variety'],
    ['Quality attributes', 'quality attribute'],
  ]) {
    await browser.click(screen.getByRole('button', { name: tab }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await browser.click(screen.getByRole('button', { name: `+ Add ${singular}` }));
    const dialog = screen.getByRole('dialog', { name: `Add ${singular}` });
    expect(within(dialog).getByLabelText('Code')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Name')).toBeInTheDocument();
    expect(within(dialog).queryByLabelText('Unit') !== null).toBe(tab === 'Quality attributes');
    await browser.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  }
});

it('submits a rice variety from the modal and closes it after success', async () => {
  const browser = userEvent.setup();
  render(<AdminCatalogs />);
  await browser.click(screen.getByRole('button', { name: 'Rice varieties' }));
  await browser.click(screen.getByRole('button', { name: '+ Add rice variety' }));
  const dialog = screen.getByRole('dialog', { name: 'Add rice variety' });
  await browser.type(within(dialog).getByLabelText('Code'), 'TEST-RICE');
  await browser.type(within(dialog).getByLabelText('Name'), 'Test rice');
  await browser.click(within(dialog).getByRole('button', { name: 'Add rice variety' }));
  await waitFor(() => expect(fetchMock.mock.calls.some(([url, init]) => url === '/api/rice-varieties' && init?.method === 'POST'
    && JSON.parse(String(init.body)).code === 'TEST-RICE')).toBe(true));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
});

it('shows commodities and measurement units as sibling catalog tabs without nested tabs', async () => {
  const browser = userEvent.setup();
  render(<AdminCatalogs />);
  const navigation = screen.getByRole('navigation', { name: 'Catalog types' });
  const commodities = within(navigation).getByRole('button', { name: 'Commodities' });
  const units = within(navigation).getByRole('button', { name: 'Measurement units' });

  await browser.click(commodities);
  expect(commodities).toHaveAttribute('aria-current', 'page');
  expect(screen.getByRole('region', { name: 'Commodity catalog' })).toHaveClass('admin-catalog-panel');
  expect(screen.queryByRole('navigation', { name: 'Commodity and unit catalogs' })).not.toBeInTheDocument();
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).startsWith('/api/commodities?'))).toBe(true));

  await browser.click(units);
  expect(units).toHaveAttribute('aria-current', 'page');
  expect(commodities).not.toHaveAttribute('aria-current');
  expect(screen.getByRole('region', { name: 'Measurement units' })).toHaveClass('admin-catalog-panel');
  expect(screen.queryByRole('navigation', { name: 'Commodity and unit catalogs' })).not.toBeInTheDocument();
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).startsWith('/api/measurement-units?'))).toBe(true));
});
