import { useEffect, useRef, useState } from 'react';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import type { PlotPoint } from './plotCoordinates';
import { toMapPoint } from './plotCoordinates';

const defaultCenter: [number, number] = [10.4, 106.35];
const configuredTileUrl = import.meta.env.VITE_MAP_TILE_URL;
const configuredAttribution = import.meta.env.VITE_MAP_ATTRIBUTION;
const sameOriginTileUrl = typeof configuredTileUrl === 'string' && configuredTileUrl.startsWith('/') && !configuredTileUrl.startsWith('//')
  ? configuredTileUrl : null;
const developmentTileUrl = import.meta.env.DEV ? 'https://tile.openstreetmap.org/{z}/{x}/{y}.png' : null;
const tileUrl = sameOriginTileUrl ?? developmentTileUrl;

const plotIcon = L.divIcon({
  className: 'plot-map-marker',
  html: '<span aria-hidden="true"></span>',
  iconSize: [22, 22],
  iconAnchor: [11, 11],
});

class CoordinateGrid extends L.GridLayer {
  createTile(coords: L.Coords) {
    const tile = document.createElement('div');
    tile.className = 'plot-map-grid-tile';
    const count = 2 ** coords.z;
    const longitude = ((coords.x + 0.5) / count) * 360 - 180;
    const mercatorY = Math.PI * (1 - 2 * (coords.y + 0.5) / count);
    const latitude = Math.atan(Math.sinh(mercatorY)) * 180 / Math.PI;
    tile.textContent = `${latitude.toFixed(3)}°, ${longitude.toFixed(3)}°`;
    return tile;
  }
}

export default function PlotMap({ point, polygon = null, onChange, disabled = false }: {
  point: PlotPoint | null;
  polygon?: number[][][] | null;
  onChange?: (point: PlotPoint) => void;
  disabled?: boolean;
}) {
  const container = useRef<HTMLDivElement>(null);
  const map = useRef<L.Map | null>(null);
  const marker = useRef<L.Marker | null>(null);
  const boundary = useRef<L.Polygon | null>(null);
  const onChangeRef = useRef(onChange);
  const disabledRef = useRef(disabled);
  const [tilesFailed, setTilesFailed] = useState(false);
  onChangeRef.current = onChange;
  disabledRef.current = disabled;

  useEffect(() => {
    if (!container.current) return;
    const firstVertex = polygon?.[0]?.[0];
    const initial = point ? toMapPoint(point)
      : firstVertex && firstVertex.length >= 2 ? toMapPoint([firstVertex[0], firstVertex[1]]) : defaultCenter;
    const instance = L.map(container.current, { center: initial, zoom: point ? 15 : 10 });
    map.current = instance;
    new CoordinateGrid({ tileSize: 256, maxZoom: 19 }).addTo(instance);
    if (tileUrl) {
      const imagery = L.tileLayer(tileUrl, {
        maxZoom: 19,
        attribution: tileUrl === developmentTileUrl ? '&copy; OpenStreetMap contributors'
          : typeof configuredAttribution === 'string' ? configuredAttribution : '',
      });
      imagery.on('tileerror', () => setTilesFailed(true));
      imagery.on('tileload', () => setTilesFailed(false));
      imagery.addTo(instance);
    }
    instance.on('click', event => {
      if (!disabledRef.current) onChangeRef.current?.([event.latlng.lng, event.latlng.lat]);
    });
    const resize = window.requestAnimationFrame(() => instance.invalidateSize());
    return () => {
      window.cancelAnimationFrame(resize);
      instance.remove();
      map.current = null;
      marker.current = null;
      boundary.current = null;
    };
  }, []);

  useEffect(() => {
    const instance = map.current;
    if (!instance) return;
    if (!point) {
      if (marker.current) { instance.removeLayer(marker.current); marker.current = null; }
      return;
    }
    const position = toMapPoint(point);
    if (!marker.current) {
      const pin = L.marker(position, { icon: plotIcon, draggable: !disabled });
      pin.on('dragend', () => {
        const { lng, lat } = pin.getLatLng();
        if (!disabledRef.current) onChangeRef.current?.([lng, lat]);
      });
      pin.addTo(instance);
      marker.current = pin;
    } else {
      marker.current.setLatLng(position);
      if (disabled) marker.current.dragging?.disable();
      else marker.current.dragging?.enable();
    }
    instance.setView(position, Math.max(instance.getZoom(), 15));
  }, [point, disabled]);

  useEffect(() => {
    const instance = map.current;
    if (!instance) return;
    if (boundary.current) {
      instance.removeLayer(boundary.current);
      boundary.current = null;
    }
    if (!polygon?.length) return;
    const rings = polygon.map(ring => ring.map(vertex => [vertex[1], vertex[0]] as [number, number]));
    const layer = L.polygon(rings, { color: '#21573f', fillColor: '#64a874', fillOpacity: 0.2, weight: 3 }).addTo(instance);
    boundary.current = layer;
    if (layer.getBounds().isValid()) instance.fitBounds(layer.getBounds(), { padding: [24, 24], maxZoom: 16 });
  }, [polygon]);

  return <div className="plot-map-wrap">
    <div ref={container} className="plot-map" role="application" aria-label="Plot location map" />
    {!tileUrl && <p className="plot-map-caption">Coordinate grid only. Select a point or enter coordinates below.</p>}
    {tilesFailed && <p className="plot-map-caption" role="status">Map imagery is unavailable. Select a point on the coordinate grid or enter coordinates below.</p>}
  </div>;
}
