import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { AdminMrl } from './AdminMrl';

const fetchMock = vi.fn<typeof fetch>();
const response = (body: unknown, status = 200, etag?: string) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...(etag ? { ETag: etag } : {}) } });
const marketId = '11111111-1111-4111-8111-111111111111';
const commodityId = '22222222-2222-4222-8222-222222222222';
const substanceId = '33333333-3333-4333-8333-333333333333';
const versionId = '44444444-4444-4444-8444-444444444444';

beforeEach(() => { vi.stubGlobal('fetch', fetchMock); vi.stubGlobal('confirm', vi.fn(() => true)); fetchMock.mockReset(); });

it('creates an MRL draft, writes a limit and publishes with the latest ETag', async () => {
  let version: Record<string, unknown> | null = null;
  let revision = 1;
  let limits: object[] = [];
  fetchMock.mockImplementation(async (url, init) => {
    const path = String(url).split('?')[0];
    if (path === '/api/auth/csrf') return response({ token: 'csrf' });
    if (path === '/api/markets') return response({ items: [{ id: marketId, code: 'EU', name: 'European Union' }], page: 1, pageSize: 100, hasMore: false });
    if (path === '/api/commodities') return response({ items: [{ id: commodityId, code: 'RICE', name: 'Rice' }], page: 1, pageSize: 100, hasMore: false });
    if (path === '/api/active-substances') return response({ items: [{ id: substanceId, code: 'SUB-1', name: 'Substance One' }], page: 1, pageSize: 100, hasMore: false });
    if (path === '/api/measurement-units') return response({ items: [{ id: 'unit-id', code: 'MG/KG', name: 'Milligram per kilogram', quantityKind: 'RESIDUE_CONCENTRATION' }], page: 1, pageSize: 100, hasMore: false });
    if (path === '/api/mrl-versions' && init?.method === 'POST') {
      version = { id: versionId, ...JSON.parse(String(init.body)), versionNo: 1, status: 'DRAFT', publishedAt: null, publishedBy: null };
      return response(version, 201, '"1"');
    }
    if (path === '/api/mrl-versions') return response({ items: version ? [version] : [], page: 1, pageSize: 20, hasMore: false });
    if (path === `/api/mrl-versions/${versionId}` && !init?.method) return response(version, 200, `"${revision}"`);
    if (path === `/api/mrl-versions/${versionId}/limits` && !init?.method) return response({ items: limits, page: 1, pageSize: 20, hasMore: false }, 200, `"${revision}"`);
    if (path === `/api/mrl-versions/${versionId}/limits/${substanceId}` && init?.method === 'PUT') {
      expect(new Headers(init.headers).get('If-Match')).toBe('"1"');
      expect(JSON.parse(String(init.body))).toEqual({ limitValue: 0.01, unit: 'mg/kg' });
      revision = 2;
      limits = [{ id: 'limit-id', mrlVersionId: versionId, substanceId, limitValue: 0.01, unit: 'mg/kg' }];
      return response(limits[0], 201, '"2"');
    }
    if (path === `/api/mrl-versions/${versionId}` && init?.method === 'PATCH') {
      expect(new Headers(init.headers).get('If-Match')).toBe('"2"');
      version = { ...version, ...JSON.parse(String(init.body)) };
      revision = 3;
      return response(version, 200, '"3"');
    }
    if (path === `/api/mrl-versions/${versionId}/publish` && init?.method === 'POST') {
      expect(new Headers(init.headers).get('If-Match')).toBe('"3"');
      revision = 4;
      version = { ...version, status: 'PUBLISHED', publishedAt: '2026-10-16T10:00:00Z' };
      return response(version, 200, '"4"');
    }
    return response({}, 404);
  });

  const browser = userEvent.setup();
  render(<AdminMrl onOpenCatalogs={() => {}} />);
  await waitFor(() => expect(screen.getByRole('option', { name: 'EU · European Union' })).toBeInTheDocument());
  await browser.click(screen.getByRole('button', { name: '+ Create draft' }));
  const dialog = screen.getByRole('dialog', { name: 'Create draft' });
  await browser.selectOptions(within(dialog).getByLabelText('Market'), marketId);
  await browser.selectOptions(within(dialog).getByLabelText('Commodity'), commodityId);
  fireEvent.change(within(dialog).getByLabelText('Effective from'), { target: { value: '2027-01-01T00:00' } });
  await browser.type(within(dialog).getByLabelText('Regulatory source reference'), 'Verified regulation section 1');
  await browser.click(within(dialog).getByRole('button', { name: 'Create draft' }));
  await screen.findByRole('heading', { name: 'Substance limits' });
  const detail = screen.getByRole('region', { name: 'Selected MRL version' });
  expect(within(detail).getByText('Draft created. Add verified limits before publishing.')).toBeInTheDocument();
  expect(screen.queryByRole('region', { name: 'MRL versions' })).not.toBeInTheDocument();
  await browser.click(screen.getByRole('button', { name: '+ Add limit' }));
  await browser.selectOptions(screen.getByLabelText('Active substance'), substanceId);
  await browser.type(screen.getByLabelText('Limit (mg/kg)'), '0.01');
  await browser.click(screen.getByRole('button', { name: 'Save limit' }));
  await waitFor(() => expect(within(screen.getByRole('region', { name: 'Substance limits' })).getByText('Limit saved.')).toBeInTheDocument());
  await waitFor(() => expect(screen.getByText('0.01')).toBeInTheDocument());
  await browser.click(screen.getByRole('button', { name: 'Edit details' }));
  expect(screen.getByRole('button', { name: 'Publish version' })).toBeDisabled();
  fireEvent.change(within(detail).getByLabelText('Effective from'), { target: { value: '2027-02-01T00:00' } });
  await browser.click(screen.getByRole('button', { name: 'Save draft details' }));
  await waitFor(() => expect(within(detail).getByText('Draft updated.')).toBeInTheDocument());
  expect(screen.getByRole('button', { name: 'Publish version' })).toBeEnabled();
  await browser.click(screen.getByRole('button', { name: 'Publish version' }));
  expect(await within(detail).findByText('MRL version published.')).toBeInTheDocument();
  expect(fetchMock.mock.calls.some(([url]) => String(url) === `/api/mrl-versions/${versionId}/publish`)).toBe(true);
});

