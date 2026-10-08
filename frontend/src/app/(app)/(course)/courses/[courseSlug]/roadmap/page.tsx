"use client";

import dynamic from "next/dynamic";
import { useCourseId } from "@/shared/providers/course-id-provider";

const RoadmapViewer = dynamic(
  () => import("@/features/roadmap-viewer").then((m) => m.RoadmapViewer),
  { ssr: false },
);

export default function CourseRoadmapPage() {
  const courseId = useCourseId();

  return <RoadmapViewer courseId={courseId} />;
}
