import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useCurrentLocation } from './useCurrentLocation';

const originalGeolocation = navigator.geolocation;

function locationResult(longitude: number, latitude: number, accuracy = 8) {
  return { coords: { longitude, latitude, accuracy } } as GeolocationPosition;
}

afterEach(() => {
  Object.defineProperty(navigator, 'geolocation', { configurable: true, value: originalGeolocation });
});

describe('device location for a plot', () => {
  it('uses a valid position only after the user requests it', () => {
    let succeed: PositionCallback = () => {};
    const getCurrentPosition = vi.fn((success: PositionCallback) => { succeed = success; });
    Object.defineProperty(navigator, 'geolocation', { configurable: true, value: { getCurrentPosition } });
    const onLocation = vi.fn();
    const { result } = renderHook(() => useCurrentLocation());
    expect(getCurrentPosition).not.toHaveBeenCalled();
    act(() => result.current.locate(onLocation));
    act(() => succeed(locationResult(106.35, 10.4)));
    expect(onLocation).toHaveBeenCalledWith([106.35, 10.4]);
    expect(result.current.accuracy).toBe(8);
  });

  it('ignores a GPS result after a newer manual or map selection', () => {
    let succeed: PositionCallback = () => {};
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: { getCurrentPosition: (success: PositionCallback) => { succeed = success; } },
    });
    const onLocation = vi.fn();
    const { result } = renderHook(() => useCurrentLocation());
    act(() => result.current.locate(onLocation));
    act(() => result.current.invalidate());
    act(() => succeed(locationResult(106.35, 10.4)));
    expect(onLocation).not.toHaveBeenCalled();
    expect(result.current.locating).toBe(false);
  });

  it('keeps manual entry available after permission is denied', () => {
    let fail: PositionErrorCallback = () => {};
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: { getCurrentPosition: (_: PositionCallback, error: PositionErrorCallback) => { fail = error; } },
    });
    const { result } = renderHook(() => useCurrentLocation());
    act(() => result.current.locate(vi.fn()));
    act(() => fail({ code: 1 } as GeolocationPositionError));
    expect(result.current.locationError).toMatch(/denied/);
    expect(result.current.locating).toBe(false);
  });
});
