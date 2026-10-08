"use client";

import { useTrackGrowthView } from "./use-track-growth-view";

const ENGAGED_AFTER_MS = 30_000;

interface FreeMaterialEngagementOptions {
  materialId: string;
  courseId?: string;
  enabled: boolean;
}

export function useTrackFreeMaterialEngagement({
  materialId,
  courseId,
  enabled,
}: FreeMaterialEngagementOptions): void {
  const key = enabled && materialId ? `${courseId ?? "standalone"}:${materialId}` : "";
  const identity = courseId
    ? { material_id: materialId, course_id: courseId }
    : { material_id: materialId };

  useTrackGrowthView(
    { name: "free_material_open", properties: identity },
    key ? `free-material-open:${key}` : "",
  );
  useTrackGrowthView(
    {
      name: "free_material_engaged",
      properties: { ...identity, engagement: "time_30s" },
    },
    key ? `free-material-engaged:${key}` : "",
    ENGAGED_AFTER_MS,
  );
}
