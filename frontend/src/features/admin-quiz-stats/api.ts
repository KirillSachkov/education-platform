import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";

/**
 * Строка overview по одному тесту (#556, AC5). `courseId`/`courseTitle` — null
 * для standalone-теста; фронт группирует такие в «Без курса».
 */
export type QuizAdminOverviewRow = {
  quizId: string;
  title: string;
  courseId: string | null;
  courseTitle: string | null;
  attemptsCount: number;
  uniqueUsers: number;
  passRatePercent: number;
  avgScorePercent: number;
};

export type QuizAdminOverview = {
  totalQuizzes: number;
  totalAttempts: number;
  overallPassRatePercent: number;
  overallAvgScorePercent: number;
  quizzes: QuizAdminOverviewRow[];
};

export type QuizScoreBucketRow = {
  bucket: string;
  count: number;
};

export type QuizQuestionStatsRow = {
  questionId: string;
  text: string;
  type: string;
  answeredCount: number;
  correctCount: number;
  correctRatePercent: number;
};

export type QuizAdminStats = {
  quizId: string;
  title: string;
  attemptsCount: number;
  uniqueUsers: number;
  passRatePercent: number;
  avgScorePercent: number;
  scoreDistribution: QuizScoreBucketRow[];
  questions: QuizQuestionStatsRow[];
};

export const adminQuizStatsApi = {
  getOverview: async (signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<QuizAdminOverview>>(
      "/progress/quizzes/admin/overview/",
      { signal },
    );
    return res.data;
  },

  getStats: async (quizId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<QuizAdminStats>>(
      `/progress/quizzes/admin/${quizId}/stats/`,
      { signal },
    );
    return res.data;
  },
};

export const adminQuizStatsQueryOptions = {
  baseKey: "admin-quiz-stats",

  getOverviewOptions: () =>
    queryOptions({
      queryKey: [adminQuizStatsQueryOptions.baseKey, "overview"] as const,
      queryFn: ({ signal }) => adminQuizStatsApi.getOverview(signal),
      select: (data) => data.result,
      staleTime: 30_000,
    }),

  getStatsOptions: (quizId: string | null) =>
    queryOptions({
      queryKey: [adminQuizStatsQueryOptions.baseKey, "stats", quizId] as const,
      queryFn: ({ signal }) => adminQuizStatsApi.getStats(quizId!, signal),
      enabled: !!quizId,
      select: (data) => data.result,
      staleTime: 30_000,
    }),
};
