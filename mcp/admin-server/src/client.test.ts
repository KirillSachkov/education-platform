import assert from 'node:assert/strict';
import { afterEach, describe, it } from 'node:test';
import type { AuthTokenProvider } from './auth.js';
import { PlatformApiError, PlatformClient } from './client.js';
import type { Config } from './config.js';

const originalFetch = globalThis.fetch;

const config: Config = {
  PLATFORM_BASE_URL: 'https://platform.test',
  MCP_CLIENT_ID: 'mcp-admin',
  MCP_CLIENT_SECRET: 'secret',
  LOG_LEVEL: 'error',
  tokenUrl: 'https://platform.test/connect/token',
};

const auth = {
  getToken: async () => 'access-token',
} as AuthTokenProvider;

afterEach(() => {
  globalThis.fetch = originalFetch;
});

describe('PlatformClient', { concurrency: false }, () => {
  it('normalizes platform API paths to a trailing slash', async () => {
    let requestedUrl = '';
    globalThis.fetch = async (input) => {
      requestedUrl = String(input);
      return Response.json({ result: [] });
    };

    const client = new PlatformClient(config, auth);
    await client.get('/api/courses/admin-list', { query: { status: 'DRAFT' } });

    assert.equal(requestedUrl, 'https://platform.test/api/courses/admin-list/?status=DRAFT');
  });

  it('does not retry an ambiguous POST after an upstream 5xx', async () => {
    let calls = 0;
    globalThis.fetch = async () => {
      calls += 1;
      return Response.json({ error: { code: 'failed', message: 'failed' } }, { status: 503 });
    };

    const client = new PlatformClient(config, auth);
    await assert.rejects(() => client.post('/api/materials/', { title: 'Material' }), PlatformApiError);

    assert.equal(calls, 1);
  });

  it('does not retry an ambiguous POST after a network failure', async () => {
    let calls = 0;
    globalThis.fetch = async () => {
      calls += 1;
      throw new TypeError('socket closed after upload');
    };

    const client = new PlatformClient(config, auth);
    await assert.rejects(() => client.post('/api/materials/', { title: 'Material' }));

    assert.equal(calls, 1);
  });

  it('retries a safe GET after a transient 5xx', async () => {
    let calls = 0;
    globalThis.fetch = async () => {
      calls += 1;
      return calls === 1
        ? Response.json({ error: { code: 'unavailable', message: 'unavailable' } }, { status: 503 })
        : Response.json({ result: { ok: true } });
    };

    const client = new PlatformClient(config, auth);
    const response = await client.get<{ ok: boolean }>('/api/ping/');

    assert.deepEqual(response.data, { ok: true });
    assert.equal(calls, 2);
  });

  it('rejects a successful HTTP response carrying an error envelope', async () => {
    globalThis.fetch = async () =>
      Response.json({ isError: true, error: { code: 'domain.failed', message: 'Ошибка' } });

    const client = new PlatformClient(config, auth);

    await assert.rejects(() => client.get('/api/ping/'), PlatformApiError);
  });
});
