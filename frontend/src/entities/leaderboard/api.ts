import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type { GetLeaderboardParams, GetLeaderboardResponse } from "./types";

export const leaderboardApi = {
  getLeaderboard: async (
    params: GetLeaderboardParams,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<GetLeaderboardResponse>>(
      "/progress/leaderboard/",
      { params, signal },
    );

    return res.data;
  },
};

export const leaderboardQueryOptions = {
  baseKey: "leaderboard",

  getLeaderboardKey: (params: GetLeaderboardParams) =>
    [leaderboardQueryOptions.baseKey, params] as const,

  getLeaderboardOptions: (params: GetLeaderboardParams) =>
    queryOptions({
      queryKey: leaderboardQueryOptions.getLeaderboardKey(params),
      queryFn: ({ signal }) => leaderboardApi.getLeaderboard(params, { signal }),
      select: (data) => data.result,
    }),
};
