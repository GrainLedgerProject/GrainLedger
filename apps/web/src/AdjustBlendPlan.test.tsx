import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { AdjustBlendPlan } from './AdjustBlendPlan';

const positions = [
  { lotId: 'a', locationId: 's1', lotCode: 'A', locationName: 'Head Store', availableKg: '10.5' },
  { lotId: 'b', locationId: 's2', lotCode: 'B', locationName: 'Broken Store', availableKg: '4' },
  { lotId: 'c', locationId: 's3', lotCode: 'C', locationName: 'Head Store B', availableKg: '10' }
];
const allocations = [ { lotId: 'a', locationId: 's1', lotCode: 'A', quantityKg: '9' },
  { lotId: 'b', locationId: 's2', lotCode: 'B', quantityKg: '1' } ];
function setup(overrides = {}) {
  const props = { allocations, totalKg: '10', disabled: false, busy: false, loadPositions: vi.fn(async () => positions),
    onEditing: vi.fn(), onCheck: vi.fn(), onCancel: vi.fn(), ...overrides };
  render(<AdjustBlendPlan {...props} />); return props;
}
describe('Adjust blend plan', () => {
  it('removes broken rice and compensates with head rice using exact whole kg', async () => {
    const user = userEvent.setup(); const props = setup();
    await user.click(screen.getByRole('button', { name: 'Edit allocations' }));
    await user.click(await screen.findByRole('button', { name: 'Remove B' }));
    expect(screen.getByRole('button', { name: 'Check adjusted plan' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Quantity for A at s1'), { target: { value: '10' } });
    await user.click(screen.getByRole('button', { name: 'Check adjusted plan' }));
    expect(props.onEditing).toHaveBeenCalledWith(true);
    expect(props.onCheck).toHaveBeenCalledWith([{ ...allocations[0], quantityKg: '10' }]);
  });
  it('adds another lot/location and rejects fractional, excess and unbalanced quantities', async () => {
    const user = userEvent.setup(); const props = setup();
    await user.click(screen.getByRole('button', { name: 'Edit allocations' }));
    await screen.findByRole('button', { name: 'Remove B' });
    await user.selectOptions(screen.getByLabelText('Add stock position'), 'c:s3');
    await user.click(screen.getByRole('button', { name: 'Add lot' }));
    const input = screen.getByLabelText('Quantity for A at s1');
    for (const value of ['8.5', '11', '0', '-1', '1e1']) {
      fireEvent.change(input, { target: { value } });
      expect(screen.getByRole('button', { name: 'Check adjusted plan' })).toBeDisabled();
    }
    fireEvent.change(input, { target: { value: '8' } });
    await user.click(screen.getByRole('button', { name: 'Check adjusted plan' }));
    expect(props.onCheck).toHaveBeenCalledWith(expect.arrayContaining([{ lotId: 'c', locationId: 's3', lotCode: 'C', quantityKg: '1' }]));
  });
  it('shows load failure and allows explicit cancellation', async () => {
    const user = userEvent.setup(); const props = setup();
    props.loadPositions.mockRejectedValueOnce(new Error('Stock changed'));
    await user.click(screen.getByRole('button', { name: 'Edit allocations' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Stock changed');
    expect(props.onEditing).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Edit allocations' }));
    await user.click(await screen.findByRole('button', { name: 'Cancel adjustment' }));
    await waitFor(() => expect(props.onCancel).toHaveBeenCalled());
    expect(props.onEditing).toHaveBeenLastCalledWith(false);
  });
});
