import type { PaginationResponse } from "@/shared/api";
import type { IssueProgressStatus } from "@/shared/types/status";

export type ReviewSubmissionStatus = "PENDING" | "IN_REVIEW" | "APPROVED" | "CHANGES_REQUESTED";

/** AI verdict mirror — see entities/ai-review/types.ts. Duplicated as string union здесь
 *  чтобы избежать cross-slice импорта entities → entities (FSD запрещает). */
export type ReviewAiVerdict = "LOOKS_GOOD" | "MINOR_ISSUES" | "MAJOR_ISSUES" | "OFF_TOPIC";

export interface ReviewSubmissionItemDto {
  submissionId: string;
  courseId: string;
  projectId: string;
  issueId: string;
  studentId: string;
  submissionNo: number;
  /** #369: всего попыток в группе «студент + задание». Эта карточка = последняя попытка группы. */
  attemptsCount: number;
  payload: string;
  issueProgressStatus: IssueProgressStatus;
  reviewStatus: ReviewSubmissionStatus;
  reviewerId: string | null;
  submittedAt: string;
  reviewStartedAt: string | null;
  reviewedAt: string | null;
  feedback: string | null;
  studentName: string | null;
  studentUsername: string | null;
  studentAvatarId: string | null;
  /** Контакты студента для связи (enrich из AuthService) — #575. */
  studentEmail: string | null;
  studentTelegramUsername: string | null;
  reviewerName: string | null;
  reviewerUsername: string | null;
  reviewerAvatarId: string | null;
  courseTitle: string | null;
  projectTitle: string | null;
  issueTitle: string | null;

  /** Phase 8 (#15) AI denorm fields. */
  latestAiVerdict: ReviewAiVerdict | null;
  aiIterationsCount: number;
  lastAiIterationAt: string | null;
  aiReviewStatus: "QUEUED" | "RUNNING" | "READY" | "FAILED" | null;

  /** #383 — timestamp когда студент нажал «Позвать автора»; null = не звали.
   *  Питает бейдж «Нужна помощь автора» на карточке ревью. */
  authorHelpRequestedAt: string | null;

  /** #575 — текст «в чём нужна помощь», который указал студент. null = без пояснения. */
  authorHelpMessage: string | null;

  /** #713 — timestamp последнего вопроса, заданного студентом в PR-треде; null = вопросов не было.
   *  Питает бейдж «Новый вопрос от студента» на карточке ревью. */
  studentQuestionAt: string | null;
}

export type ReviewSubmissionListResponse = PaginationResponse<ReviewSubmissionItemDto>;

export interface ReviewSubmissionListFilters {
  page: number;
  pageSize: number;
  courseId?: string;
  submissionId?: string;
}

export interface ReviewSubmissionActionPayload {
  courseId: string;
  submissionId: string;
}

export interface ApproveReviewSubmissionPayload extends ReviewSubmissionActionPayload {
  feedback?: string | null;
}

export interface RequestReviewChangesPayload extends ReviewSubmissionActionPayload {
  feedback?: string | null;
}

export type ReopenReviewPayload = ReviewSubmissionActionPayload;

/** #668 — «Отказаться от проверки»: вернуть взятую (IN_REVIEW) сдачу в PENDING. */
export type CancelReviewPayload = ReviewSubmissionActionPayload;

/** #383 — ручная приёмка автором/админом («Отметить выполненным»): форс-аппрув
 *  попытки из любого статуса проверки. Опциональный feedback. */
export interface MarkIssueCompletePayload extends ReviewSubmissionActionPayload {
  feedback?: string | null;
}

export interface SetReviewIssueProgressStatusPayload {
  courseId: string;
  issueId: string;
  userId: string;
  targetStatus: IssueProgressStatus;
  feedback?: string | null;
}

export interface SubmissionAttemptHistoryItemDto {
  submissionId: string;
  attemptNumber: number;
  payload: string;
  reviewStatus: ReviewSubmissionStatus;
  submittedAt: string;
  reviewStartedAt: string | null;
  reviewedAt: string | null;
  feedback: string | null;

  /** #369: AI-денорм на каждой попытке — чтобы в раскрытой истории группы у любой
   *  попытки можно было раскрыть её собственное AI-ревью. */
  latestAiVerdict: ReviewAiVerdict | null;
  aiIterationsCount: number;
  lastAiIterationAt: string | null;
  aiReviewStatus: "QUEUED" | "RUNNING" | "READY" | "FAILED" | null;
}
