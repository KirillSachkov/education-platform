"use client";

import { enrollmentQueryOptions } from "@/entities/enrollment";
import { useInfiniteQuery } from "@tanstack/react-query";

const DEFAULT_LIMIT = 6;

export function useMyCourseProgress(limit = DEFAULT_LIMIT) {
  const query = useInfiniteQuery(
    enrollmentQueryOptions.getMyCourseProgressInfiniteOptions({ limit }),
  );

  return {
    items: query.data?.items ?? [],
    totalCount: query.data?.totalCount ?? 0,
    hasNextPage: query.hasNextPage,
    fetchNextPage: query.fetchNextPage,
    isLoading: query.isLoading,
    isFetchingNextPage: query.isFetchingNextPage,
    error: query.error ?? null,
    refetch: query.refetch,
  };
}
