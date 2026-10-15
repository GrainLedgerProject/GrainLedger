import { api } from './api';

export type CropSeason = { id: string; companyId: string; name: string; startsOn: string; endsOn: string };
type CropSeasonList = { items: CropSeason[]; page: number; pageSize: number; hasMore: boolean };

export async function loadCropSeasons(signal: AbortSignal): Promise<CropSeason[]> {
  const seasons: CropSeason[] = [];
  for (let page = 1; page <= 100; page++) {
    const response = await api<CropSeasonList>(`/api/crop-seasons?page=${page}&pageSize=100`, { signal });
    seasons.push(...response.items);
    if (!response.hasMore) return seasons;
  }
  throw new Error('The crop season list is too large to load.');
}
