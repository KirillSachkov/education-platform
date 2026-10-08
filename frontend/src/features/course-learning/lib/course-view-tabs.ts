import type { CourseViewTab } from "@/entities/course";

export function isCourseViewTab(value: string | null | undefined): value is CourseViewTab {
  return value === "modules" || value === "projects";
}

export function parseCourseViewTab(
  value: string | null | undefined,
  fallback: CourseViewTab = "modules",
): CourseViewTab {
  return isCourseViewTab(value) ? value : fallback;
}
