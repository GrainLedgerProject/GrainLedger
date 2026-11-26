import { render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { ProcurementDetail } from './Procurement';

const fetchMock = vi.fn<typeof fetch>();
const procurement = {
  id: 'procurement-1', code: 'PN-001', farmerId: 'farmer-1', farmerName: 'Farmer One',
  status: 'RECEIVED_WITHIN_TOLERANCE', receivedAt: '2026-10-14T03:00:00Z', totalNetMassKg: '99',
  items: [{ id: 'item-1', lineNo: 1, sourceLotId: 'source-lot-1', lotCode: 'LOT-1', plotId: 'plot-1', plotCode: 'Plot 1', plotSeasonId: 'season-1', seasonName: 'Season 1', riceVariety: 'IR', measuredValue: '100', measuredUnit: 'KG', netMassKg: '100', moisturePercent: '22' }],
};

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock);
  fetchMock.mockReset();
  fetchMock.mockResolvedValue(new Response(JSON.stringify(procurement), { headers: { 'Content-Type': 'application/json' } }));
});

it('shows within-tolerance receipt as accepted with no pending-arrival warning', async () => {
  render(<ProcurementDetail id="procurement-1" onBack={() => {}} onOpenSourceLot={() => {}} />);
  expect(await screen.findByText('Received — within tolerance')).toBeInTheDocument();
  expect(screen.queryByText(/mill has not accepted this arrival/i)).not.toBeInTheDocument();
});

it('separates recorded variety from current catalog label and retains legacy names', async () => {
  fetchMock.mockResolvedValue(new Response(JSON.stringify({ ...procurement, items: [
    { ...procurement.items[0], currentRiceVarietyName: 'Renamed', currentRiceVarietyCode: 'V-1' },
    { ...procurement.items[0], id: 'item-2', lineNo: 2, riceVariety: 'Legacy variety' },
    { ...procurement.items[0], id: 'item-3', lineNo: 3, riceVariety: '' },
  ] }), { headers: { 'Content-Type': 'application/json' } }));
  render(<ProcurementDetail id="procurement-1" onBack={() => {}} onOpenSourceLot={() => {}} />);
  expect(await screen.findByText('Renamed (V-1)')).toBeInTheDocument();
  expect(screen.getByText('IR')).toBeInTheDocument();
  expect(screen.getByText('Legacy variety')).toBeInTheDocument();
  expect(screen.getByText('Unknown variety')).toBeInTheDocument();
  expect(screen.getAllByText('Recorded rice variety')).toHaveLength(3);
  expect(screen.getAllByText('Current catalog variety')).toHaveLength(1);
});
