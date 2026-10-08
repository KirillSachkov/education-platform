import { z } from 'zod';

const envSchema = z.object({
  PLATFORM_BASE_URL: z.string().url().default('http://localhost'),
  MCP_CLIENT_ID: z.string().min(1).default('mcp-admin'),
  MCP_CLIENT_SECRET: z.string().min(1),
  LOG_LEVEL: z.enum(['trace', 'debug', 'info', 'warn', 'error']).default('info'),
});

export type Config = z.infer<typeof envSchema> & {
  tokenUrl: string;
};

export function loadConfig(): Config {
  const parsed = envSchema.safeParse(process.env);
  if (!parsed.success) {
    const issues = parsed.error.issues
      .map((i) => `${i.path.join('.')}: ${i.message}`)
      .join('; ');
    throw new Error(`Invalid MCP environment: ${issues}`);
  }
  const base = parsed.data.PLATFORM_BASE_URL.replace(/\/+$/, '');
  return {
    ...parsed.data,
    tokenUrl: `${base}/connect/token`,
  };
}
