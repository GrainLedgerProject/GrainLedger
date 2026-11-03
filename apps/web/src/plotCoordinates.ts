export type PlotPoint = [longitude: number, latitude: number];

export function parsePlotPoint(longitude: string, latitude: string): PlotPoint | null {
  if (!longitude.trim() || !latitude.trim()) return null;
  const lon = Number(longitude);
  const lat = Number(latitude);
  if (!Number.isFinite(lon) || lon < -180 || lon > 180 ||
      !Number.isFinite(lat) || lat < -90 || lat > 90) return null;
  return [lon, lat];
}

export function toMapPoint([longitude, latitude]: PlotPoint): [latitude: number, longitude: number] {
  return [latitude, longitude];
}
