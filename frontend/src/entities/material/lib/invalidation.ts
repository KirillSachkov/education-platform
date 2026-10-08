import type { QueryClient } from "@tanstack/react-query";
import { invalidateEducationContent } from "@/entities/course";

/**
 * Backward-compat alias. Use {@link invalidateEducationContent} directly
 * in new code — material mutations have no special invalidation surface,
 * they share the same six ECS baseKey'и.
 */
export function invalidateMaterials(qc: QueryClient): Promise<void> {
  return invalidateEducationContent(qc);
}
