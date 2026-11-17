import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it, vi } from 'vitest';
import { ContainerProduction } from './ContainerProduction';
const mock = vi.fn<typeof fetch>();
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const route = '/api/export-containers/c1/production';
const task = { containerId: 'c1', code: 'C1', state: 'DRAFT', version: 2, facilityId: 'f1', ownerId: 'owner1', targetKg: '20', outputCommodityId: 'commodity1',
  inputs: [{ lotId: 'i1', lotCode: 'HEAD-1', locationId: 'store1', quantityKg: '20' }],
  operation: { id: 'operation1', version: 'draft-v1', equipmentId: 'equipment1', occurredAt: '2026-11-17T10:00:00Z', declaredLossKg: '0',
    inputs: [{ lotId: 'i1', locationId: 'store1', quantityKg: '20' }], outputs: [{ id: 'out1', code: 'BLD-1', commodityId: 'commodity1', stage: 'BLENDED_RICE', productIdentity: 'BLENDED_RICE', locationId: 'outstore', quantityKg: '20' }] } };
const look = { equipment: [{ id: 'equipment1', facilityId: 'f1', name: 'Blender' }], locations: [{ id: 'outstore', facilityId: 'f1', name: 'Finished store', mixedBulk: false }], owners: [], facilities: [], commodities: [] };
beforeEach(() => { vi.stubGlobal('fetch', mock); mock.mockReset(); sessionStorage.clear(); });
it('retains the exact uncertain production request across remount and retries it once confirmed', async () => {
  const bodies: string[] = []; let failed = false;
  mock.mockImplementation(async (url, init) => {
    if (String(url) === '/api/auth/csrf') return json({ token: 'csrf' });
    if (String(url) === '/api/ledger/lookups') return json(look);
    if (String(url) === route + '/commit') { bodies.push(String(init?.body)); if (!failed) { failed = true; throw new TypeError('Network lost'); } return json({ ...task, state: 'PRODUCED', version: 3 }); }
    return json(task);
  });
  const browser = userEvent.setup();
  const first = render(<ContainerProduction id="c1" userId="operator1" operator />);
  await screen.findByText('DRAFT'); await browser.click(screen.getByRole('checkbox', { name: /saved draft records the actual production/ }));
  await browser.click(screen.getByRole('button', { name: 'Commit approved blend production' }));
  await screen.findByRole('button', { name: 'Retry production operation' }); first.unmount();
  render(<ContainerProduction id="c1" userId="operator1" operator />);
  await browser.click(await screen.findByRole('button', { name: 'Retry production operation' }));
  await waitFor(() => expect(bodies).toHaveLength(2)); expect(bodies[1]).toBe(bodies[0]);
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Retry production operation' })).not.toBeInTheDocument());
});
it('requires saving edited actual measurements before committing the previously saved draft', async () => {
  mock.mockImplementation(async url => String(url) === '/api/ledger/lookups' ? json(look) : json(task));
  const browser = userEvent.setup(); render(<ContainerProduction id="c1" userId="operator1" operator />);
  await screen.findByText('DRAFT');
  await browser.clear(screen.getByLabelText('Actual finished output (kg)')); await browser.type(screen.getByLabelText('Actual finished output (kg)'), '19.999999');
  await browser.click(screen.getByRole('checkbox', { name: /saved draft records the actual production/ }));
  expect(screen.getByRole('button', { name: 'Commit approved blend production' })).toBeDisabled();
  expect(mock.mock.calls.some(([url]) => String(url).endsWith('/commit'))).toBe(false);
});
it('gives the customer reviewer review controls and no production or loading authority', async () => {
  mock.mockImplementation(async () => json({ ...task, state: 'PRODUCED', outputId: 'out1', outputCode: 'BLD-1' }));
  render(<ContainerProduction id="c1" userId="cm1" canReview />);
  await screen.findByRole('button', { name: 'Approve actual output' });
  expect(screen.queryByRole('button', { name: 'Commit approved blend production' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Request approved blend production' })).not.toBeInTheDocument();
});
