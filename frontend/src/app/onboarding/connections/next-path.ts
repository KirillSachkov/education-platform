import { resolveWorkspaceCallback } from "@/shared/lib/workspace-callback";

/** Resume an existing content destination after account linking. */
export function resolveNextPath(raw: string | null | undefined): string {
  return resolveWorkspaceCallback(raw);
}
