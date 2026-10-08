/**
 * Типы квизов — зеркалят C#-контракты ECS (`EducationContentService.Contracts.Quizzes`)
 * и ProgressService (`SubmitQuizAttemptRequest` / `QuizAttemptResultResponse`).
 * Enum-литералы — UPPER_SNAKE_CASE, как члены enum на бэкенде. Issue #471.
 */

import type { DeveloperLevel } from "@/shared/types";

export const QUIZ_QUESTION_TYPES = [
  "SINGLE_CHOICE",
  "MULTI_CHOICE",
  "OPEN_TEXT",
  "EXACT_TEXT",
] as const;

export type QuizQuestionType = (typeof QUIZ_QUESTION_TYPES)[number];

/** Назначение квиза (`Quiz.Purpose`, immutable после создания). */
export const QUIZ_PURPOSES = ["MATERIAL_CHECK", "LEVEL_TEST"] as const;

export type QuizPurpose = (typeof QUIZ_PURPOSES)[number];

/** Собственный уровень доступа квиза (зеркало `Material.AccessType`, ST-11 #490). */
export type QuizAccessType = "PUBLIC" | "REGISTERED" | "ENROLLED";

export type QuizStatus = "DRAFT" | "PUBLISHED" | "ARCHIVED";

/**
 * Сложность вопроса — зеркало C#-enum `QuestionDifficulty` (3 значения).
 * НЕ путать со шкалой уровней `DeveloperLevel` (6 значений, #528) из
 * `@/shared/types` — пороги level-test-конфига типизированы ею.
 */
export const QUIZ_DIFFICULTY_LEVELS = ["JUNIOR", "MIDDLE", "SENIOR"] as const;

export type QuizDifficultyLevel = (typeof QUIZ_DIFFICULTY_LEVELS)[number];

/** Серверные инварианты (`Quiz` / `QuizQuestion` / `QuizOption` domain constants). */
export const QUIZ_TITLE_MAX_LENGTH = 200;
export const QUIZ_QUESTION_TEXT_MAX_LENGTH = 2000;
export const QUIZ_OPTION_TEXT_MAX_LENGTH = 500;
export const QUIZ_REFERENCE_ANSWER_MAX_LENGTH = 4000;
export const QUIZ_EXPLANATION_MAX_LENGTH = 2000;
export const QUIZ_SECTION_KEY_MAX_LENGTH = 100;
export const QUIZ_MIN_OPTIONS = 2;
export const QUIZ_MAX_OPTIONS = 10;
export const QUIZ_MAX_QUESTIONS = 50;
export const QUIZ_DEFAULT_PASSING_SCORE_PERCENT = 70;

/**
 * Бэкенд-код 409: автор отредактировал тест, пока студент его проходил, и id вопросов
 * больше не совпадают с актуальным ключом. Попытка не сохраняется — нужно перезагрузить
 * страницу и пройти заново (#556).
 */
export const QUIZ_CHANGED_RELOAD_CODE = "quiz.changed.reload";

/** Вариант ответа (без признака правильности — он только в авторской проекции). */
export interface QuizOptionDto {
  id: string;
  text: string;
}

/** Студенческая проекция вопроса — без correctOptionIds/referenceAnswer (их нет в JSON физически). */
export interface QuizQuestionStudentDto {
  id: string;
  type: QuizQuestionType;
  text: string;
  /** Ключ секции level-test'а; `null` для MATERIAL_CHECK-квизов. */
  section: string | null;
  difficulty: QuizDifficultyLevel | null;
  options: QuizOptionDto[];
}

/**
 * Студенческая проекция опубликованного квиза (`GET /materials/{id}/quiz/`,
 * `GET /quizzes/{id}/student/`). `materialId` — материал-контекст запроса;
 * `null` для standalone-чтения (#490).
 */
export interface QuizStudentDto {
  id: string;
  materialId: string | null;
  title: string;
  passingScorePercent: number;
  questions: QuizQuestionStudentDto[];
}

