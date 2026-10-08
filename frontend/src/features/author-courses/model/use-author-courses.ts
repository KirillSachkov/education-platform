import { coursesQueryOptions } from "@/entities/course";
import type { CourseKind } from "@/shared/config/course-kind";
import { useInfiniteScroll } from "@/shared/hooks";
import { useInfiniteQuery } from "@tanstack/react-query";

export function useAuthorCourses(kind?: CourseKind) {
  const {
    data,
    isLoading,
    error,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = useInfiniteQuery({
    ...coursesQueryOptions.getMyCoursesInfiniteOptions({
      limit: 10,
      kind,
    }),
  });

  const cursorRef = useInfiniteScroll({
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage,
  });

  return {
    data,
    isLoading,
    error,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
    cursorRef,
  };
}
