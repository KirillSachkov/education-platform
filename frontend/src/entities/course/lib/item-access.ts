import type { AccessType } from "@/shared/config/access-type";
import type { LockReason } from "@/shared/lib/lock-copy";

export type CourseAccessLevel = "anonymous" | "authenticated" | "standard" | "admin";

type CourseAccessPlan = {
  tier?: string | null;
  kind?: string | null;
  /** Bundle-набор курсов плана (#404 — заменил singular `courseId`). */
  courseIds?: string[] | null;
  includesFutureContent?: boolean | null;
};

export type CourseAuthorAccessContext = {
  highestTier?: string | null;
  grants?: Array<{ plan?: CourseAccessPlan | null }> | null;
} | null;

/**
 * Check if a user with the given access level can access an item with the given accessType.
 *
 * Matrix (issue #358: FREE removed, бесплатный = system default = REGISTERED):
 * | Level         | PUBLIC | REGISTERED | ENROLLED |
 * |---------------|--------|------------|----------|
 * | anonymous     | yes    | no         | no       |
 * | authenticated | yes    | yes        | no       |
 * | standard      | yes    | yes        | yes      |
 * | admin         | yes    | yes        | yes      |
 */
export function canAccessItem(
  accessType: AccessType | null | undefined,
  accessLevel: CourseAccessLevel,
): boolean {
  if (accessLevel === "admin") return true;
  if (!accessType) return false;

  switch (accessType) {
    case "PUBLIC":
      return true;
    case "REGISTERED":
      return accessLevel !== "anonymous";
    case "ENROLLED":
      return accessLevel === "standard";
    default:
      return false;
  }
}

/**
 * Derive the effective CourseAccessLevel from auth + enrollment state.
 *
 * Phase E (#45): «standard» level больше не зависит от enrollmentStatus —
 * backend теперь либо возвращает CourseLearningStateDto (есть enrollment), либо null.
 */
export function deriveCourseAccessLevel(opts: {
  isAuthenticated: boolean;
  isAdmin: boolean;
  hasEnrollment: boolean;
  courseId?: string | null;
  authorContext?: CourseAuthorAccessContext;
}): CourseAccessLevel {
  if (opts.isAdmin) return "admin";
  if (!opts.isAuthenticated) return "anonymous";

  if (opts.authorContext) {
    if (hasFullAuthorAccess(opts.authorContext)) return "standard";
    if (hasCourseAccess(opts.authorContext, opts.courseId)) return "standard";
    return "authenticated";
  }

  if (opts.hasEnrollment) return "standard";
  return "authenticated";
}

/**
 * Derive the LockReason an item would show, given viewer's access level and
 * item's AccessType. Mirrors backend {@link ContentAccess.LockReasonResolver} for
 * DTOs that don't carry `lockReason` directly (notably the course curriculum
 * endpoint, which is cached in three buckets and returns only `accessType`).
 *
 * Returns `null` if item is accessible.
 */
export function deriveLockReasonForItem(
  accessType: AccessType | null | undefined,
  accessLevel: CourseAccessLevel,
): LockReason | null {
  if (canAccessItem(accessType, accessLevel)) return null;
  if (accessLevel === "anonymous") return "anonymous";
  if (accessType === "ENROLLED") return "not_enrolled";
  // REGISTERED + authenticated-or-higher is accessible; shouldn't reach here.
  // Fallback for unexpected combos.
  return "not_enrolled";
}

function normalizeTier(plan: CourseAccessPlan | null | undefined): string | null {
  return (plan?.tier ?? plan?.kind ?? null)?.toUpperCase() ?? null;
}

function hasFullAuthorAccess(ctx: Exclude<CourseAuthorAccessContext, null>): boolean {
  const tier = ctx.highestTier;
  if (tier === "full_all" || tier === "learn_all") return true;

  return (ctx.grants ?? []).some((grant) => {
    const planTier = normalizeTier(grant.plan);
    return planTier === "FULL_ALL" || planTier === "LEARN_ALL";
  });
}

function hasCourseAccess(
  ctx: Exclude<CourseAuthorAccessContext, null>,
  courseId: string | null | undefined,
): boolean {
  if (!courseId) return false;

  return (ctx.grants ?? []).some((grant) => {
    const plan = grant.plan;
    const planTier = normalizeTier(plan);
    if (planTier !== "COURSE" && planTier !== "SUBSCRIPTION") return false;
    if (plan?.includesFutureContent) return true;
    return (plan?.courseIds ?? []).includes(courseId);
  });
}
