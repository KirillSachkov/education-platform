import assert from 'node:assert/strict';
import { afterEach, describe, it } from 'node:test';
import { AuthTokenProvider } from './auth.js';
import type { Config } from './config.js';

const originalFetch = globalThis.fetch;

const config: Config = {
  PLATFORM_BASE_URL: 'https://platform.test',
  MCP_CLIENT_ID: 'mcp-admin',
  MCP_CLIENT_SECRET: 'secret',
  LOG_LEVEL: 'error',
  tokenUrl: 'https://platform.test/connect/token',
};

afterEach(() => {
  globalThis.fetch = originalFetch;
});

describe('AuthTokenProvider', { concurrency: false }, () => {
  it('rejects malformed successful token responses', async () => {
    globalThis.fetch = async () => Response.json({ expires_in: 300 });

    const provider = new AuthTokenProvider(config);

    await assert.rejects(() => provider.getToken(), /invalid token response/i);
  });

  it('bounds token endpoint requests with an abort signal', async () => {
    let requestSignal: AbortSignal | null | undefined;
    globalThis.fetch = async (_input, init) => {
      requestSignal = init?.signal;
      return Response.json({ access_token: 'token', expires_in: 300, token_type: 'Bearer' });
    };

    const provider = new AuthTokenProvider(config);
    await provider.getToken();

    assert.ok(requestSignal instanceof AbortSignal);
  });
});
