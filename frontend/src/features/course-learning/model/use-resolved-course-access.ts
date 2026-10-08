"use client";

import { useQuery } from "@tanstack/react-query";
import { useCourseAccess } from "@/entities/course";
import type { CourseAccessState } from "@/entities/course";
import { myAuthorContextQueryOptions } from "@/entities/author-context";
import { useRoles } from "@/shared/auth";

export function useResolvedCourseAccess(
  courseId: string,
  authorId: string | null | undefined,
): CourseAccessState {
  const { isAuthenticated } = useRoles();
  const authorContextQuery = useQuery({
    ...myAuthorContextQueryOptions(authorId ?? undefined),
    enabled: isAuthenticated && Boolean(authorId),
  });
  const shouldUseAuthorContext =
    isAuthenticated && Boolean(authorId) && !authorContextQuery.isError;

  return useCourseAccess(courseId, {
    authorContext: shouldUseAuthorContext
      ? (authorContextQuery.data ?? { highestTier: "registered", grants: [] })
      : undefined,
    authorContextLoading: authorContextQuery.isLoading && isAuthenticated && Boolean(authorId),
  });
}
