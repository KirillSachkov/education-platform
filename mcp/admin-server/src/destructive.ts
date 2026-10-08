/**
 * Runtime guard for destructive MCP tools (delete / archive / publish / restore).
 *
 * Tool schemas are strict, but invocation still requires an explicit
 * `confirm` parameter equal to the target resource ID. This prevents an AI
 * agent from accidentally hard-deleting or mass-publishing prod content
 * after misreading a task — the model has to plumb the same UUID through
 * two parameters, which makes an "oops" call much less likely.
 *
 * Pattern:
 *   inputSchema: z.object({
 *     materialId: z.string().uuid(),
 *     confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
 *   }).strict(),
 *   handler: async ({ materialId, confirm }, { client }) => {
 *     requireConfirm('edu_material_delete', materialId, confirm);
 *     // ... backend call
 *   }
 */

export class ConfirmMismatchError extends Error {
  readonly toolName: string;
  readonly expected: string;

  constructor(toolName: string, expected: string) {
    super(
      `Destructive operation "${toolName}" requires the "confirm" parameter ` +
        `to equal "${expected}" exactly. Pass the same resource ID you pass ` +
        `in the primary id field. This guards against accidental ` +
        `delete/archive/publish/restore calls by AI agents without explicit ` +
        `human confirmation.`,
    );
    this.name = 'ConfirmMismatchError';
    this.toolName = toolName;
    this.expected = expected;
  }
}

export function requireConfirm(toolName: string, expected: string, provided: string): void {
  if (provided !== expected) {
    throw new ConfirmMismatchError(toolName, expected);
  }
}

export const CONFIRM_PARAM_DESCRIPTION =
  'Destructive-op guard. MUST equal the target resource ID (UUID) exactly. ' +
  'Pass the same value you pass in the primary id field of this tool. ' +
  'Prevents accidental invocation by AI agents without explicit human confirmation.';
