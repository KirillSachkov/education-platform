import type { QueryClient } from "@tanstack/react-query";
import { invalidateEducationContent } from "@/entities/course";

/**
 * Backward-compat alias. Use {@link invalidateEducationContent} directly
 * in new code — issue mutations have no special invalidation surface,
 * they share the same six ECS baseKey'и.
 */
export function invalidateIssues(qc: QueryClient): Promise<void> {
  return invalidateEducationContent(qc);
}
