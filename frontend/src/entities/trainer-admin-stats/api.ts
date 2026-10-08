import { queryOptions } from "@tanstack/react-query";

import { apiClient, type Envelope } from "@/shared/api";

import type {
  AdminFeedbackRatingStats,
  AdminFunnelStats,
  AdminQuestionQualityStats,
  AdminStats,
  AdminTopicBankStats,
  AdminTrafficStats,
  AdminTrendStats,
} from "./types";

/** Допустимые окна для переключателя периода админ-дашборда тренажёра. */
export const TRAINER_ADMIN_STATS_RANGES = [7, 30, 90, 365] as const;
export type TrainerAdminStatsRange = (typeof TRAINER_ADMIN_STATS_RANGES)[number];

export const trainerAdminStatsApi = {
  /**
   * Композитный admin-снимок тренажёра за окно `days` (#614 D1, money #680): AI-расходы
   * (из лоджера `ai_usage`) + использование (из `training_sessions`).
   */
  getAdminStats: async (
    days: number,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<AdminStats> => {
    const res = await apiClient.get<Envelope<AdminStats>>(`/trainer/admin/stats/?days=${days}`, {
      signal,
    });
    return res.data.result!;
  },

  /** Трафик: DAU/WAU/MAU + retention + new-vs-returning + sessions-by-mode (#681 T3). */
  getTraffic: async (
    days: number,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<AdminTrafficStats> => {
    const res = await apiClient.get<Envelope<AdminTrafficStats>>(
      `/trainer/admin/stats/traffic/?days=${days}`,
      { signal },
    );
    return res.data.result!;
  },

  /** Воронка: completion (всего + по режиму) + drop-off по позиции + брошенные моки (#681 T3). */
  getFunnel: async (
    days: number,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<AdminFunnelStats> => {
    const res = await apiClient.get<Envelope<AdminFunnelStats>>(
      `/trainer/admin/stats/funnel/?days=${days}`,
      { signal },
    );
    return res.data.result!;
  },

  /** Качество вопросов: per-question %-верных / дискриминация / skip / время + OPEN_TEXT (#681 T4). */
  getQuestionQuality: async (
    days: number,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<AdminQuestionQualityStats> => {
    const res = await apiClient.get<Envelope<AdminQuestionQualityStats>>(
      `/trainer/admin/stats/question-quality/?days=${days}`,
      { signal },
    );
    return res.data.result!;
  },

  /** Темы и банки: per-topic mastery/% + per-bank coverage + калибровка сложности (#681 T5). */
  getTopicBankStats: async (
    days: number,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<AdminTopicBankStats> => {
    const res = await apiClient.get<Envelope<AdminTopicBankStats>>(
      `/trainer/admin/stats/topics/?days=${days}`,
      { signal },
    );
    return res.data.result!;
  },

  /** Owner-кривая динамики по дням: sessions/active/completed/cost₽/accuracy (#681 T6). */
  getTrends: async (
    days: number,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<AdminTrendStats> => {
    const res = await apiClient.get<Envelope<AdminTrendStats>>(
      `/trainer/admin/stats/trends/?days=${days}`,
      { signal },
    );
    return res.data.result!;
  },

  /** Оценки AI-разбора 👍/👎 по вопросам, «худшие сверху» (наибольший down-rate) (#691 t7). */
  getFeedbackRatings: async (
    days: number,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<AdminFeedbackRatingStats> => {
    const res = await apiClient.get<Envelope<AdminFeedbackRatingStats>>(
      `/trainer/admin/stats/feedback-ratings/?days=${days}`,
      { signal },
    );
    return res.data.result!;
  },
};

export const adminStatsQueryOptions = (days: number) =>
  queryOptions({
    queryKey: ["trainer", "admin", "stats", days] as const,
    queryFn: ({ signal }) => trainerAdminStatsApi.getAdminStats(days, { signal }),
  });

export const adminTrafficStatsQueryOptions = (days: number) =>
  queryOptions({
    queryKey: ["trainer", "admin", "stats", "traffic", days] as const,
    queryFn: ({ signal }) => trainerAdminStatsApi.getTraffic(days, { signal }),
  });

export const adminFunnelStatsQueryOptions = (days: number) =>
  queryOptions({
    queryKey: ["trainer", "admin", "stats", "funnel", days] as const,
    queryFn: ({ signal }) => trainerAdminStatsApi.getFunnel(days, { signal }),
  });

export const adminQuestionQualityQueryOptions = (days: number) =>
  queryOptions({
    queryKey: ["trainer", "admin", "stats", "question-quality", days] as const,
    queryFn: ({ signal }) => trainerAdminStatsApi.getQuestionQuality(days, { signal }),
  });

export const adminTopicBankStatsQueryOptions = (days: number) =>
  queryOptions({
    queryKey: ["trainer", "admin", "stats", "topics", days] as const,
    queryFn: ({ signal }) => trainerAdminStatsApi.getTopicBankStats(days, { signal }),
  });

export const adminTrendStatsQueryOptions = (days: number) =>
  queryOptions({
    queryKey: ["trainer", "admin", "stats", "trends", days] as const,
    queryFn: ({ signal }) => trainerAdminStatsApi.getTrends(days, { signal }),
  });

export const adminFeedbackRatingStatsQueryOptions = (days: number) =>
  queryOptions({
    queryKey: ["trainer", "admin", "stats", "feedback-ratings", days] as const,
    queryFn: ({ signal }) => trainerAdminStatsApi.getFeedbackRatings(days, { signal }),
  });
