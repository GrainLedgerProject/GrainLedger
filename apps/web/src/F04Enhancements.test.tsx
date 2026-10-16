import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FarmerCreate } from './FarmerCreate';
import { FarmerEdit } from './FarmerEdit';
import { PlotCreate } from './PlotCreate';
import { PlotEdit } from './PlotEdit';
import { clearLocalSession } from './api';

const fetchMock = vi.fn<typeof fetch>();
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body),
  { status, headers: { 'Content-Type': 'application/json' } });
const farmerId = '6114dc9c-f08c-4b25-a90f-91c7c88a05b0';

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  clearLocalSession();
  window.sessionStorage.clear();
  window.localStorage.clear();
});

describe('F04 enhancements', () => {
  it('warns about a same-name farmer without blocking registration', async () => {
    const created = vi.fn();
    fetchMock.mockImplementation(async (url, init) => {
      if (url === '/api/auth/csrf') return json({ token: 'csrf' });
      if (url === '/api/farmers/duplicate-check') {
        expect(JSON.parse(String(init?.body))).toMatchObject({ name: 'Nguyễn Văn A' });
        return json({ items: [{ id: farmerId, code: 'ND-1', name: 'Nguyễn Văn A', contact: null, reasons: ['SAME_NAME'] }], hasMore: false });
      }
      if (url === '/api/farmers') return json({ id: 'new-farmer', code: 'ND-2', name: 'Nguyễn Văn A' }, 201);
      return json({}, 404);
    });
    render(<FarmerCreate userId="user" companyId="company" onCancel={vi.fn()} onCreated={created} />);
    const browser = userEvent.setup();
    await browser.type(screen.getByLabelText(/Full name/), 'Nguyễn Văn A');
    expect(await screen.findByText(/Possible existing farmer/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Nguyễn Văn A \(ND-1\)/ })).toHaveAttribute('href', `/capture/farmers/${farmerId}`);
    await browser.click(screen.getByRole('button', { name: 'Save farmer' }));
    await waitFor(() => expect(created).toHaveBeenCalledWith('new-farmer'));
  });

  it('uses the current version when editing and excludes the same farmer from duplicate results', async () => {
    const updated = vi.fn();
    fetchMock.mockImplementation(async (url, init) => {
      if (url === `/api/farmers/${farmerId}` && init?.method === 'PUT') {
        expect(JSON.parse(String(init.body))).toMatchObject({ code: 'ND-1', name: 'Nguyễn Văn B', expectedVersion: 3 });
        return json({ id: farmerId, code: 'ND-1', name: 'Nguyễn Văn B', version: 4 });
      }
      if (url === `/api/farmers/${farmerId}`) return json({ id: farmerId, code: 'ND-1', name: 'Nguyễn Văn A', contact: null, version: 3, canEdit: true });
      if (url === '/api/auth/csrf') return json({ token: 'csrf' });
      if (url === '/api/farmers/duplicate-check') {
        expect(JSON.parse(String(init?.body))).toMatchObject({ excludeId: farmerId });
        return json({ items: [], hasMore: false });
      }
      return json({}, 404);
    });
    render(<FarmerEdit id={farmerId} onCancel={vi.fn()} onUpdated={updated} />);
    const browser = userEvent.setup();
    const name = await screen.findByLabelText(/Full name/);
    await browser.clear(name);
    await browser.type(name, 'Nguyễn Văn B');
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url === '/api/farmers/duplicate-check')).toBe(true));
    await browser.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(updated).toHaveBeenCalled());
  });

  it('warns about a nearby saved point and keeps Save plot available', async () => {
    fetchMock.mockImplementation(async (url, init) => {
      if (url === '/api/auth/csrf') return json({ token: 'csrf' });
      if (url === '/api/plots/duplicate-check') {
        expect(JSON.parse(String(init?.body))).toMatchObject({ geometry: { type: 'Point', coordinates: [106.35, 10.4] } });
        return json({ items: [{ id: 'plot-1', farmerId, code: 'TR-1', distanceMeters: 1.1, reasons: ['NEARBY_POINT'] }], hasMore: false, radiusMeters: 20, coverage: 'POINT_ONLY' });
      }
      return json({}, 404);
    });
    render(<PlotCreate farmerId={farmerId} farmerName="Nguyễn Văn A" userId="user" companyId="company" onCancel={vi.fn()} onCreated={vi.fn()} />);
    const browser = userEvent.setup();
    await browser.type(screen.getByLabelText(/Longitude/), '106.35');
    await browser.type(screen.getByLabelText(/Latitude/), '10.4');
    expect(await screen.findByText(/Possible nearby plot/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save plot' })).toBeEnabled();
  });

  it('keeps a polygon unchanged when only its plot area is edited', async () => {
    const polygon = { type: 'Polygon', coordinates: [[[106.35, 10.4], [106.36, 10.4], [106.36, 10.41], [106.35, 10.4]]] };
    const updated = vi.fn();
    fetchMock.mockImplementation(async (url, init) => {
      if (url === '/api/plots/plot-1' && init?.method === 'PUT') {
        expect(JSON.parse(String(init.body))).toMatchObject({ farmerId, code: 'TR-1', geometry: polygon, areaHa: 1.5, expectedVersion: 2 });
        return json({ id: 'plot-1', geometry: polygon, areaHa: 1.5, version: 3 });
      }
      if (url === '/api/plots/plot-1') return json({ id: 'plot-1', farmerId, code: 'TR-1', geometry: polygon, areaHa: 1.25, version: 2, canEdit: true });
      if (url === '/api/auth/csrf') return json({ token: 'csrf' });
      return json({}, 404);
    });
    render(<PlotEdit id="plot-1" onCancel={vi.fn()} onUpdated={updated} />);
    const browser = userEvent.setup();
    expect(await screen.findByText(/recorded boundary is preserved/)).toBeInTheDocument();
    const area = screen.getByLabelText(/Area \(hectares\)/);
    await browser.clear(area);
    await browser.type(area, '1.5');
    await browser.click(screen.getByRole('button', { name: 'Save changes' }));
    await waitFor(() => expect(updated).toHaveBeenCalled());
  });
});
