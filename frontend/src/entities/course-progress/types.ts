import type {
  IssueProgressStatus,
  MaterialProgressStatus,
  SubmissionReviewStatus,
} from "@/shared/types";

export type { IssueProgressStatus, MaterialProgressStatus, SubmissionReviewStatus };

export interface CoursePublicStatsDto {
  enrolledStudentsCount: number;
  completedStudentsCount: number;
  averageProgressPercent: number;
}

export interface CourseLearningSummaryDto {
  totalModules: number;
  materialsTotal: number;
  materialsViewed: number;
  modulesCompleted: number;
  issuesTotal: number;
  issuesCompleted: number;
  totalItems: number;
  completedItems: number;
  progressPercent: number;
}

export interface MaterialLearningItemDto {
  materialId: string;
  status: MaterialProgressStatus;
  viewedAt: string | null;
}

export interface IssueLatestSubmissionDto {
  submissionId: string;
  payload: string;
  reviewStatus: SubmissionReviewStatus;
  submittedAt: string;
  reviewStartedAt: string | null;
  reviewedAt: string | null;
  feedback: string | null;
}

export interface IssueSubmissionHistoryItemDto {
  submissionId: string;
  attemptNumber: number;
  payload: string;
  reviewStatus: SubmissionReviewStatus;
  submittedAt: string;
  reviewStartedAt: string | null;
  reviewedAt: string | null;
  feedback: string | null;
  /** #383 — timestamp когда студент позвал автора; null = не звал. */
  authorHelpRequestedAt: string | null;
}

export interface IssueSubmissionHistoryDto {
  issueId: string;
  currentStatus: IssueProgressStatus;
  startedAt: string | null;
  completedAt: string | null;
  attempts: IssueSubmissionHistoryItemDto[];
}

export interface IssueLearningItemDto {
  issueId: string;
  projectId: string;
  status: IssueProgressStatus;
  startedAt: string | null;
  completedAt: string | null;
  latestSubmission: IssueLatestSubmissionDto | null;
}

export interface CoursePositionDto {
  /** "MATERIAL" | "ISSUE" — тип последнего открытого ресурса. */
  entityType: "MATERIAL" | "ISSUE";
  entityId: string;
  openedAt: string;
}

export interface CourseLearningStateDto {
  enrollmentId: string;
  courseId: string;
  summary: CourseLearningSummaryDto;
  issues: IssueLearningItemDto[];
  /** Unified Material progress items (replaces lessons + articles). */
  materials: MaterialLearningItemDto[];
  enrolledAt: string;
  /**
   * Последняя точка ученика в курсе — материал или задание, которое он открывал последним.
   * Используется для CTA «Продолжить курс» и подсветки «Продолжить» в программе.
   * null до первого открытия любого материала/задания этого курса.
   */
  lastPosition: CoursePositionDto | null;
  /**
   * Id квизов курса с passed-попыткой (distinct passed quiz_attempts ∩ blueprint.QuizIds,
   * ST-16 #495) — питает галочку «пройден» на quiz-строках программы. Optional —
   * отсутствует в ~минутном окне деплоя, пока бэкенд старый.
   */
  passedQuizIds?: string[];
}

export interface SubmitIssueResponseDto {
  submissionId: string;
}

/** Результат одного пройденного теста текущего пользователя (#556 → #578). */
export interface MyQuizAttemptsSummaryItem {
  quizId: string;
  title: string;
  /** Представительный курс теста; `null` для standalone-квиза. */
  courseId: string | null;
  attemptsCount: number;
  bestScorePercent: number;
  lastScorePercent: number;
  lastSubmittedAt: string;
  /** Прошёл ли тест хотя бы в одной попытке. */
  passed: boolean;
}

/**
 * Сводка всех тестов, которые проходил текущий пользователь. Питает результаты
 * на курсовой вкладке «Тесты» (join по `quizId === curriculum item.id`, #578).
 */
export interface MyQuizAttemptsSummaryDto {
  items: MyQuizAttemptsSummaryItem[];
  totalQuizzesTaken: number;
  passedCount: number;
  avgBestScorePercent: number;
}
