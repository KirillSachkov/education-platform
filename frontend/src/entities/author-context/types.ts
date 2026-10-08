import type { PlanGrantDto } from "@/entities/access-plan";

/**
 * Backend computed tier для personalized space-home (issue #83 / Phase 1.3 #111).
 *
 * Issue #358: tier `free` удалён — у юзера с одним только FREE-grant эффективный
 * доступ теперь = "registered" (бесплатный = system default).
 *
 * - `anonymous` — никогда не возвращается из endpoint'а (auth required)
 * - `registered` — авторизован, но нет ACTIVE grant'а у автора
 * - `course` — есть COURSE/SUBSCRIPTION-grant и нет LEARN_ALL/FULL_ALL
 * - `learn_all` — есть LEARN_ALL grant и нет FULL_ALL
 * - `full_all` — есть FULL_ALL grant
 */
export type AuthorContextTier =
  | "anonymous"
  | "registered"
  | "course"
  | "learn_all"
  | "full_all";

export interface AuthorContextDto {
  hasAnyGrant: boolean;
  highestTier: AuthorContextTier;
  grants: PlanGrantDto[];
}
