"use client";

import { profileQueryOptions } from "@/entities/profile";
import { useIsAuthenticated } from "@/shared/auth";
import { useQuery } from "@tanstack/react-query";

export function useMyProfile() {
  const isAuthenticated = useIsAuthenticated();
  const query = useQuery({
    ...profileQueryOptions.getMyProfileOptions(),
    enabled: isAuthenticated,
  });

  return {
    profile: query.data,
    isPending: query.isPending,
    error: query.error,
    refetch: query.refetch,
  };
}
