import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type { TrainerLimits } from "./types";

export const trainerLimitsApi = {
  /** Остаток AI-лимитов вызывающего + статус Trainer Pro (read-only, без расхода квоты). */
  getMyLimits: async ({ signal }: { signal?: AbortSignal } = {}): Promise<TrainerLimits> => {
    const res = await apiClient.get<Envelope<TrainerLimits>>("/trainer/me/limits/", { signal });
    return res.data.result!;
  },
};

export const trainerLimitsQueryOptions = () =>
  queryOptions({
    queryKey: ["trainer", "me", "limits"] as const,
    queryFn: ({ signal }) => trainerLimitsApi.getMyLimits({ signal }),
    staleTime: 60_000,
  });
