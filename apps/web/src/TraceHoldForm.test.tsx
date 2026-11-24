import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { expect, it, vi } from 'vitest';
import { TraceHoldForm, type HoldTarget } from './TraceHoldForm';

const target: HoldTarget = { type: 'lot', id: 'lot-a', code: 'RAW-A' };

it('retries the same committed HOLD after remount, even after the lot is shown as held', async () => {
  sessionStorage.clear();
  const bodies: string[] = [];
  const response = (body: unknown) => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } });
  vi.stubGlobal('fetch', vi.fn(async (url, init) => {
    if (url === '/api/auth/csrf') return response({ token: 'csrf' });
    if (url === '/api/ledger/lookups') return response({ facilities: [{ id: 'warehouse-a', name: 'Warehouse A' }] });
    if (url === '/api/ledger/evidence') return response({ id: 'evidence-a' });
    if (url === '/api/ledger/holds') {
      bodies.push(String(init?.body));
      if (bodies.length === 1) throw new TypeError('Response lost after commit');
      return response({ id: JSON.parse(bodies[0]).id });
    }
    throw Error('Unexpected API request');
  }));
  const actor = userEvent.setup();
  const first = render(<TraceHoldForm target={target} userId="cm-a" ownerId="owner-a" />);
  await actor.click(await screen.findByRole('button', { name: 'Place HOLD on RAW-A' }));
  await actor.selectOptions(await screen.findByLabelText('Evidence facility'), 'warehouse-a');
  await actor.upload(screen.getByLabelText('Evidence file'), new File(['evidence'], 'report.txt', { type: 'text/plain' }));
  await actor.click(screen.getByRole('button', { name: 'Upload evidence' }));
  await screen.findByText('Evidence stored immutably.');
  await actor.type(screen.getByLabelText('Reason'), 'Synthetic confirmed risk');
  await actor.click(screen.getByRole('button', { name: 'Confirm HOLD' }));
  await screen.findByText('Response lost after commit');
  first.unmount();

  const other = render(<TraceHoldForm target={{ ...target, held: true }} userId="cm-b" ownerId="owner-a" />);
  expect(screen.queryByRole('button', { name: 'Retry HOLD' })).not.toBeInTheDocument();
  other.unmount();

  const onSaved = vi.fn();
  render(<TraceHoldForm target={{ ...target, held: true }} userId="cm-a" ownerId="owner-a" onSaved={onSaved} />);
  await actor.click(await screen.findByRole('button', { name: 'Retry HOLD' }));
  await screen.findByText('HOLD recorded for RAW-A.');
  expect(bodies).toHaveLength(2);
  expect(bodies[1]).toBe(bodies[0]);
  expect(onSaved).toHaveBeenCalledOnce();
});
