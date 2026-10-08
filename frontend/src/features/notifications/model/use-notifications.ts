"use client";

import { useInfiniteQuery } from "@tanstack/react-query";
import { notificationQueryOptions } from "@/entities/notification";
import { useIsAuthenticated } from "@/shared/auth";

const DEFAULT_LIMIT = 20;

interface UseNotificationsParams {
  limit?: number;
  unreadOnly?: boolean;
  /** Server-side фильтр по типам (short-коды NotificationType). Пусто — без фильтра. */
  types?: number[];
}

export function useNotifications({
  limit = DEFAULT_LIMIT,
  unreadOnly,
  types,
}: UseNotificationsParams = {}) {
  const isAuthenticated = useIsAuthenticated();
  const query = useInfiniteQuery({
    ...notificationQueryOptions.listInfinite({ limit, unreadOnly, types }),
    enabled: isAuthenticated,
  });

  return {
    items: query.data?.items ?? [],
    hasNextPage: query.hasNextPage,
    fetchNextPage: query.fetchNextPage,
    isLoading: query.isLoading,
    isFetchingNextPage: query.isFetchingNextPage,
    error: query.error ?? null,
    refetch: query.refetch,
  };
}
