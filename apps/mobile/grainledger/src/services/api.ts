import { storage } from './storage';

const API_BASE_URL_KEY = 'grainledger_api_base_url';
const DEFAULT_API_BASE_URL = 'http://localhost:5080';

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

  private async init() {
    const saved = await storage.get<string>(API_BASE_URL_KEY);
    if (saved) {
      this.baseUrl = saved;
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
    this.baseUrl = url.trim().replace(/\/+$/, '');
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
    const url = `${this.baseUrl}${endpoint.startsWith('/') ? endpoint : `/${endpoint}`}`;

    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      Accept: 'application/json',
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

      const data = (await response.json()) as T;
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
