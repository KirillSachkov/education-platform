import { logger } from './logger.js';
import type { Config } from './config.js';

interface TokenCacheEntry {
  accessToken: string;
  expiresAt: number; // epoch ms
}

const REFRESH_LEAD_MS = 60_000; // refresh 60s before actual expiry
const TOKEN_REQUEST_TIMEOUT_MS = 10_000;

export class AuthTokenProvider {
  private cache: TokenCacheEntry | null = null;
  private inflight: Promise<string> | null = null;

  constructor(private readonly config: Config) {}

  async getToken(forceRefresh = false): Promise<string> {
    const now = Date.now();
    if (!forceRefresh && this.cache && this.cache.expiresAt - REFRESH_LEAD_MS > now) {
      return this.cache.accessToken;
    }

    if (this.inflight) return this.inflight;

    this.inflight = this.fetchToken().finally(() => {
      this.inflight = null;
    });
    return this.inflight;
  }

  private async fetchToken(): Promise<string> {
    const body = new URLSearchParams({
      grant_type: 'client_credentials',
      client_id: this.config.MCP_CLIENT_ID,
      client_secret: this.config.MCP_CLIENT_SECRET,
      scope: 'platform',
    });

    const response = await fetch(this.config.tokenUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body,
      signal: AbortSignal.timeout(TOKEN_REQUEST_TIMEOUT_MS),
    });

    if (!response.ok) {
      const text = await response.text();
      logger.error({ status: response.status, body: text }, 'token request failed');
      throw new Error(`Token request failed: ${response.status} ${text}`);
    }

    const json: unknown = await response.json().catch(() => null);
    if (
      !isRecord(json) ||
      typeof json.access_token !== 'string' ||
      json.access_token.length === 0 ||
      typeof json.expires_in !== 'number' ||
      !Number.isFinite(json.expires_in) ||
      json.expires_in <= 0
    ) {
      throw new Error('Invalid token response from identity provider');
    }

    const expiresAt = Date.now() + json.expires_in * 1000;
    this.cache = { accessToken: json.access_token, expiresAt };
    logger.debug({ expiresAt }, 'access token cached');
    return json.access_token;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
