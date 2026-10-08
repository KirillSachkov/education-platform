import type { RoadmapProgressItemDto } from "../types";

export type ProgressColor = "default" | "completed" | "in-progress" | "not-started" | "not-enrolled";

const COMPLETED_STATUSES = new Set(["VIEWED", "COMPLETED"]);
const IN_PROGRESS_STATUSES = new Set(["IN_PROGRESS", "UNDER_REVIEW", "REQUESTED_CHANGES"]);

export function getProgressColor(
  entityId: string,
  progressMap: Map<string, RoadmapProgressItemDto>,
  enrolledCourseIds: Set<string>,
  courseId?: string,
): ProgressColor {
  const progress = progressMap.get(entityId);

  if (!progress) {
    if (courseId && !enrolledCourseIds.has(courseId)) {
      return "not-enrolled";
    }
    return "not-started";
  }

  if (COMPLETED_STATUSES.has(progress.status)) return "completed";
  if (IN_PROGRESS_STATUSES.has(progress.status)) return "in-progress";
  return "not-started";
}

export function buildProgressMap(
  items: RoadmapProgressItemDto[],
): Map<string, RoadmapProgressItemDto> {
  return new Map(items.map((item) => [item.entityId, item]));
}

export function computeOverallProgress(
  items: RoadmapProgressItemDto[],
): { completed: number; total: number; percent: number } {
  if (items.length === 0) return { completed: 0, total: 0, percent: 0 };

  const completed = items.filter((i) => COMPLETED_STATUSES.has(i.status)).length;
  const percent = Math.round((completed / items.length) * 100);

  return { completed, total: items.length, percent };
}

export const PROGRESS_COLOR_MAP: Record<ProgressColor, string> = {
  completed: "border-green-500 bg-green-50 dark:bg-green-950/30",
  "in-progress": "border-yellow-500 bg-yellow-50 dark:bg-yellow-950/30",
  "not-started": "border-border bg-card",
  "not-enrolled": "border-muted bg-muted/30 opacity-60",
  default: "border-border bg-card",
};
