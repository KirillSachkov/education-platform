"use client";

import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "next/navigation";
import { courseCurriculumQueryOptions, getAdjacentLearningItems } from "@/entities/course";
import { useResolvedCourseAccess, useTrackCoursePositionOnMount } from "@/features/course-learning";
import { CourseMaterialView } from "@/widgets/material-view";
import { useCourseId } from "@/shared/providers/course-id-provider";

export function SpaceCourseMaterialClient({ materialId }: { materialId: string }) {
  const courseId = useCourseId();
  const searchParams = useSearchParams();
  const sectionId = searchParams.get("section");

  const { data: curriculum } = useQuery(courseCurriculumQueryOptions(courseId));

  const access = useResolvedCourseAccess(courseId, curriculum?.authorId);
  const navigation = getAdjacentLearningItems(curriculum, materialId, sectionId);
  const canRecordPosition =
    access.hasActiveEnrollment &&
    !!navigation.currentItem &&
    access.canAccessItem(navigation.currentItem.accessType);
  // Записываем «последнюю точку» только для зачисленных — у анонимов и preview-просмотра
  // нет course-position'а в принципе.
  useTrackCoursePositionOnMount(canRecordPosition ? courseId : null, "MATERIAL", materialId);

  const moduleId = navigation.currentItem?.moduleId ?? undefined;

  return <CourseMaterialView courseId={courseId} materialId={materialId} moduleId={moduleId} />;
}
