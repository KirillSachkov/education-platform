import { apiClient, type Envelope } from "@/shared/api";
import { keepPreviousData, queryOptions } from "@tanstack/react-query";
import type {
  CourseLearningStateDto,
  CoursePublicStatsDto,
  IssueSubmissionHistoryDto,
  MyQuizAttemptsSummaryDto,
  SubmitIssueResponseDto,
} from "./types";

export const courseProgressQueryOptions = {
  baseKey: "course-learning-state",
  issueHistoryKey: "issue-submission-history",
  publicStatsKey: "course-public-stats",
  /** Сводка результатов тестов пользователя — общий ключ для query и инвалидации
   *  после сабмита попытки (#578). */
  mySummaryKey: ["progress", "quizzes", "my-summary"] as const,
};

export const courseProgressApi = {
  getCourseLearningState: async (courseId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<CourseLearningStateDto | null>>(
      `/progress/courses/${courseId}/learning-state/`,
      { signal },
    );
    return res.data;
  },

  getIssueSubmissionHistory: async (
    courseId: string,
    issueId: string,
    { signal }: { signal?: AbortSignal } = {},
  ) => {
    const res = await apiClient.get<Envelope<IssueSubmissionHistoryDto>>(
      `/progress/courses/${courseId}/issues/${issueId}/history/`,
      { signal },
    );
    return res.data;
  },

  markMaterialViewed: async ({ materialId }: { materialId: string }) => {
    const res = await apiClient.post<Envelope<void>>(`/progress/materials/${materialId}/view/`);
    return res.data;
  },

  unmarkMaterialViewed: async ({ materialId }: { materialId: string }) => {
    const res = await apiClient.delete<Envelope<void>>(`/progress/materials/${materialId}/view/`);
    return res.data;
  },

  recordCoursePosition: async ({
    courseId,
    entityType,
    entityId,
  }: {
    courseId: string;
    entityType: "MATERIAL" | "ISSUE";
    entityId: string;
  }) => {
    const res = await apiClient.post<Envelope<void>>(`/progress/courses/${courseId}/position/`, {
      entityType,
      entityId,
    });
    return res.data;
  },

  startIssueWork: async ({
    courseId,
    projectId,
    issueId,
  }: {
    courseId: string;
    projectId: string;
    issueId: string;
  }) => {
    const res = await apiClient.post<Envelope<void>>(
      `/progress/courses/${courseId}/projects/${projectId}/issues/${issueId}/start/`,
    );
    return res.data;
  },

  submitIssue: async ({
    courseId,
    issueId,
    submissionUrl,
    contentPayload,
  }: {
    courseId: string;
    issueId: string;
    submissionUrl?: string;
    contentPayload?: string;
  }) => {
    const res = await apiClient.post<Envelope<SubmitIssueResponseDto>>(
      `/progress/courses/${courseId}/issues/${issueId}/submit/`,
      { submissionUrl, contentPayload },
    );
    return res.data;
  },
};

export const courseLearningStateQueryOptions = (courseId: string) =>
  queryOptions({
    queryKey: [courseProgressQueryOptions.baseKey, courseId],
    queryFn: ({ signal }) => courseProgressApi.getCourseLearningState(courseId, { signal }),
    select: (data) => data.result ?? null,
    // Learning state is invalidated explicitly on view/submit/start mutations,
    // so we can hold the cache longer between navigations within a course.
    staleTime: 60_000,
    enabled: !!courseId,
  });

export const issueSubmissionHistoryQueryOptions = (courseId: string, issueId: string) =>
  queryOptions({
    queryKey: [courseProgressQueryOptions.issueHistoryKey, courseId, issueId],
    queryFn: ({ signal }) =>
      courseProgressApi.getIssueSubmissionHistory(courseId, issueId, { signal }),
    select: (data) => data.result,
    // Хвостовая история по предыдущей задаче остаётся видимой пока грузится новая
    // — для перехода между двумя issue-страницами без spinner-flash.
    placeholderData: keepPreviousData,
    enabled: !!courseId && !!issueId,
  });

/**
 * Сводка результатов тестов текущего пользователя по всем курсам. На курсовой
 * вкладке «Тесты» фильтруется до тестов курса по `quizId` (#578). Не course-scoped
 * — один кэш на пользователя.
 */
export const myQuizAttemptsSummaryQueryOptions = () =>
  queryOptions({
    queryKey: courseProgressQueryOptions.mySummaryKey,
    queryFn: async ({ signal }) => {
      const res = await apiClient.get<Envelope<MyQuizAttemptsSummaryDto>>(
        "/progress/quizzes/attempts/my-summary/",
        { signal },
      );
      return res.data;
    },
    select: (data) => data.result!,
  });

export const coursePublicStatsQueryOptions = (courseId: string) =>
  queryOptions({
    queryKey: [courseProgressQueryOptions.publicStatsKey, courseId],
    queryFn: async ({ signal }) => {
      const res = await apiClient.get<Envelope<CoursePublicStatsDto>>(
        `/progress/courses/${courseId}/public-stats/`,
        { signal },
      );
      return res.data;
    },
    select: (data) => data.result!,
    staleTime: 5 * 60 * 1000,
    enabled: !!courseId,
  });
