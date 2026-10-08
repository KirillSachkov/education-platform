import type { z } from 'zod';
import type { PlatformClient } from './client.js';

export interface ToolContext {
  client: PlatformClient;
}

export interface ToolDefinition<TInput extends z.ZodTypeAny, TOutput> {
  name: string;
  description: string;
  inputSchema: TInput;
  handler: (input: z.infer<TInput>, ctx: ToolContext) => Promise<TOutput>;
}

export function defineTool<TInput extends z.ZodTypeAny, TOutput>(
  def: ToolDefinition<TInput, TOutput>,
): ToolDefinition<TInput, TOutput> {
  return def;
}
