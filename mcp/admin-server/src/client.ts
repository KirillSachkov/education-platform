import { logger } from './logger.js';
import type { AuthTokenProvider } from './auth.js';
import type { Config } from './config.js';

export interface PlatformError {
  code: string;
  message: string;
  status: number;
  details?: unknown;
}

export class PlatformApiError extends Error {
  constructor(public readonly info: PlatformError) {
    super(`${info.code}: ${info.message} (HTTP ${info.status})`);
    this.name = 'PlatformApiError';
  }
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PATCH' | 'PUT' | 'DELETE';
  body?: unknown;
  query?: Record<string, string | number | boolean | undefined>;
  timeoutMs?: number;
}

const DEFAULT_TIMEOUT_MS = 30_000;
const MAX_RETRIES = 3;

export class PlatformClient {
  constructor(
    private readonly config: Config,
    private readonly auth: AuthTokenProvider,
  ) {}

  get<T>(path: string, options?: Omit<RequestOptions, 'method' | 'body'>) {
    return this.request<T>(path, { ...options, method: 'GET' });
  }

  post<T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>) {
    return this.request<T>(path, { ...options, method: 'POST', body });
  }

  patch<T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>) {
    return this.request<T>(path, { ...options, method: 'PATCH', body });
  }

  put<T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>) {
    return this.request<T>(path, { ...options, method: 'PUT', body });
  }

  delete<T>(path: string, options?: Omit<RequestOptions, 'method' | 'body'>) {
    return this.request<T>(path, { ...options, method: 'DELETE' });
  }

  private async request<T>(
    path: string,
    options: RequestOptions,
  ): Promise<{ data: T; status: number }> {
    const method = options.method ?? 'GET';
    const url = this.buildUrl(path, options.query);
    const timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
    const isRetrySafe = method === 'GET';

    let lastError: unknown;
    let forceRefreshToken = false;

    for (let attempt = 1; attempt <= MAX_RETRIES; attempt++) {
      try {
        const token = await this.auth.getToken(forceRefreshToken);
        forceRefreshToken = false;

        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), timeoutMs);

        const response = await fetch(url, {
          method,
          headers: {
            Authorization: `Bearer ${token}`,
            Accept: 'application/json',
            ...(options.body !== undefined ? { 'Content-Type': 'application/json' } : {}),
          },
          body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
          signal: controller.signal,
        }).finally(() => clearTimeout(timeout));

        const status = response.status;

        if (status === 401 && attempt === 1) {
          // token might be stale — force refresh and retry once
          forceRefreshToken = true;
          logger.warn({ url }, '401 received, forcing token refresh');
          continue;
        }

        const text = await response.text();
        const payload: unknown = text ? safeJsonParse(text) : null;

        if (!response.ok) {
          const err = toPlatformError(status, payload);
          // Retry only 5xx. 4xx is a caller problem.
          if (isRetrySafe && status >= 500 && attempt < MAX_RETRIES) {
            logger.warn({ url, status, attempt }, 'upstream 5xx, retrying');
            await backoff(attempt);
            continue;
          }
          throw new PlatformApiError(err);
        }

        // Backend wraps success responses in { result, error, isError, timeGenerated }.
        const envelope = payload as
          | { result?: T; error?: unknown; isError?: boolean }
          | null;
        if (envelope && typeof envelope === 'object' && envelope.isError) {
          throw new PlatformApiError(toPlatformError(status, payload));
        }
        const data =
          envelope && typeof envelope === 'object' && 'result' in envelope && !envelope.isError
            ? (envelope.result as T)
            : (payload as T);

        return { data, status };
      } catch (err) {
        lastError = err;
        if (err instanceof PlatformApiError) throw err;
        if (isRetrySafe && attempt < MAX_RETRIES) {
          logger.warn({ url, attempt, err: String(err) }, 'network error, retrying');
          await backoff(attempt);
          continue;
        }
        throw err;
      }
    }

    throw lastError ?? new Error('Unknown request failure');
  }

  private buildUrl(path: string, query?: Record<string, string | number | boolean | undefined>): string {
    const base = this.config.PLATFORM_BASE_URL.replace(/\/+$/, '');
    const normalized = path.startsWith('/') ? path : `/${path}`;
    const url = new URL(base + normalized);
    if (!url.pathname.endsWith('/')) {
      url.pathname += '/';
    }
    if (query) {
      for (const [k, v] of Object.entries(query)) {
        if (v !== undefined) url.searchParams.set(k, String(v));
      }
    }
    return url.toString();
  }
}

function safeJsonParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

function toPlatformError(status: number, payload: unknown): PlatformError {
  if (payload && typeof payload === 'object') {
    const maybeEnvelope = payload as {
      errorCode?: string;
      errorMessage?: string;
      error?: { code?: string; message?: string };
      errors?: Array<{ code?: string; message?: string }>;
    };
    const code =
      maybeEnvelope.errorCode ??
      maybeEnvelope.error?.code ??
      maybeEnvelope.errors?.[0]?.code ??
      `http.${status}`;
    const message =
      maybeEnvelope.errorMessage ??
      maybeEnvelope.error?.message ??
      maybeEnvelope.errors?.[0]?.message ??
      `HTTP ${status}`;
    return { code, message, status, details: payload };
  }
  return { code: `http.${status}`, message: `HTTP ${status}`, status, details: payload };
}

async function backoff(attempt: number): Promise<void> {
  const delay = Math.min(2000, 250 * 2 ** (attempt - 1));
  await new Promise((resolve) => setTimeout(resolve, delay));
}
