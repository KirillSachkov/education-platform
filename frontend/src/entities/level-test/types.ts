import type { QuizOptionDto, QuizQuestionType } from "@/entities/quiz";

/**
 * Типы level-test воронки — зеркалят C#-контракты ECS (`LevelTestStudentDto` из
 * `GetActiveLevelTest`) и ProgressService (`LevelTestAttemptResponses`).
 * Enum-литералы — UPPER_SNAKE_CASE, как члены enum на бэкенде. Issue #481.
 */

// Шкала уровней живёт в shared/types (#528) — нужна и quiz-entity (пороги
// конфига); здесь re-export ради стабильного публичного API слайса.
import type { DeveloperLevel } from "@/shared/types";

export {
  DEVELOPER_LEVEL_LABELS,
  DEVELOPER_LEVELS,
  type DeveloperLevel,
} from "@/shared/types";

export const AI_GRADING_STATUSES = ["NONE", "QUEUED", "GRADING", "READY", "FAILED"] as const;

export type AiGradingStatus = (typeof AI_GRADING_STATUSES)[number];

/** AI ещё считает развёрнутые ответы — UI показывает skeleton и поллит результат. */
export function isAiGradingPending(status: AiGradingStatus): boolean {
  return status === "QUEUED" || status === "GRADING";
}

/** Секция теста в студенческой проекции — только ключ и заголовок (без весов/порогов). */
export interface LevelTestSectionDto {
  key: string;
  title: string;
}

/**
 * Вопрос level-test'а: студенческая проекция квиза, расширенная `section` +
 * `difficulty` (ST-2 #477). Правильные ответы в JSON физически отсутствуют.
 */
export interface LevelTestQuestionDto {
  id: string;
  type: QuizQuestionType;
  /** Markdown — может содержать ```csharp блоки, рендерить через MarkdownContent. */
  text: string;
  section: string | null;
  difficulty: DeveloperLevel | null;
  options: QuizOptionDto[];
}

/** `GET /quizzes/level-test/active/` — активный тест уровня. 404 → теста нет. */
export interface LevelTestStudentDto {
  id: string;
  title: string;
  questions: LevelTestQuestionDto[];
  sections: LevelTestSectionDto[];
  totalQuestions: number;
}

/** Ответ на один вопрос в `POST /progress/level-test/attempts/`. */
export interface SubmitLevelTestAnswerItem {
  questionId: string;
  selectedOptionIds?: string[] | null;
  textAnswer?: string | null;
}

/**
 * Сабмит попытки. Auth-вызов — UserId из токена (`anonymousId` игнорируется);
 * анонимный — `anonymousId` (UUID из cookie `plu_anon_id`) обязателен.
 * Вопросы без ответа можно не присылать.
 */
export interface SubmitLevelTestAttemptRequest {
  quizId: string;
  anonymousId?: string | null;
  answers: SubmitLevelTestAnswerItem[];
}

/**
 * Lead-gated тизер результата — всё, что видит аноним. Секции и per-вопрос
 * разбор отсутствуют в JSON физически (server-side shaping отдельным типом).
 */
export interface LevelTestAttemptTeaserDto {
  attemptId: string;
  overallPercent: number;
  level: DeveloperLevel;
  totalQuestions: number;
  answeredCount: number;
  aiGradingStatus: AiGradingStatus;
}

export interface LevelTestSectionScoreDto {
  key: string;
  title: string;
  percent: number;
  level: DeveloperLevel;
  earnedPoints: number;
  maxPoints: number;
}

/**
 * Per-вопрос разбор: `isCorrect` — только для choice-вопросов (open_text → null);
 * `pendingAi`/`aiScore`/`aiFeedback` — состояние AI-грейдинга открытого ответа.
 *
 * Разбор правильных ответов (#561): `options`/`correctOptionIds`/`selectedOptionIds`
 * (choice) + `textAnswer`/`referenceAnswer` (EXACT/OPEN) + `explanation`. Для попыток,
 * созданных до деплоя, `options` будет пустым — разбор рендерится только при наличии
 * данных.
 */
export interface LevelTestQuestionResultDto {
  questionId: string;
  section: string | null;
  difficulty: DeveloperLevel | null;
  type: QuizQuestionType;
  isCorrect: boolean | null;
  pendingAi: boolean;
  aiScore: number | null;
  aiFeedback: string | null;
  earnedPoints: number;
  maxPoints: number;
  options: QuizOptionDto[];
  correctOptionIds: string[];
  selectedOptionIds: string[];
  textAnswer: string | null;
  referenceAnswer: string | null;
  explanation: string | null;
}

/** Полный результат попытки — владелец (после клейма) или auth-сабмит. */
export interface LevelTestAttemptResultDto {
  attemptId: string;
  quizId: string;
  overallPercent: number;
  level: DeveloperLevel;
  aiGradingStatus: AiGradingStatus;
  recommendedCourseId: string | null;
  sections: LevelTestSectionScoreDto[];
  questions: LevelTestQuestionResultDto[];
  weakestSectionKeys: string[];
}

/**
 * Submit и result-эндпоинты отдают ОДИН из двух типов: тизер (аноним /
 * неклеймленная попытка) или полный разбор. Различаем по физическому наличию
 * `sections` — у тизера поля нет в JSON вовсе.
 */
export type LevelTestAttemptResult = LevelTestAttemptTeaserDto | LevelTestAttemptResultDto;

export function isFullLevelTestResult(
  result: LevelTestAttemptResult,
): result is LevelTestAttemptResultDto {
  return Array.isArray((result as LevelTestAttemptResultDto).sections);
}

/** `POST /progress/level-test/attempts/claim/` body. */
export interface ClaimLevelTestAttemptsRequest {
  anonymousId: string;
}

/** Итог клейма: сколько попыток привязано + последняя (для редиректа). */
export interface ClaimLevelTestAttemptsResponse {
  claimedCount: number;
  latestAttemptId: string | null;
}
