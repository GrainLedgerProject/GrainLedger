import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { clearLocalSession } from './api';
import { ByproductVarietyPolicy } from './ByproductVarietyPolicy';

const fetchMock = vi.fn<typeof fetch>();
const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
let conflict = false;
let items: Array<{ stage: string; allowCrossVariety: boolean; source: string; revision: string | null }>;
beforeEach(() => {
  clearLocalSession(); fetchMock.mockReset(); vi.stubGlobal('fetch', fetchMock); conflict = false;
  items = ['BRAN', 'HUSK'].map(stage => ({ stage, allowCrossVariety: true, source: 'SYSTEM_DEFAULT', revision: null }));
  fetchMock.mockImplementation(async (url, init) => {
    const path = String(url);
    if (path === '/api/auth/csrf') return response({ token: 'csrf' });
    if (init?.method === 'PUT') {
      if (conflict) return response({ detail: 'Stale' }, 409);
      const body = JSON.parse(String(init.body));
      const stage = path.split('/').pop();
      const updated = { stage: stage!, allowCrossVariety: body.allowCrossVariety ?? true, source: body.allowCrossVariety === null ? 'SYSTEM_DEFAULT' : 'COMPANY_OVERRIDE', revision: 'new-revision' };
      items = items.map(item => item.stage === stage ? updated : item);
      return response(updated);
    }
    return response({ companyId: 'owner', items });
  });
});

it('saves bran independently and reloads stale policy without submitting husk', async () => {
  const user = userEvent.setup(); render(<ByproductVarietyPolicy />);
  const bran = (await screen.findByRole('heading', { name: 'Bran' })).closest('section')!;
  const husk = screen.getByRole('heading', { name: 'Husk' }).closest('section')!;
  await user.selectOptions(within(bran).getByLabelText('Bran mixing policy'), 'separate');
  await user.type(within(bran).getByLabelText('Reason'), 'Separate stock');
  await user.click(within(bran).getByRole('button', { name: 'Save' }));
  await screen.findByText(/Keep varieties separate · Company override/);
  expect(within(husk).getByText(/Allow different varieties · System default/)).toBeVisible();
  const put = fetchMock.mock.calls.find(([url, init]) => String(url).endsWith('/BRAN') && init?.method === 'PUT')!;
  expect(JSON.parse(String(put[1]?.body))).toEqual({ allowCrossVariety: false, expectedRevision: null, reason: 'Separate stock' });
  conflict = true;
  const currentBran = screen.getByRole('heading', { name: 'Bran' }).closest('section')!;
  await user.type(within(currentBran).getByLabelText('Reason'), 'Retry review');
  await user.click(within(currentBran).getByRole('button', { name: 'Save' }));
  await screen.findByRole('alert');
  await waitFor(() => expect(screen.getByText(/review before saving again/i)).toBeVisible());
  expect(fetchMock.mock.calls.filter(([url, init]) => String(url).endsWith('/HUSK') && init?.method === 'PUT')).toHaveLength(0);
});
