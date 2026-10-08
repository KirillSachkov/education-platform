import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type { MyActivityDto } from "./types";

export const userActivityApi = {
  getMyActivity: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<MyActivityDto>>("/progress/my/activity/", {
      signal,
    });

    return res.data;
  },
};

export const userActivityQueryOptions = {
  baseKey: "user-activity",

  myActivityKey: () => [userActivityQueryOptions.baseKey, "me"] as const,

  myActivityOptions: () =>
    queryOptions({
      queryKey: userActivityQueryOptions.myActivityKey(),
      queryFn: ({ signal }) => userActivityApi.getMyActivity({ signal }),
      select: (data) => data.result,
    }),
};
