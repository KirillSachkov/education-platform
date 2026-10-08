import { z } from 'zod';
import { defineTool } from '../tool.js';

export const eduPing = defineTool({
  name: 'edu_ping',
  description:
    'Verify the MCP can reach the platform admin API as mcp-admin. Fetches GET /api/courses/admin-list with limit=1. Returns { ok: true, sampleCourse, issuer } on success.',
  inputSchema: z.object({}).strict(),
  handler: async (_input, { client }) => {
    const { data } = await client.get<Array<{ id: string; title: string; status: string }>>(
      '/api/courses/admin-list',
      { query: { limit: 1 } },
    );

    return {
      ok: true,
      sampleCourse: data[0] ?? null,
      returnedCount: data.length,
    };
  },
});
