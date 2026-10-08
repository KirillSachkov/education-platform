"use client";

import { useQuery } from "@tanstack/react-query";
import { leaderboardQueryOptions } from "../api";
import type { GetLeaderboardParams } from "../types";

export function useLeaderboard(params: GetLeaderboardParams) {
  const query = useQuery(leaderboardQueryOptions.getLeaderboardOptions(params));

  return {
    data: query.data ?? null,
    isLoading: query.isLoading,
    error: query.error ?? null,
    refetch: query.refetch,
  };
}
