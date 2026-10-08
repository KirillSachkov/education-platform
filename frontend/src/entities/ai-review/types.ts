// AI review types — mirror backend AssignmentReviewService.Contracts.

export type AiReviewStatus = "QUEUED" | "RUNNING" | "READY" | "FAILED";

export type AiReviewIterationStatus = "PROCESSING" | "COMPLETED" | "FAILED";

export type AiReviewVerdict = "LOOKS_GOOD" | "MINOR_ISSUES" | "MAJOR_ISSUES" | "OFF_TOPIC";

export interface AiReviewIterationDto {
  id: string;
  iterationNumber: number;
  commitSha: string;
  status: AiReviewIterationStatus;
  verdict: AiReviewVerdict | null;
  summary: string;
  inlineCommentsCount: number;
  gitHubReviewId: number | null;
  modelUsed: string;
  inputTokens: number | null;
  outputTokens: number | null;
  startedAt: string;
  completedAt: string | null;
  failureReason: string | null;
}

/**
 * #713 — реплика/вопрос студента в его PR (обратный канал к AI-ревью), плюс ответ
 * автора (`answeredAt` / `answerBody`). Read-only проекция для UI-треда; порядок в
 * списке `AiReviewDetailDto.studentMessages` — по `createdAt` возрастанию.
 * Зеркалит backend `AssignmentReviewService.Contracts.Reviews.StudentPrMessageDto`.
 */
export interface StudentPrMessageDto {
  id: string;
  githubCommentId: number;
  inReplyToGithubId: number | null;
  authorGithubLogin: string;
  body: string;
  path: string | null;
  line: number | null;
  commentUrl: string;
  createdAt: string;
  answeredAt: string | null;
  answerBody: string | null;
}

export interface AiReviewDetailDto {
  id: string;
  submissionId: string;
  issueId: string;
  userId: string;
  provider: string;
  repoFullName: string;
  pullNumber: number;
  pullRequestUrl: string;
  status: AiReviewStatus;
  latestVerdict: AiReviewVerdict | null;
  iterationsCount: number;
  createdAt: string;
  updatedAt: string;
  iterations: AiReviewIterationDto[];
  /** #713 — обратный канал: реплики студента в PR-треде (см. StudentPrMessageDto). */
  studentMessages: StudentPrMessageDto[];
}

/**
 * #713 (1b) — результат ответа автора на реплику студента: id + html_url созданного
 * в GitHub коммента-ответа и момент, когда сообщение помечено отвеченным.
 * Зеркалит backend `ReplyToStudentMessageResponse`.
 */
export interface StudentMessageReplyResponseDto {
  messageId: string;
  answerGithubCommentId: number;
  answerHtmlUrl: string;
  answeredAt: string;
}

// POST /run-iteration/ is async (#357): endpoint publishes durable Wolverine
// command and returns immediately. The iteration runs in a worker scope, not
// tied to the HTTP request — frontend polls /by-submission/{id} (staleTime 10s)
// to see RUNNING → COMPLETED/FAILED transition. `status` is always "QUEUED" on
// successful enqueue; consumers shouldn't rely on iteration-level fields here.
export interface RequestRunIterationResponseDto {
  aiReviewId: string;
  status: "QUEUED";
}

export interface AiReviewControlResponseDto {
  aiReviewId: string;
  status: AiReviewStatus;
}

export interface CancelActiveAiReviewsResponseDto {
  cancelledCount: number;
}
