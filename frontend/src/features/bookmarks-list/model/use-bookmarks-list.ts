"use client";

import { bookmarkQueryOptions, type BookmarkTargetType } from "@/entities/bookmark";
import { useInfiniteQuery } from "@tanstack/react-query";

const DEFAULT_LIMIT = 20;

interface UseBookmarksListParams {
  limit?: number;
  courseId?: string;
  entityType?: BookmarkTargetType;
  /** Fire the request only when enabled (e.g. user is authenticated). Defaults to `true`. */
  enabled?: boolean;
}

export function useBookmarksList({
  limit = DEFAULT_LIMIT,
  courseId,
  entityType,
  enabled = true,
}: UseBookmarksListParams = {}) {
  const query = useInfiniteQuery({
    ...bookmarkQueryOptions.getBookmarksInfiniteOptions({
      limit,
      courseId,
      entityType,
    }),
    enabled,
  });

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
