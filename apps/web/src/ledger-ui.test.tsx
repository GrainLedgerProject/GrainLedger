import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ListControls, Pagination, Table } from './ledger-ui';

describe('shared list controls', () => {
  it('renders exact boundary states and current-page label', () => {
    const change = vi.fn(), submit = vi.fn(), onSearch = vi.fn(), onSortBy = vi.fn(), onSortDirection = vi.fn();
    render(<form onSubmit={event => { event.preventDefault(); submit(); }}><ListControls label="operations" search="" onSearch={onSearch} sortBy="kind" onSortBy={onSortBy} sortDirection="asc" onSortDirection={onSortDirection} sortOptions={[{ value: 'kind', label: 'Kind' }]} /><Pagination page={2} pageSize={25} totalCount={251} label="operations" onChange={change} /></form>);
    expect(screen.getByRole('button', { name: 'First operations page' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Last operations page' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Page 2 of operations' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByText('…')).toBeInTheDocument();
    fireEvent.keyDown(screen.getByRole('searchbox', { name: 'Search operations' }), { key: 'Enter', code: 'Enter' });
    expect(submit).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Page 3 of operations' }));
    expect(change).toHaveBeenCalledWith(3);
    expect(submit).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'First operations page' }));
    expect(change).toHaveBeenCalledWith(1);
    const first = render(<Pagination page={1} pageSize={25} totalCount={0} label="records" onChange={change} />);
    expect(screen.getByRole('button', { name: 'First records page' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Last records page' })).toBeDisabled();
    first.unmount();
  });

  it('resets pagination after filtering and sorts exact decimal strings', () => {
    const rows = Array.from({ length: 30 }, (_, i) => ({ id: String(i), quantityKg: i < 3 ? ['9007199254740992.1', '9007199254740992.09', '9007199254740992.11'][i] : '0', note: i === 29 ? 'match me' : 'other' }));
    render(<Table rows={rows} label="lots" />);
    fireEvent.click(screen.getByRole('button', { name: 'Page 2 of lots' }));
    expect(screen.getByText('25')).toBeInTheDocument();
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search lots' }), { target: { value: 'match me' } });
    expect(screen.getByText('match me')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Page 1 of lots' })).toHaveAttribute('aria-current', 'page');
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search lots' }), { target: { value: '' } });
    fireEvent.change(screen.getByLabelText('Sort lots by'), { target: { value: 'quantityKg' } });
    fireEvent.change(screen.getByLabelText('lots sort direction'), { target: { value: 'desc' } });
    const body = screen.getByRole('table').querySelector('tbody')!;
    const ids = within(body).getAllByRole('row').slice(0, 3).map(row => row.querySelectorAll('td')[0].textContent);
    expect(ids).toEqual(['2', '0', '1']);
  });
});
