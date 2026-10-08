export interface CourseStudentDto {
  enrollmentId: string;
  userId: string;
  name: string | null;
  username: string | null;
  email: string | null;
  avatarId: string | null;
  enrolledAt: string;
}

export interface GetCourseStudentsRequest {
  page: number;
  pageSize: number;
  search?: string;
}

/** Материал, который студент явно отметил «Изучено» (is_completed=true). */
export interface StudentCompletedMaterialDto {
  materialId: string;
  completedAt: string;
}

/**
 * Статус задания студента в курсе.
 * `status` — статус issue_progress (NOT_STARTED / IN_PROGRESS / UNDER_REVIEW /
 * COMPLETED / REQUESTED_CHANGES); `reviewStatus` — review-статус последней попытки
 * (PENDING / IN_REVIEW / APPROVED / CHANGES_REQUESTED), null если попыток не было.
 */
export interface StudentIssueProgressDto {
  issueId: string;
  projectId: string;
  status: string;
  reviewStatus: string | null;
  submittedAt: string | null;
  attemptsCount: number;
}

/**
 * Прогресс-факты конкретного студента по курсу для staff-просмотра.
 * Только данные ProgressService — структуру курса фронт уже знает (course-builder
 * DTO) и накладывает статусы сверху. Если у студента нет прогресс-якоря
 * (grant-holder, ещё не начинал) → `enrollmentStarted=false`, массивы пустые.
 */
export interface StudentCourseProgressDto {
  courseId: string;
  userId: string;
  enrollmentStarted: boolean;
  enrolledAt: string | null;
  completedMaterials: StudentCompletedMaterialDto[];
  issues: StudentIssueProgressDto[];
}
