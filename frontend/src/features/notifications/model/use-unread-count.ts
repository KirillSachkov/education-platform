"use client";

import { useQuery } from "@tanstack/react-query";
import { notificationQueryOptions } from "@/entities/notification";
import { useIsAuthenticated } from "@/shared/auth";

export function useUnreadCount() {
  const isAuthenticated = useIsAuthenticated();
  const query = useQuery({
    ...notificationQueryOptions.unreadCount(),
    enabled: isAuthenticated,
  });

  return {
    count: query.data ?? 0,
    isLoading: query.isLoading,
  };
}
