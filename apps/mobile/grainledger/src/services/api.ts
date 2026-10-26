import { Platform } from 'react-native';
import { storage } from './storage';

const API_BASE_URL_KEY = 'grainledger_api_base_url';
// On Android emulator, 10.0.2.2 points to host machine loopback; on iOS/web it is localhost
const DEFAULT_API_BASE_URL =
  Platform.OS === 'android' ? 'http://10.0.2.2:5080' : 'http://localhost:5080';

export class ApiService {
  private static instance: ApiService;
  private baseUrl: string = DEFAULT_API_BASE_URL;
  private token: string | null = null;

  private constructor() {
    this.init();
  }

  public static getInstance(): ApiService {
    if (!ApiService.instance) {
      ApiService.instance = new ApiService();
    }
    return ApiService.instance;
  }

  public normalizeUrl(url: string): string {
    let clean = url.trim();
    if (!clean) return DEFAULT_API_BASE_URL;
    if (!/^https?:\/\//i.test(clean)) {
      clean = `http://${clean}`;
    }
    return clean.replace(/\/+$/, '');
  }

  private async init() {
    const saved = await storage.get<string>(API_BASE_URL_KEY);
    if (saved) {
      this.baseUrl = this.normalizeUrl(saved);
    }
    const token = await storage.get<string>('grainledger_token');
    if (token) {
      this.token = token;
    }
  }

  public getBaseUrl(): string {
    return this.baseUrl;
  }

  public async setBaseUrl(url: string): Promise<void> {
    this.baseUrl = this.normalizeUrl(url);
    await storage.set(API_BASE_URL_KEY, this.baseUrl);
  }

  public setToken(token: string | null): void {
    this.token = token;
    if (token) {
      storage.set('grainledger_token', token);
    } else {
      storage.remove('grainledger_token');
    }
  }

  public async request<T>(
    endpoint: string,
    options: RequestInit = {}
  ): Promise<{ data: T | null; error: string | null; isMock: boolean }> {
    const normalizedEndpoint = endpoint.startsWith('/') ? endpoint : `/${endpoint}`;
    const url = `${this.baseUrl}${normalizedEndpoint}`;

    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      Accept: 'application/json, text/plain, */*',
      ...(options.headers as Record<string, string>),
    };

    if (this.token) {
      headers['Authorization'] = `Bearer ${this.token}`;
    }

    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 6000); // 6s timeout

      const response = await fetch(url, {
        ...options,
        headers,
        signal: controller.signal,
      });

      clearTimeout(timeoutId);

      if (!response.ok) {
        const errorText = await response.text();
        return {
          data: null,
          error: `HTTP ${response.status}: ${errorText || response.statusText}`,
          isMock: false,
        };
      }

      const contentType = response.headers.get('content-type') || '';
      let data: any = null;
      if (contentType.includes('application/json')) {
        data = (await response.json()) as T;
      } else {
        const text = await response.text();
        try {
          data = JSON.parse(text) as T;
        } catch {
          data = text as unknown as T;
        }
      }

      return { data, error: null, isMock: false };
    } catch (err: any) {
      return {
        data: null,
        error: err?.message || 'Network request failed (offline / host unreachable)',
        isMock: true,
      };
    }
  }
}

export const api = ApiService.getInstance();
