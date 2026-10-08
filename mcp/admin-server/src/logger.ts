import pino from 'pino';

// stdio transport uses stdout for JSON-RPC, so logs must go to stderr.
export const logger = pino(
  {
    level: process.env.LOG_LEVEL ?? 'info',
    base: { component: 'platform-admin-mcp' },
  },
  pino.destination(2),
);
