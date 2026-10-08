import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type { TrainerTrack } from "./types";

export const trainerTracksApi = {
  /**
   * PUBLISHED-треки + число опубликованных тем в каждом — верхний селектор хаба.
   * Метаданные треков не gated (как каталог курсов): аноним тоже получит список.
   */
  getTracks: async ({ signal }: { signal?: AbortSignal } = {}): Promise<TrainerTrack[]> => {
    const res = await apiClient.get<Envelope<TrainerTrack[]>>("/trainer/tracks/", { signal });
    return res.data.result ?? [];
  },
};

export const trainerTracksQueryOptions = {
  baseKey: "trainer-tracks",

  tracksKey: () => [trainerTracksQueryOptions.baseKey] as const,

  /** Список опубликованных треков (верхний селектор). */
  tracksOptions: () =>
    queryOptions({
      queryKey: trainerTracksQueryOptions.tracksKey(),
      queryFn: ({ signal }) => trainerTracksApi.getTracks({ signal }),
      staleTime: 5 * 60_000,
    }),
};
