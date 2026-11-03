import { describe, expect, it } from 'vitest';
import { parsePlotPoint, toMapPoint } from './plotCoordinates';

describe('plot coordinates', () => {
  it('keeps GeoJSON longitude first and converts only the map view', () => {
    expect(parsePlotPoint('106.35', '10.4')).toEqual([106.35, 10.4]);
    expect(toMapPoint([106.35, 10.4])).toEqual([10.4, 106.35]);
  });

  it('does not turn missing or out-of-range coordinates into a selected point', () => {
    expect(parsePlotPoint('', '10.4')).toBeNull();
    expect(parsePlotPoint('106.35', '')).toBeNull();
    expect(parsePlotPoint('181', '10.4')).toBeNull();
    expect(parsePlotPoint('106.35', '-91')).toBeNull();
  });
});
