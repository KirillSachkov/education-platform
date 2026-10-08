"use client";

import { createContext, useContext, useEffect, type ReactNode } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { bookmarkQueryOptions } from "../api";
import type { BookmarkTargetType } from "../types";

interface BookmarkStatusContextValue {
  isLoading: boolean;
  isAuthenticated: boolean;
  get: (
    courseId: string,
    type: BookmarkTargetType,
    id: string,
  ) => boolean | undefined;
}

const BookmarkStatusContext = createContext<BookmarkStatusContextValue | null>(
  null,
);

export function useBookmarkStatusContext() {
  return useContext(BookmarkStatusContext);
}

interface BookmarkStatusProviderProps {
  /**
   * Targets visible in the subtree. The provider stays inert (no fetch) for
   * empty arrays so callers can mount it before items load. Internally only
   * the unique courseIds matter — they feed the cursor-paginated server query
   * that returns the user's bookmarks for those courses. The provider
   * auto-fetches every page so deeply bookmarked users still get the full set.
   */
  items: ReadonlyArray<{
    courseId: string;
    entityType: BookmarkTargetType;
    entityId: string;
  }>;
  children: ReactNode;
}

const targetKey = (
  courseId: string,
  type: BookmarkTargetType,
  id: string,
): string => `${courseId}|${type}|${id}`;

export function BookmarkStatusProvider({
  items,
  children,
}: BookmarkStatusProviderProps) {
  const session = useSession();
  const isAuthenticated = session.status === "authenticated";

  const courseIds = Array.from(new Set(items.map((i) => i.courseId))).sort();
  const enabled = isAuthenticated && courseIds.length > 0;

  const {
    data,
    isLoading,
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage,
  } = useInfiniteQuery(
    bookmarkQueryOptions.getMyBookmarkIdsInfiniteOptions(courseIds, enabled),
  );

  // Auto-fetch every page so consumers see the full Set after initial load.
  // useInfiniteQuery exposes `hasNextPage` only after the first page lands.
  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage) {
      void fetchNextPage();
    }
  }, [hasNextPage, isFetchingNextPage, fetchNextPage]);

  const map = new Map<string, boolean>();
  for (const item of data ?? []) {
    map.set(targetKey(item.courseId, item.target.type, item.target.id), true);
  }

  const isStillLoading = enabled && (isLoading || isFetchingNextPage || hasNextPage);

  const value: BookmarkStatusContextValue = {
    isLoading: isStillLoading,
    isAuthenticated,
    get: (courseId, type, id) => map.get(targetKey(courseId, type, id)),
  };

  return (
    <BookmarkStatusContext.Provider value={value}>
      {children}
    </BookmarkStatusContext.Provider>
  );
}
