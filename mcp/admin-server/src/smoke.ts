#!/usr/bin/env node
/**
 * Smoke check — requests an access token from the platform and pings the
 * admin-list endpoint. Run after starting local infra (./scripts/dev.sh up).
 *
 *   node dist/smoke.js
 *
 * Exits 0 on success, 1 on any failure.
 */
import { loadConfig } from './config.js';
import { AuthTokenProvider } from './auth.js';
import { PlatformClient } from './client.js';
import { logger } from './logger.js';

async function main() {
  const config = loadConfig();
  const auth = new AuthTokenProvider(config);

  logger.info({ tokenUrl: config.tokenUrl }, 'requesting access token');
  const token = await auth.getToken();
  const claims = decodeJwtClaims(token);
  const roles = claims.roles as string[] | string | undefined;

  // eslint-disable-next-line no-console
  console.log(
    JSON.stringify(
      {
        tokenOk: true,
        clientId: config.MCP_CLIENT_ID,
        subject: claims.sub,
        roles,
        audience: claims.aud,
        expiresAt: new Date((claims.exp as number) * 1000).toISOString(),
      },
      null,
      2,
    ),
  );

  if (process.env.SMOKE_CALL_ECS !== '0') {
    const client = new PlatformClient(config, auth);
    try {
      const { data } = await client.get<Array<{ id: string; title: string; status: string }>>(
        '/api/courses/admin-list',
        { query: { limit: 3 } },
      );
      // eslint-disable-next-line no-console
      console.log(JSON.stringify({ ecsOk: true, count: data.length, sample: data }, null, 2));
    } catch (err) {
      // eslint-disable-next-line no-console
      console.log(JSON.stringify({ ecsOk: false, reason: String(err) }, null, 2));
    }
  }
}

function decodeJwtClaims(token: string): Record<string, unknown> {
  const [, payload] = token.split('.');
  if (!payload) throw new Error('Invalid JWT');
  const normalized = payload.replace(/-/g, '+').replace(/_/g, '/');
  const padded = normalized + '='.repeat((4 - (normalized.length % 4)) % 4);
  const decoded = Buffer.from(padded, 'base64').toString('utf8');
  return JSON.parse(decoded) as Record<string, unknown>;
}

main().catch((err) => {
  // eslint-disable-next-line no-console
  console.error('smoke failed:', err);
  process.exit(1);
});