/** Порог уровня level-test'а (минимальный общий процент для присвоения уровня). */
export interface LevelThresholdConfig {
  level: DeveloperLevel;
  minPercent: number;
}

/** Секция level-test'а в авторской конфигурации (key матчится с `section` вопросов). */
export interface LevelTestSectionConfig {
  key: string;
  title: string;
  weight: number;
  recommendedCourseId: string | null;
}

/**
 * Конфигурация level-test квиза — авторская проекция и тело запроса совпадают
 * (`LevelTestConfigDto` / `LevelTestConfigRequest` на бэкенде, replace целиком).
 */
export interface LevelTestConfigDto {
  levelThresholds: LevelThresholdConfig[];
  sections: LevelTestSectionConfig[];
  fallbackCourseId: string | null;
}

/** Полная авторская проекция вопроса — включая правильные ответы и эталон. */
export interface QuizQuestionAuthorDto {
  id: string;
  type: QuizQuestionType;
  text: string;
  /** Ключ секции level-test'а (kebab-case); `null` — вне секций / MATERIAL_CHECK. */
  section: string | null;
  difficulty: QuizDifficultyLevel | null;
  options: QuizOptionDto[];
  correctOptionIds: string[];
  referenceAnswer: string | null;
  /** Пояснение к вопросу — показывается студенту в разборе после ответа (#561). */
  explanation: string | null;
}

/**
 * Полная авторская проекция квиза (`GET /quizzes/{id}/`, `GET /quizzes/by-material/{id}/`,
 * `GET /quizzes/level-test/mine/`). Квиз — standalone-сущность (#489): привязка живёт
 * на стороне материалов (`materials.quiz_id`), поэтому `materialId` в проекции нет.
 */
export interface QuizAuthorDto {
  id: string;
  authorId: string;
  title: string;
  status: QuizStatus;
  accessType: QuizAccessType;
  purpose: QuizPurpose;
  passingScorePercent: number;
  questions: QuizQuestionAuthorDto[];
  levelTestConfig: LevelTestConfigDto | null;
  createdAt: string;
  updatedAt: string;
}

/**
 * Карточка квиза в авторской библиотеке (`GET /quizzes/mine/`, ST-12 #492).
 * Содержит и LEVEL_TEST — библиотека на фронте фильтрует его сама (у него
 * своя страница `/author/level-test`). Counts — usage-метрики: сколько
 * материалов ссылаются на квиз и в скольких курсах он размещён.
 */
export interface MyQuizSummaryDto {
  id: string;
  title: string;
  status: QuizStatus;
  accessType: QuizAccessType;
  purpose: QuizPurpose;
  questionsCount: number;
  passingScorePercent: number;
  usedByMaterialsCount: number;
  courseCount: number;
  createdAt: string;
  updatedAt: string;
}

/**
 * Вариант в запросе создания/обновления. `id` генерирует UI (crypto.randomUUID),
 * чтобы на вариант можно было сослаться из `correctOptionIds`.
 */
export interface QuizOptionRequest {
  id: string | null;
  text: string;
}

/** Вопрос в запросе создания/обновления — порядок в массиве = порядок показа. */
export interface QuizQuestionRequest {
  id: string | null;
  type: QuizQuestionType;
  text: string;
  options?: QuizOptionRequest[] | null;
  correctOptionIds?: string[] | null;
  referenceAnswer?: string | null;
  /** Пояснение к вопросу — студент видит его в разборе после ответа (#561). */
  explanation?: string | null;
  /** Ключ секции level-test'а; не передавать / `null` для MATERIAL_CHECK-квизов. */
  section?: string | null;
  difficulty?: QuizDifficultyLevel | null;
}

/** Конфигурация level-test'а в запросе (replace целиком; `null` — очистить). */
export interface LevelTestConfigRequest {
  levelThresholds: LevelThresholdConfig[];
  sections?: LevelTestSectionConfig[] | null;
  fallbackCourseId?: string | null;
}

