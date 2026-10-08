import { apiClient, type Envelope } from "@/shared/api";
import { keepPreviousData, queryOptions } from "@tanstack/react-query";
import type { TrainerProgress, TrainerTopicListItem } from "./types";

/** Фильтр списка тем: трек + направление (оба опциональны — зеркало `?trackId=&direction=`). */
export interface TrainerTopicsFilter {
  trackId?: string;
  direction?: string;
}

export const trainerTopicsApi = {
  /**
   * PUBLISHED-темы + персональный mastery + фримиум-флаги (`hasFreeBank`,
   * `isLocked`). Опциональный фильтр по треку/направлению. Метаданные тем не
   * gated (как каталог курсов) — аноним получит список без mastery.
   */
  getTopics: async (
    filter: TrainerTopicsFilter = {},
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerTopicListItem[]> => {
    const params = new URLSearchParams();
    if (filter.trackId) params.set("trackId", filter.trackId);
    if (filter.direction) params.set("direction", filter.direction);
    const query = params.toString();
    const res = await apiClient.get<Envelope<TrainerTopicListItem[]>>(
      `/trainer/topics/${query ? `?${query}` : ""}`,
      { signal },
    );
    return res.data.result ?? [];
  },

  /** Mastery по всем темам + недавние сессии (newest-first). Требует auth. */
  getProgress: async ({ signal }: { signal?: AbortSignal } = {}): Promise<TrainerProgress> => {
    const res = await apiClient.get<Envelope<TrainerProgress>>("/trainer/progress/", { signal });
    return res.data.result!;
  },
};

export const trainerTopicsQueryOptions = {
  baseKey: "trainer-topics",
  progressBaseKey: "trainer-progress",

  topicsKey: (filter: TrainerTopicsFilter = {}) =>
    [trainerTopicsQueryOptions.baseKey, filter.trackId ?? null, filter.direction ?? null] as const,
  progressKey: () => [trainerTopicsQueryOptions.progressBaseKey] as const,

  /** Список тем тренажёра с mastery вызывающего (опц. фильтр трек/направление). */
  topicsOptions: (filter: TrainerTopicsFilter = {}) =>
    queryOptions({
      queryKey: trainerTopicsQueryOptions.topicsKey(filter),
      queryFn: ({ signal }) => trainerTopicsApi.getTopics(filter, { signal }),
      staleTime: 60_000,
      // Смена направления не мигает skeleton'ом — старые карточки держатся до новых.
      placeholderData: keepPreviousData,
    }),

  /** Прогресс-сводка вызывающего (mastery + недавние сессии). Гейтить auth. */
  progressOptions: () =>
    queryOptions({
      queryKey: trainerTopicsQueryOptions.progressKey(),
      queryFn: ({ signal }) => trainerTopicsApi.getProgress({ signal }),
      staleTime: 30_000,
    }),
};
