import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { expect, it, vi } from 'vitest';
import { ContainerSourceTrace, type ContainerTrace } from './ContainerSourceTrace';
import { flowLimit } from './trace-flow';

const trace: ContainerTrace = { loadedKg: '10.000000', complete: true,
  sourceContributions: ['a', 'b'].map(sourceLotId => ({ sourceLotId, quantityKg: '5.000000', percentage: '50', provenance: { plotId: `plot-${sourceLotId}`, procurementCode: 'PRC-1' } })),
  graph: { root: { type: 'container', id: 'c1' }, schemaVersion: 2, traceId: 'graph1', direction: 'backward', view: 'current', asOf: '2026-11-18T00:00:00Z', complete: true,
    lotCount: 1, edgeCount: 0, sourceContributionsVisible: true, lots: [{ id: 'blend', code: 'BLD-1', stage: 'BLENDED_RICE', producedKg: '20.000000', positions: [], restrictions: [], contributions: null }],
    edges: [], operations: [], corrections: [], sources: [], actualContainers: [{ containerId: 'c1', code: 'CTN-1', status: 'CLOSED', linkedLoadedKg: '10.000000', basis: 'ACTUAL_LOADING', held: false }],
    loadingEdges: [{ containerId: 'c1', lotId: 'blend', quantityKg: '10.000000', basis: 'ACTUAL_LOADING', lines: [
      { lineId: 'l1', loadId: 'load1', locationId: 'store', proposalId: 'p1', loadedAt: '2026-11-18T00:00:00Z', quantityKg: '4.000001' },
      { lineId: 'l2', loadId: 'load2', locationId: 'store', proposalId: 'p2', loadedAt: '2026-11-18T00:00:00Z', quantityKg: '5.999999' }] }] } };

it('shows container source kg alongside historical production and changes display without another API/audit', async () => {
  const fetch = vi.fn(); vi.stubGlobal('fetch', fetch);
  const user = userEvent.setup(); render(<ContainerSourceTrace trace={trace} />);
  expect(within(screen.getByRole('table', { name: 'Actual container sources' })).getAllByText('5.000000')).toHaveLength(2);
  expect(screen.getByRole('table', { name: 'Container ancestry lots' })).toHaveTextContent('20.000000');
  expect(screen.getByRole('table', { name: 'Trace actual loading' })).toHaveTextContent('10.000000');
  await user.click(screen.getByRole('button', { name: 'Flowchart' }));
  const container = screen.getByRole('link', { name: 'Open container CTN-1 — starting container' });
  expect(container).toHaveAttribute('href', '/export/containers/c1');
  await user.click(screen.getByRole('button', { name: 'Focus starting container' })); expect(container).toHaveFocus();
  expect(screen.getByRole('region', { name: 'Trace flowchart' })).toHaveTextContent('10.000000 kg linked actual loading');
  await user.click(screen.getByRole('button', { name: 'Select lot BLD-1' }));
  expect(screen.getByRole('region', { name: 'Selected graph lot' })).toHaveTextContent('Quantity when created: 20.000000 kg');
  await user.click(screen.getByRole('button', { name: 'Table' }));
  expect(screen.getByRole('table', { name: 'Trace actual loading' })).toHaveTextContent('5.999999');
  expect(fetch).not.toHaveBeenCalled();
});

it('keeps incomplete source findings and a container with no loading visible without inventing edges', async () => {
  const user = userEvent.setup(); render(<ContainerSourceTrace trace={{ ...trace, loadedKg: '0.000000', complete: false, findings: ['NO_ACTUAL_LOADING'], sourceContributions: [],
    graph: { ...trace.graph!, lots: [], loadingEdges: [] } }} />);
  expect(screen.getByText(/incomplete: NO_ACTUAL_LOADING/)).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Flowchart' }));
  expect(screen.getByRole('link', { name: /starting container/ })).toBeInTheDocument();
  expect(screen.getByText(/starting container is shown on its own/)).toBeInTheDocument();
});

it('falls back to the full paginated table when the graph is too large', async () => {
  const user = userEvent.setup(); render(<ContainerSourceTrace trace={{ ...trace, graph: { ...trace.graph!, lots: Array.from({ length: flowLimit.nodes }, (_, i) => ({ ...trace.graph!.lots[0], id: `lot${i}`, code: `LOT-${i}` })), loadingEdges: [] } }} />);
  await user.click(screen.getByRole('button', { name: 'Flowchart' }));
  expect(screen.getByText(/complete result remains available in Table/)).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Show table' }));
  expect(screen.getByRole('table', { name: 'Container ancestry lots' })).toHaveTextContent('LOT-0');
  await user.click(screen.getByRole('button', { name: 'Next container ancestry lots page' }));
  expect(screen.getByRole('table', { name: 'Container ancestry lots' })).toHaveTextContent('LOT-25');
});
