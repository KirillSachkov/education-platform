"use client";

import { useInfiniteQuery } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { bookmarkQueryOptions } from "../api";
import type { BookmarkTargetType } from "../types";
import { useBookmarkStatusContext } from "./bookmark-status-context";

interface UseBookmarkStatusParams {
  courseId: string;
  entityType: BookmarkTargetType;
  entityId: string;
}

export function useBookmarkStatus({
  courseId,
  entityType,
  entityId,
}: UseBookmarkStatusParams) {
  const session = useSession();
  const isAuthenticated = session.status === "authenticated";
  const ctx = useBookmarkStatusContext();
  const fromContext = ctx !== null;

  const query = useInfiniteQuery(
    bookmarkQueryOptions.getMyBookmarkIdsInfiniteOptions(
      [courseId],
      isAuthenticated && !fromContext,
    ),
  );

  if (fromContext) {
    return {
      isAuthenticated,
      isBookmarked: ctx.get(courseId, entityType, entityId) ?? false,
      isLoading: ctx.isLoading,
      isFetching: ctx.isLoading,
    };
  }

  const isBookmarked = (query.data ?? []).some(
    (item) =>
      item.courseId === courseId &&
      item.target.type === entityType &&
      item.target.id === entityId,
  );

  return {
    isAuthenticated,
    isBookmarked,
    isLoading: isAuthenticated ? query.isLoading : false,
    isFetching: query.isFetching || query.isFetchingNextPage,
  };
}
