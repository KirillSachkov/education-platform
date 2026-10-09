import type { LastActiveCourseDto } from "@/entities/enrollment";
import { routes } from "@/shared/config/routes";

export function resolveContinueHref(course: LastActiveCourseDto): string {
  const { lastPosition, courseSlug } = course;
  if (!lastPosition) return routes.courseOverview(courseSlug);
  return lastPosition.entityType === "MATERIAL"
    ? routes.courseMaterial(courseSlug, lastPosition.entityId)
    : routes.courseIssue(courseSlug, lastPosition.entityId);
}
