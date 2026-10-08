import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";
import type {
  AiReviewControlResponseDto,
  AiReviewDetailDto,
  CancelActiveAiReviewsResponseDto,
  RequestRunIterationResponseDto,
  StudentMessageReplyResponseDto,
} from "./types";

export const aiReviewQueryKeys = {
  baseKey: "ai-review",
  bySubmission: (submissionId: string) =>
    [aiReviewQueryKeys.baseKey, "by-submission", submissionId] as const,
};

export const aiReviewApi = {
  getBySubmission: async (submissionId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<AiReviewDetailDto>>(
      `/assignment-review/reviews/by-submission/${submissionId}/`,
      { signal },
    );
    return res.data;
  },

  // Async-enqueue (#357): backend returns immediately with status="QUEUED" and
  // runs the iteration in a Wolverine worker. UI polls bySubmission to see the
  // result; do NOT consume iteration-level fields from this response.
  runIteration: async (reviewId: string) => {
    const res = await apiClient.post<Envelope<RequestRunIterationResponseDto>>(
      `/assignment-review/reviews/${reviewId}/run-iteration/`,
    );
    return res.data;
  },

  // Студенческая доработка после AI-approve с MINOR_ISSUES (#725): владелец решения
  // запускает повторную проверку своего PR. Зачёт не теряется (задача остаётся
  // COMPLETED) — новая итерация лишь обновляет вердикт. Async, как runIteration.
  studentRerun: async (reviewId: string) => {
    const res = await apiClient.post<Envelope<RequestRunIterationResponseDto>>(
      `/assignment-review/reviews/${reviewId}/student-rerun/`,
    );
    return res.data;
  },

  restart: async (reviewId: string) => {
    const res = await apiClient.post<Envelope<AiReviewControlResponseDto>>(
      `/assignment-review/reviews/${reviewId}/restart/`,
    );
    return res.data;
  },

  cancel: async (reviewId: string) => {
    const res = await apiClient.post<Envelope<AiReviewControlResponseDto>>(
      `/assignment-review/reviews/${reviewId}/cancel/`,
    );
    return res.data;
  },

  cancelActive: async () => {
    const res = await apiClient.post<Envelope<CancelActiveAiReviewsResponseDto>>(
      `/assignment-review/admin/reviews/active/cancel/`,
    );
    return res.data;
  },

  finalizeSubmission: async (submissionId: string) => {
    const res = await apiClient.post<Envelope<void>>(
      `/progress/submissions/${submissionId}/finalize/`,
    );
    return res.data;
  },

  submitIterationFeedback: async ({
    iterationId,
    isHelpful,
    comment,
  }: {
    iterationId: string;
    isHelpful: boolean;
    comment?: string | null;
  }) => {
    const res = await apiClient.post<Envelope<void>>(
      `/assignment-review/iterations/${iterationId}/feedback/`,
      { isHelpful, comment: comment ?? null },
    );
    return res.data;
  },

  // #713 (1b) — автор курса отвечает студенту на его реплику в PR. Ответ постится
  // в тот же тред GitHub; после успеха у сообщения появляются answeredAt/answerBody
  // (видны при рефетче detail через aiReviewBySubmissionQueryOptions).
  replyToStudentMessage: async ({ messageId, body }: { messageId: string; body: string }) => {
    const res = await apiClient.post<Envelope<StudentMessageReplyResponseDto>>(
      `/assignment-review/student-messages/${messageId}/reply/`,
      { body },
    );
    return res.data;
  },
};

/**
 * Returns AiReview detail for a given submission.
 * Throws (caller catches via TanStack `error`) if no AiReview exists yet —
 * UI distinguishes "not yet" vs "failed" via `error.code === 'review.not_found'`.
 */
export const aiReviewBySubmissionQueryOptions = (submissionId: string) =>
  queryOptions({
    queryKey: aiReviewQueryKeys.bySubmission(submissionId),
    queryFn: ({ signal }) => aiReviewApi.getBySubmission(submissionId, { signal }),
    select: (data) => data.result!,
    retry: false,
    staleTime: 10_000,
  });
