import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";

/** Строка overview по одному тесту курса (#634). */
export type CourseQuizStatsRow = {
  quizId: string;
  title: string;
  attemptsCount: number;
  uniqueUsers: number;
  passRatePercent: number;
  avgScorePercent: number;
};

export type CourseQuizStatsOverview = {
  courseId: string;
  totalQuizzesWithAttempts: number;
  totalAttempts: number;
  overallPassRatePercent: number;
  overallAvgScorePercent: number;
  quizzes: CourseQuizStatsRow[];
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

export type CourseQuizStatsDetail = {
  quizId: string;
  title: string;
  attemptsCount: number;
  uniqueUsers: number;
  passRatePercent: number;
  avgScorePercent: number;
  scoreDistribution: QuizScoreBucketRow[];
  questions: QuizQuestionStatsRow[];
};

/** KPI прохождения курса студентами (#634, endpoint #3). */
export type CourseProgressStats = {
  engagedStudents: number;
  completedStudents: number;
  averageProgressPercent: number;
  materialViewsCompleted: number;
  issueSubmissionsCount: number;
  quizPassersCount: number;
};

export const courseStatisticsApi = {
  getQuizOverview: async (courseId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<CourseQuizStatsOverview>>(
      `/progress/courses/${courseId}/stats/quizzes/`,
      { signal },
    );
    return res.data;
  },

  getQuizDetail: async (courseId: string, quizId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<CourseQuizStatsDetail>>(
      `/progress/courses/${courseId}/stats/quizzes/${quizId}/`,
      { signal },
    );
    return res.data;
  },

  getProgress: async (courseId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<CourseProgressStats>>(
      `/progress/courses/${courseId}/stats/progress/`,
      { signal },
    );
    return res.data;
  },
};

const baseKey = "course-statistics";

export const courseQuizStatsOverviewOptions = (courseId: string) =>
  queryOptions({
    queryKey: [baseKey, "quizzes", courseId] as const,
    queryFn: ({ signal }) => courseStatisticsApi.getQuizOverview(courseId, signal),
    select: (data) => data.result,
    staleTime: 30_000,
  });

export const courseQuizStatsDetailOptions = (courseId: string, quizId: string | null) =>
  queryOptions({
    queryKey: [baseKey, "quizzes", courseId, "detail", quizId] as const,
    queryFn: ({ signal }) => courseStatisticsApi.getQuizDetail(courseId, quizId!, signal),
    enabled: !!quizId,
    select: (data) => data.result,
    staleTime: 30_000,
  });

export const courseProgressStatsOptions = (courseId: string) =>
  queryOptions({
    queryKey: [baseKey, "progress", courseId] as const,
    queryFn: ({ signal }) => courseStatisticsApi.getProgress(courseId, signal),
    select: (data) => data.result,
    staleTime: 30_000,
  });
