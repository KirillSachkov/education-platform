"use client";

import { useQuery } from "@tanstack/react-query";
import { courseCurriculumQueryOptions, getAdjacentLearningItems } from "@/entities/course";
import { useResolvedCourseAccess, useTrackCoursePositionOnMount } from "@/features/course-learning";
import { IssueView } from "@/widgets/issue-view/issue-view";
import { useCourseId } from "@/shared/providers/course-id-provider";

export function IssuePageClient({ issueId }: { issueId: string }) {
  const courseId = useCourseId();
  const { data: curriculum } = useQuery(courseCurriculumQueryOptions(courseId));
  const access = useResolvedCourseAccess(courseId, curriculum?.authorId);
  const navigation = getAdjacentLearningItems(curriculum, issueId);
  const canRecordPosition =
    access.hasActiveEnrollment &&
    !!navigation.currentItem &&
    access.canAccessItem(navigation.currentItem.accessType);
  // Запись last-position только для зачисленных (см. material page).
  useTrackCoursePositionOnMount(canRecordPosition ? courseId : null, "ISSUE", issueId);
  return <IssueView courseId={courseId} issueId={issueId} />;
}