it('imports a CSV group as one open-ended draft with its numeric limits', async () => {
  const imported: unknown[] = [];
  fetchMock.mockImplementation(async (url, init) => {
    const path = String(url).split('?')[0];
    if (path === '/api/auth/csrf') return response({ token: 'csrf' });
    if (path === '/api/markets') return response({ items: [{ id: marketId, code: 'EU', name: 'European Union' }], hasMore: false });
    if (path === '/api/commodities') return response({ items: [{ id: commodityId, code: 'RICE', name: 'Rice' }], hasMore: false });
    if (path === '/api/active-substances') return response({ items: [{ id: substanceId, code: 'TCZ001', name: 'Tricyclazole' }], hasMore: false });
    if (path === '/api/measurement-units') return response({ items: [{ id: 'unit', code: 'MG/KG', quantityKind: 'RESIDUE_CONCENTRATION' }], hasMore: false });
    if (path === '/api/mrl-versions/import' && init?.method === 'POST') {
      imported.push(JSON.parse(String(init.body)));
      return response({ id: versionId, marketId, commodityId, versionNo: 1, status: 'DRAFT', effectiveFrom: '2027-01-01T00:00:00Z', effectiveTo: null, sourceRef: 'Regulation', publishedAt: null }, 201);
    }
    if (path === '/api/mrl-versions') return response({ items: [], page: 1, pageSize: 20, hasMore: false });
    if (path === `/api/mrl-versions/${versionId}`) return response({ id: versionId, marketId, commodityId, versionNo: 1, status: 'DRAFT', effectiveFrom: '2027-01-01T00:00:00Z', effectiveTo: null, sourceRef: 'Regulation', publishedAt: null }, 200, '"1"');
    if (path === `/api/mrl-versions/${versionId}/limits`) return response({ items: [{ id: 'limit', substanceId, limitValue: 0.01, unit: 'mg/kg' }], page: 1, pageSize: 20, hasMore: false });
    return response({}, 404);
  });
  render(<AdminMrl onOpenCatalogs={() => {}} />);
  const browser = userEvent.setup();
  await waitFor(() => expect(screen.getByRole('option', { name: 'EU · European Union' })).toBeInTheDocument());
  await browser.click(screen.getByRole('button', { name: 'Import CSV / Excel' }));
  const dialog = screen.getByRole('dialog', { name: 'Import MRL draft' });
  const file = new File([
    'substance_id,substance_name,commodity_code,commodity_name_en,market,regulation_reference,mrl_value_mg_kg,mrl_unit,effective_date,version,status\n',
    'TCZ001,Tricyclazole,0500060,Rice,EU,Regulation,0.01,mg/kg,2027-01-01,EU-v1,ACTIVE\n',
    'IPT002,Isoprothiolane,0500060,Rice,EU,Regulation,,mg/kg,2027-01-01,EU-v1,NO_MRL\n',
  ], 'mrl.csv', { type: 'text/csv' });
  await browser.upload(within(dialog).getByLabelText('MRL file (.csv or .xlsx)'), file);
  expect(await within(dialog).findByText(/1 rows with statuses other than ACTIVE excluded/)).toBeInTheDocument();
  expect(within(dialog).getByLabelText('Market')).toHaveValue(marketId);
  await browser.selectOptions(within(dialog).getByLabelText('Commodity'), commodityId);
  fireEvent.change(within(dialog).getByLabelText('Effective from'), { target: { value: '2027-01-01T00:00' } });
  expect(within(dialog).getByLabelText('Effective to (optional)')).toHaveValue('');
  await browser.click(within(dialog).getByRole('button', { name: 'Create draft with 1 limits' }));
  await waitFor(() => expect(imported).toHaveLength(1));
  expect(imported[0]).toMatchObject({ marketId, commodityId, effectiveTo: null,
    limits: [{ substanceCode: 'TCZ001', limitValue: '0.01', unit: 'mg/kg' }] });
  expect(await screen.findByText(/1 limits imported into a draft/)).toBeInTheDocument();
});
