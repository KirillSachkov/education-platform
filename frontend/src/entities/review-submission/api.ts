import { apiClient, type Envelope, type PaginationResponse } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  ApproveReviewSubmissionPayload,
  CancelReviewPayload,
  MarkIssueCompletePayload,
  ReopenReviewPayload,
  RequestReviewChangesPayload,
  ReviewSubmissionActionPayload,
  ReviewSubmissionItemDto,
  ReviewSubmissionListFilters,
  SetReviewIssueProgressStatusPayload,
  SubmissionAttemptHistoryItemDto,
} from "./types";

export const reviewSubmissionsApi = {
  getPending: async (
    filters: ReviewSubmissionListFilters,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<Envelope<PaginationResponse<ReviewSubmissionItemDto>>> => {
    const res = await apiClient.get<Envelope<PaginationResponse<ReviewSubmissionItemDto>>>(
      "/progress/reviews/issues/pending",
      {
        params: filters,
        signal,
      },
    );

    return res.data;
  },

  getInReview: async (
    filters: ReviewSubmissionListFilters,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<Envelope<PaginationResponse<ReviewSubmissionItemDto>>> => {
    const res = await apiClient.get<Envelope<PaginationResponse<ReviewSubmissionItemDto>>>(
      "/progress/reviews/issues/in-review",
      {
        params: filters,
        signal,
      },
    );

    return res.data;
  },

  getReviewed: async (
    filters: ReviewSubmissionListFilters,
    { signal }: { signal?: AbortSignal } = {},
  ): Promise<Envelope<PaginationResponse<ReviewSubmissionItemDto>>> => {
    const res = await apiClient.get<Envelope<PaginationResponse<ReviewSubmissionItemDto>>>(
      "/progress/reviews/issues/reviewed",
      {
        params: filters,
        signal,
      },
    );

    return res.data;
  },

  startReview: async ({ courseId, submissionId }: ReviewSubmissionActionPayload) => {
    const res = await apiClient.post<Envelope<null>>(
      `/progress/courses/${courseId}/reviews/issues/${submissionId}/start-review`,
    );

    return res.data;
  },

  approve: async ({ courseId, submissionId, feedback }: ApproveReviewSubmissionPayload) => {
    const res = await apiClient.post<Envelope<null>>(
      `/progress/courses/${courseId}/reviews/issues/${submissionId}/approve`,
      { feedback: feedback ?? null },
    );

    return res.data;
  },

  requestChanges: async ({ courseId, submissionId, feedback }: RequestReviewChangesPayload) => {
    const res = await apiClient.post<Envelope<null>>(
      `/progress/courses/${courseId}/reviews/issues/${submissionId}/request-changes`,
      { feedback: feedback ?? null },
    );

    return res.data;
  },

  reopen: async ({ courseId, submissionId }: ReopenReviewPayload) => {
    const res = await apiClient.post<Envelope<null>>(
      `/progress/courses/${courseId}/reviews/issues/${submissionId}/reopen`,
    );

    return res.data;
  },

  cancelReview: async ({ courseId, submissionId }: CancelReviewPayload) => {
    const res = await apiClient.post<Envelope<null>>(
      `/progress/courses/${courseId}/reviews/issues/${submissionId}/cancel-review`,
    );

    return res.data;
  },

  markComplete: async ({ courseId, submissionId, feedback }: MarkIssueCompletePayload) => {
    const res = await apiClient.post<Envelope<null>>(
      `/progress/courses/${courseId}/reviews/issues/${submissionId}/mark-complete`,
      { feedback: feedback ?? null },
    );

    return res.data;
  },

  setIssueProgressStatus: async ({
    courseId,
    issueId,
    userId,
    targetStatus,
    feedback,
  }: SetReviewIssueProgressStatusPayload) => {
    const res = await apiClient.put<Envelope<void>>(
      `/progress/courses/${courseId}/issues/${issueId}/progress-status-for-user/`,
      { userId, targetStatus, feedback: feedback ?? null },
    );

    return res.data;
  },

  getAttemptHistory: async (submissionId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<SubmissionAttemptHistoryItemDto[]>>(
      `/progress/reviews/submissions/${submissionId}/history`,
      { signal },
    );

    return res.data;
  },
};

export const reviewSubmissionsQueryOptions = {
  baseKey: "review-submissions",

  pendingList: (filters: ReviewSubmissionListFilters) =>
    queryOptions({
      queryKey: [reviewSubmissionsQueryOptions.baseKey, "pending", filters],
      queryFn: ({ signal }) => reviewSubmissionsApi.getPending(filters, { signal }),
      select: (data) => data.result,
    }),

  // In-flight AI-review feed (#363) — polled every 15s so the author sees
  // submissions appear/leave while the AI iterates without manual refresh.
  inReviewList: (filters: ReviewSubmissionListFilters) =>
    queryOptions({
      queryKey: [reviewSubmissionsQueryOptions.baseKey, "in-review", filters],
      queryFn: ({ signal }) => reviewSubmissionsApi.getInReview(filters, { signal }),
      select: (data) => data.result,
      refetchInterval: 15_000,
    }),

  reviewedList: (filters: ReviewSubmissionListFilters) =>
    queryOptions({
      queryKey: [reviewSubmissionsQueryOptions.baseKey, "reviewed", filters],
      queryFn: ({ signal }) => reviewSubmissionsApi.getReviewed(filters, { signal }),
      select: (data) => data.result,
    }),

  attemptHistory: (submissionId: string) =>
    queryOptions({
      queryKey: [reviewSubmissionsQueryOptions.baseKey, "attempt-history", submissionId],
      queryFn: ({ signal }) => reviewSubmissionsApi.getAttemptHistory(submissionId, { signal }),
      select: (data) => data.result ?? [],
    }),
};
