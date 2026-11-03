import { useEffect, useRef, useState } from 'react';
import type { PlotPoint } from './plotCoordinates';

export function useCurrentLocation() {
  const [locating, setLocating] = useState(false);
  const [locationError, setLocationError] = useState('');
  const [accuracy, setAccuracy] = useState<number | null>(null);
  const requestVersion = useRef(0);
  const mounted = useRef(false);

  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; requestVersion.current += 1; };
  }, []);

  function invalidate() {
    requestVersion.current += 1;
    if (mounted.current) {
      setLocating(false);
      setAccuracy(null);
      setLocationError('');
    }
  }

  function locate(onLocation: (point: PlotPoint) => void) {
    if (!navigator.geolocation) {
      setLocationError('Location is not available in this browser. Enter the coordinates manually.');
      return;
    }
    const version = ++requestVersion.current;
    setLocating(true);
    setLocationError('');
    setAccuracy(null);
    navigator.geolocation.getCurrentPosition(
      position => {
        if (!mounted.current || version !== requestVersion.current) return;
        setLocating(false);
        const { longitude, latitude, accuracy: reportedAccuracy } = position.coords;
        if (!Number.isFinite(longitude) || !Number.isFinite(latitude) ||
            longitude < -180 || longitude > 180 || latitude < -90 || latitude > 90) {
          setLocationError('The device returned invalid coordinates. Enter them manually.');
          return;
        }
        setAccuracy(Number.isFinite(reportedAccuracy) && reportedAccuracy >= 0 ? reportedAccuracy : null);
        onLocation([longitude, latitude]);
      },
      error => {
        if (!mounted.current || version !== requestVersion.current) return;
        setLocating(false);
        setLocationError(error.code === 1
          ? 'Location permission was denied. Enter the coordinates manually.'
          : error.code === 3
            ? 'Finding the device location timed out. Enter the coordinates manually.'
            : 'The device location is unavailable. Enter the coordinates manually.');
      },
      { enableHighAccuracy: true, timeout: 10_000, maximumAge: 0 },
    );
  }

  return { locate, invalidate, locating, locationError, accuracy };
}
