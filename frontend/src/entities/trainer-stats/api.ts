import { queryOptions } from "@tanstack/react-query";

import { apiClient, type Envelope } from "@/shared/api";

import type {
  TrainerActivity,
  TrainerMockTrend,
  TrainerStatsSummary,
  TrainerStrengths,
  TrainerTrends,
} from "./types";

export const trainerStatsApi = {
  /** Дневная активность за `days` (сессии/вопросы/правильные) + streak'и. */
  getActivity: async (
    days: number,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<TrainerActivity> => {
    const res = await apiClient.get<Envelope<TrainerActivity>>(
      `/trainer/stats/activity/?days=${days}`,
      { signal },
    );
    return res.data.result!;
  },

  /** Сводка-агрегаты: точность, покрытие, сложность, SRS. */
  getSummary: async ({ signal }: { signal?: AbortSignal } = {}): Promise<TrainerStatsSummary> => {
    const res = await apiClient.get<Envelope<TrainerStatsSummary>>("/trainer/stats/summary/", {
      signal,
    });
    return res.data.result!;
  },

  /** Тренд мок-собесов + слабые/сильные темы. */
  getMockTrend: async ({ signal }: { signal?: AbortSignal } = {}): Promise<TrainerMockTrend> => {
    const res = await apiClient.get<Envelope<TrainerMockTrend>>("/trainer/stats/mock-trend/", {
      signal,
    });
    return res.data.result!;
  },

  /** Сильные/слабые стороны по измеренному mastery + объём выборки + де-шумленный мок-сигнал. */
  getStrengths: async ({ signal }: { signal?: AbortSignal } = {}): Promise<TrainerStrengths> => {
    const res = await apiClient.get<Envelope<TrainerStrengths>>("/trainer/stats/strengths/", {
      signal,
    });
    return res.data.result!;
  },

  /** Тренд mastery «vs месяц назад»: per-topic снимок сегодня vs ~30 дней назад + общая дельта. */
  getTrends: async ({ signal }: { signal?: AbortSignal } = {}): Promise<TrainerTrends> => {
    const res = await apiClient.get<Envelope<TrainerTrends>>("/trainer/stats/trends/", {
      signal,
    });
    return res.data.result!;
  },
};

/** Допустимые окна для переключателя периода активности. */
export const TRAINER_ACTIVITY_RANGES = [30, 90] as const;
export type TrainerActivityRange = (typeof TRAINER_ACTIVITY_RANGES)[number];

export const trainerActivityQueryOptions = (days: TrainerActivityRange) =>
  queryOptions({
    queryKey: ["trainer", "stats", "activity", days] as const,
    queryFn: ({ signal }) => trainerStatsApi.getActivity(days, { signal }),
  });

export const trainerStatsSummaryQueryOptions = () =>
  queryOptions({
    queryKey: ["trainer", "stats", "summary"] as const,
    queryFn: ({ signal }) => trainerStatsApi.getSummary({ signal }),
  });

export const trainerMockTrendQueryOptions = () =>
  queryOptions({
    queryKey: ["trainer", "stats", "mock-trend"] as const,
    queryFn: ({ signal }) => trainerStatsApi.getMockTrend({ signal }),
  });

export const trainerStrengthsQueryOptions = () =>
  queryOptions({
    queryKey: ["trainer", "stats", "strengths"] as const,
    queryFn: ({ signal }) => trainerStatsApi.getStrengths({ signal }),
  });

export const trainerTrendsQueryOptions = () =>
  queryOptions({
    queryKey: ["trainer", "stats", "trends"] as const,
    queryFn: ({ signal }) => trainerStatsApi.getTrends({ signal }),
  });