/**
 * `POST /quizzes/` — квиз создаётся в DRAFT, standalone (#489, materialId снят в ST-10).
 * Привязка к материалу — через PUT материала (`UpdateMaterialRequest.quizId`).
 */
export interface CreateQuizRequest {
  title: string;
  questions?: QuizQuestionRequest[] | null;
  passingScorePercent: number;
  /** Назначение (immutable). Не передавать / `null` — MATERIAL_CHECK. */
  purpose?: QuizPurpose | null;
  levelTestConfig?: LevelTestConfigRequest | null;
  /** Уровень доступа. Не передавать / `null` — PUBLIC. */
  accessType?: QuizAccessType | null;
}

/** `PUT /quizzes/{id}/` — заменяет весь набор вопросов и level-test конфиг целиком. */
export interface UpdateQuizRequest {
  title: string;
  questions?: QuizQuestionRequest[] | null;
  passingScorePercent: number;
  /** `null` / не передан — очищает конфигурацию level-test'а. */
  levelTestConfig?: LevelTestConfigRequest | null;
  /** Новый уровень доступа. Не передавать / `null` — не менять. */
  accessType?: QuizAccessType | null;
}

/** Ответ на один вопрос в `POST /progress/quizzes/{quizId}/attempts/`. */
export interface SubmitQuizAnswerItem {
  questionId: string;
  selectedOptionIds?: string[] | null;
  textAnswer?: string | null;
}

export interface SubmitQuizAttemptRequest {
  answers: SubmitQuizAnswerItem[];
}

/**
 * Per-вопрос разбор попытки. `correct`: true/false для choice-вопросов,
 * `null` для OPEN_TEXT (самопроверка по `referenceAnswer`, в score не входит).
 */
export interface QuizAttemptQuestionResultDto {
  questionId: string;
  type: QuizQuestionType;
  correct: boolean | null;
  selectedOptionIds: string[];
  correctOptionIds: string[];
  textAnswer: string | null;
  referenceAnswer: string | null;
  /** Снапшот вариантов (id+text) из answer-key — разбор рисуется из result-DTO, а не из «живого» квиза (#556). Пустой для текстовых вопросов. */
  options: QuizAttemptOptionResultDto[];
  /** Пояснение к вопросу — нейтральный блок «Пояснение» в разборе (#561). */
  explanation: string | null;
}

/** Вариант ответа в разборе попытки (id+text) — для self-contained ревью-экрана (#556). */
export interface QuizAttemptOptionResultDto {
  id: string;
  text: string;
}

/** Результат попытки — full-reveal разбор после сабмита. */
export interface QuizAttemptResultDto {
  attemptId: string;
  scorePercent: number;
  passed: boolean;
  passingScorePercent: number;
  submittedAt: string;
  questions: QuizAttemptQuestionResultDto[];
}

/** `GET /progress/quizzes/{quizId}/attempts/my/` — нет попыток → оба поля null. */
export interface MyQuizAttemptsDto {
  best: QuizAttemptResultDto | null;
  last: QuizAttemptResultDto | null;
}

/**
 * `POST /progress/quizzes/{quizId}/questions/{questionId}/check/` (#556) — текущий
 * ответ студента на один вопрос для немедленной проверки «на лету». Попытка не
 * сохраняется. Только COURSE-квизы (level-test исключён).
 */
export interface CheckQuizQuestionRequest {
  selectedOptionIds?: string[] | null;
  textAnswer?: string | null;
}

/**
 * Результат проверки одного вопроса. `correct`: true/false для choice/EXACT_TEXT,
 * `null` для OPEN_TEXT (самопроверка по `referenceAnswer`). `options` (id+text) —
 * снапшот вариантов для раскраски на клиенте.
 */
export interface CheckQuizQuestionResultDto {
  questionId: string;
  type: QuizQuestionType;
  correct: boolean | null;
  correctOptionIds: string[];
  referenceAnswer: string | null;
  options: QuizOptionDto[];
  /** Пояснение к вопросу — нейтральный блок «Пояснение» под вердиктом (#561). */
  explanation: string | null;
}
