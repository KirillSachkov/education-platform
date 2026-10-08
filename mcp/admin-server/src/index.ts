#!/usr/bin/env node
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from '@modelcontextprotocol/sdk/types.js';
import { zodToJsonSchema } from './zod-to-json-schema.js';
import { loadConfig } from './config.js';
import { logger } from './logger.js';
import { AuthTokenProvider } from './auth.js';
import { PlatformClient, PlatformApiError } from './client.js';
import { allTools } from './tools/index.js';

const config = loadConfig();
const auth = new AuthTokenProvider(config);
const client = new PlatformClient(config, auth);
const ctx = { client };

const toolsByName = new Map(allTools.map((t) => [t.name, t]));

const server = new Server(
  { name: 'platform-admin-mcp', version: '0.1.0' },
  { capabilities: { tools: {} } },
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({
  tools: allTools.map((t) => ({
    name: t.name,
    description: t.description,
    inputSchema: zodToJsonSchema(t.inputSchema),
  })),
}));

server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const name = request.params.name;
  const tool = toolsByName.get(name);
  if (!tool) {
    return {
      isError: true,
      content: [{ type: 'text', text: `Unknown tool: ${name}` }],
    };
  }

  const parsed = tool.inputSchema.safeParse(request.params.arguments ?? {});
  if (!parsed.success) {
    return {
      isError: true,
      content: [
        {
          type: 'text',
          text: `Invalid input for ${name}: ${parsed.error.issues
            .map((issue: { path: (string | number)[]; message: string }) =>
              `${issue.path.join('.')} ${issue.message}`)
            .join('; ')}`,
        },
      ],
    };
  }

  const started = Date.now();
  try {
    const result = await tool.handler(parsed.data, ctx);
    const durationMs = Date.now() - started;
    logger.info({ tool: name, durationMs }, 'tool ok');
    return {
      content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
    };
  } catch (err) {
    const durationMs = Date.now() - started;
    if (err instanceof PlatformApiError) {
      logger.warn({ tool: name, durationMs, error: err.info }, 'tool upstream error');
      return {
        isError: true,
        content: [
          {
            type: 'text',
            text: JSON.stringify(
              { error: err.info.code, message: err.info.message, status: err.info.status },
              null,
              2,
            ),
          },
        ],
      };
    }
    logger.error({ tool: name, durationMs, err: String(err) }, 'tool error');
    return {
      isError: true,
      content: [{ type: 'text', text: `Unexpected error: ${String(err)}` }],
    };
  }
});

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  logger.info(
    { baseUrl: config.PLATFORM_BASE_URL, clientId: config.MCP_CLIENT_ID, toolCount: allTools.length },
    'platform-admin-mcp ready',
  );
}

main().catch((err) => {
  logger.error({ err: String(err) }, 'fatal startup error');
  process.exit(1);
});
