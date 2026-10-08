/**
 * Типы тренировочных сессий + закладок — зеркалят C#-контракты
 * `TrainerService.Contracts.Sessions` и `.Bookmarks`. Свойства camelCase,
 * enum'ы строками в UPPER_SNAKE_CASE. Issue #568 (Ф1 — DRILL).
 *
 * Типы вопросов и грейдинг зеркалят ECS/ProgressService: choice/exact → 100|0,
 * OPEN_TEXT → вердикт PENDING (самопроверка по эталону, балл null).
 */

import type { QuizQuestionType } from "@/entities/quiz";
import type { LockReason } from "@/shared/lib/lock-copy";

/** Режим сессии. `LEARN` — formative «обучение по тестам» (Ф2); CHALLENGE зарезервирован. */
export type TrainerSessionMode = "DRILL" | "LEARN" | "MOCK" | "CHALLENGE";

export type TrainerSessionStatus = "IN_PROGRESS" | "COMPLETED" | "ABANDONED";

/**
 * Когда раскрывается правильный ответ (`RevealPolicy`, Ф2): `END_OF_SESSION` —
 * grade-at-end (счёт копится, разбор после Complete; до этого вердикт/балл/ключ
 * скрыты) | `PER_QUESTION` — мгновенный фидбэк. DRILL/LEARN/MOCK = PER_QUESTION,
 * grade-at-end тест = END_OF_SESSION.
 */
export type TrainerRevealPolicy = "END_OF_SESSION" | "PER_QUESTION";

/**
 * Вердикт проверки одного ответа (зеркало `AnswerGrader`). `CORRECT`/`INCORRECT`
 * для авто-грейдимых вопросов, `PARTIAL` — частично верный открытый ответ (после
 * AI-грейдинга мока), `PENDING` — открытый вопрос до проверки (самопроверка по
 * эталону, mastery не трогается).
 */
export type TrainerVerdict = "CORRECT" | "PARTIAL" | "INCORRECT" | "PENDING";

/**
 * Статус AI-грейдинга открытых ответов мок-сессии (#585). `NOT_REQUIRED` —
 * грейдинг не нужен (нет открытых ответов / не MOCK); `PENDING` — поставлен в
 * очередь после Complete; `GRADING` — AI обрабатывает; `GRADED` — готов разбор;
 * `FAILED` — AI недоступен (показываем авто-балл без фидбэка). Зеркало
 * `SessionDto.GradingStatus`.
 */
export type TrainerGradingStatus = "NOT_REQUIRED" | "PENDING" | "GRADING" | "GRADED" | "FAILED";

/** Кол-во вопросов в DRILL-сессии по умолчанию, если не задано явно. */
export const TRAINER_DEFAULT_QUESTION_COUNT = 10;

/** Вариант ответа в снапшоте вопроса (id + текст, без признака правильности). */
export interface TrainerSessionOption {
  id: string;
  text: string;
}

/**
 * Снапшот вопроса сессии — студенческая проекция БЕЗ ключа грейдинга. Если
 * вопрос уже отвечен (`isAnswered`), несёт результат проверки для ревью:
 * вердикт/балл/фидбэк/разбор + правильный ответ. Для неотвеченного — все
 * «answer/correct»-поля = null. Зеркало `SessionItemDto`.
 */
export interface TrainerSessionItem {
  id: string;
  questionId: string;
  /** Тема-источник вопроса (для mock — per-topic разбивка). Зеркало `SessionItemDto.TopicId`. */
  topicId: string;
  questionType: QuizQuestionType;
  /**
   * Текст вопроса. `null` для заблокированного (`isLocked`) вопроса не-PRO пользователя —
   * сервер редактирует контент (#674), фронт рисует blur-плейсхолдер вместо markdown.
   */
  questionText: string | null;
  /** Варианты ответа. Пустой массив (`[]`) для заблокированного вопроса — ключ не утекает (#674). */
  options: TrainerSessionOption[];
  section: string | null;
  difficulty: string | null;
  sortIndex: number;
  isAnswered: boolean;
  answerRaw: string | null;
  scorePercent: number | null;
  verdict: TrainerVerdict | null;
  feedback: string | null;
  /** Раскрывается ТОЛЬКО для уже отвеченного вопроса (review). */
  correctOptionIds: string[] | null;
  referenceAnswer: string | null;
  explanation: string | null;
  /**
   * Монетизация по типу вопроса (#623): развёрнутый (OPEN_TEXT, голос → AI-анализ) вопрос
   * заблокирован для не-PRO. Закрытые тесты никогда не locked. Фронт показывает замок
   * «Доступно на полном доступе» + CTA вместо инпута. Зеркало `SessionItemDto.IsLocked`.
   */
  isLocked: boolean;
  lockReason: LockReason | null;
}

/**
 * Тренировочная сессия — студенческая проекция. Items НЕ содержат ключ
 * грейдинга (правильные ответы раскрываются на item'е лишь после ответа на
 * него). Зеркало `SessionDto`.
 */
export interface TrainerSession {
  id: string;
  mode: TrainerSessionMode;
  status: TrainerSessionStatus;
  /**
   * Когда раскрывается правильный ответ. Раннер по нему решает: показывать ли
   * разбор сразу после `CheckAnswer` (`PER_QUESTION`) или «вслепую» до Complete
   * (`END_OF_SESSION`). Зеркало `SessionDto.RevealPolicy` (Ф2).
   */
  revealPolicy: TrainerRevealPolicy;
  topicIds: string[];
  /** Лимит времени MOCK-сессии в секундах (информативный таймер). null для DRILL. */
  timeLimitSeconds: number | null;
  startedAt: string;
  completedAt: string | null;
  scorePercent: number | null;
  /**
   * Статус AI-грейдинга открытых ответов (#585). Для мока с открытыми ответами
   * после Complete переходит PENDING → GRADING → GRADED|FAILED (фронт поллит).
   * Зеркало `SessionDto.GradingStatus`.
   */
  gradingStatus: TrainerGradingStatus;
  /** Агрегатный AI-фидбэк по всей мок-сессии (раскрывается на GRADED). */
  aiOverallFeedback: string | null;
  /** Слабые темы по итогам AI-разбора мока. */
  aiWeakTopics: string[];
  /** Сильные стороны по итогам AI-разбора мока. */
  aiStrengths: string[];
  items: TrainerSessionItem[];
}

/**
 * Тело запроса на старт DRILL/тест-сессии по теме (`StartSessionRequest`).
 * `revealPolicy` (Ф2): пусто → `END_OF_SESSION` (grade-at-end по умолчанию на
 * бэке), `PER_QUESTION` — мгновенный фидбэк.
 *
 * `questionIds` (Ф3 — юниты): если задан, сессия идёт ровно по этим вопросам в
 * заданном порядке (`questionCount`/шафл игнорируются). Пусто → текущее
 * поведение (шафл `min(N, доступных)`). Питает «Тест»-юниты Duolingo-стиля.
 */
export interface StartSessionRequest {
  mode?: TrainerSessionMode | null;
  topicId: string;
  questionCount?: number | null;
  revealPolicy?: TrainerRevealPolicy | null;
  questionIds?: string[] | null;
}

/**
 * Тело запроса на старт LEARN-сессии («обучение по тестам», Ф2). Адаптивный
 * мини-тест с мгновенным фидбэком; ошибочные повторяются. Formative — без
 * записываемого балла, питает study-state + SRS. Зеркало `StartLearnSessionRequest`.
 */
export interface StartLearnSessionRequest {
  topicId: string;
  questionCount?: number | null;
}

/**
 * Тело запроса на старт REVIEW-сессии из произвольного набора вопросов
 * (`StartReviewSessionRequest`, #568). LEARN-движок (instant feedback) по
 * конкретным `questionIds` — питает «Доучить» (тест по N ошибкам) и «Пройти тест
 * по закладке». Сервер резолвит quiz/topic по id, фримиум-гейт, cap 50.
 */
export interface StartReviewSessionRequest {
  questionIds: string[];
}

/**
 * Тело запроса на старт MOCK-сессии (симуляция собеса по треку). Зеркало
 * `StartMockSessionRequest`. Вопросы набираются кросс-тематически из доступных
 * банков трека. `timeLimitSeconds` — информативный таймер (сервер не авто-фейлит).
 */
export interface StartMockSessionRequest {
  trackId: string;
  questionCount: number;
  /** Опциональный лимит времени на сессию в секундах (информативный таймер). */
  timeLimitSeconds?: number | null;
  /** Опциональный фильтр сложности (JUNIOR/MIDDLE/SENIOR). */
  difficulty?: string | null;
  /**
   * Опциональный scope по направлению трека (BACKEND/FRONTEND/FULLSTACK/GENERAL,
   * Ф2): пул сужается до тем этого направления. Пусто = весь трек.
   */
  direction?: string | null;
}

/**
 * Тело запроса на старт MOCK-сессии из кураторского мок-собеседования
 * (POSITION-ориентированная подборка, #568). Зеркало
 * `StartMockInterviewSessionRequest`. Вопросы набираются из тем подборки;
 * `timeLimitSeconds` — информативный таймер (сервер не авто-фейлит). Без фильтра
 * сложности и направления — подборка кросс-тематическая по самой себе.
 */
export interface StartMockInterviewRequest {
  /** Сколько вопросов; пусто → дефолт бэкенда. */
  questionCount?: number | null;
  /** Опциональный лимит времени на сессию в секундах (информативный таймер). */
  timeLimitSeconds?: number | null;
}

/**
 * Тело запроса на мгновенную проверку одного ответа (`CheckAnswerRequest`).
 * `optionIds` — для SINGLE/MULTI_CHOICE; `text` — для EXACT_TEXT/OPEN_TEXT.
 */
export interface CheckAnswerRequest {
  optionIds?: string[] | null;
  text?: string | null;
}

/**
 * Результат мгновенной проверки ответа (`CheckAnswerResponse`). `scorePercent`
 * = 0..100; для OPEN_TEXT в не-мок сессиях ответ AI-грейдится инлайн (#568) —
 * приходит балл + вердикт + краткий `feedback`. `null` (PENDING) — если AI-проверка
 * временно недоступна (фолбэк) либо grade-at-end (раскроется на Complete).
 */
export interface CheckAnswerResponse {
  itemId: string;
  verdict: TrainerVerdict;
  scorePercent: number | null;
  correctOptionIds: string[] | null;
  referenceAnswer: string | null;
  explanation: string | null;
  /** Краткий разбор AI для открытого ответа (#568). `null` для авто-грейда / скрытого. */
  feedback: string | null;
  /**
   * Записанный ответ студента (для OPEN_TEXT — текст ИЛИ распознанная из голоса речь, #585).
   * Нужен голосовому ответу: транскрипта на клиенте нет, поэтому «Твой ответ» в разборе берём
   * отсюда. `null` для choice/exact (у клиента свой черновик) и для скрытого раскрытия.
   */
  answerText: string | null;
}

/** Итог завершённой сессии (`SessionSummaryDto`). */
export interface TrainerSessionSummary {
  id: string;
  status: TrainerSessionStatus;
  scorePercent: number;
  totalItems: number;
  answeredItems: number;
  correctItems: number;
  completedAt: string | null;
  /**
   * Статус AI-грейдинга (#585). Для мока с открытыми ответами Complete вернёт
   * `PENDING` (грейдинг идёт асинхронно) — фронт переключается на поллинг сессии.
   */
  gradingStatus: TrainerGradingStatus;
}

/** Оценка студентом AI-разбора («Разбор ИИ») открытого ответа: палец вверх/вниз (#691 t7). */
export type TrainerFeedbackRating = "UP" | "DOWN";

/** Тело запроса на оценку AI-разбора (`RateAiFeedbackRequest`). */
export interface RateAiFeedbackRequest {
  rating: TrainerFeedbackRating;
}

/** Текущая оценка AI-разбора item'а после апсерта (`AiFeedbackRatingDto`). */
export interface AiFeedbackRatingDto {
  itemId: string;
  rating: TrainerFeedbackRating;
}

/**
 * Тело запроса на мягкую самооценку item'а сессии (`SelfAssessRequest`, #691 t8). Пока единственный
 * вердикт — `UNSURE` («Не уверен» → вопрос уходит в REVIEW / «На повтор»).
 */
export interface SelfAssessRequest {
  verdict: "UNSURE";
}

/** Новый study-status вопроса после самооценки (`SelfAssessmentDto`). `status` — обычно `REVIEW`. */
export interface SelfAssessmentDto {
  itemId: string;
  status: string;
}

/** Тело запроса на добавление закладки (`CreateBookmarkRequest`). */
export interface CreateBookmarkRequest {
  topicId: string;
  questionId: string;
}

/**
 * Закладка вызывающего на вопрос (`BookmarkDto`). Список обогащён контентом
 * вопроса (#568): `stem`/`difficulty`/`topicTitle` приходят в list-ответе (в
 * create-ответе — null). Ключ/эталон НЕ раскрывается — только текст для превью.
 */
export interface TrainerBookmark {
  id: string;
  topicId: string | null;
  questionId: string;
  createdAt: string;
  /** Текст вопроса (превью). `null`, если вопрос удалён или это create-ответ. */
  stem: string | null;
  /** Сложность вопроса (JUNIOR/MIDDLE/SENIOR) или `null`. */
  difficulty: string | null;
  /** Название темы-источника или `null`. */
  topicTitle: string | null;
}

/** Страница закладок с курсором (`GET /trainer/bookmarks?cursor=&limit=`). */
export interface TrainerBookmarksPage {
  items: TrainerBookmark[];
  /** Opaque-курсор следующей страницы; `null` — больше нет. */
  nextCursor: string | null;
}

/**
 * Краткая запись сессии в истории вызывающего («вернуться к сессии» / история).
 * Без item'ов и ключа грейдинга. Зеркало `SessionHistoryItemDto`.
 */
export interface TrainerSessionHistoryItem {
  id: string;
  mode: TrainerSessionMode;
  status: TrainerSessionStatus;
  topicIds: string[];
  scorePercent: number | null;
  answeredCount: number;
  totalCount: number;
  startedAt: string;
  completedAt: string | null;
  /** Лимит времени MOCK-сессии (сек). `null` для DRILL/LEARN. Для расчёта «истекла ли симуляция». */
  timeLimitSeconds: number | null;
  /** Статус AI-грейдинга (#585): мок-симуляция грейдится в фоне → история показывает «ИИ проверяет…». */
  gradingStatus: TrainerGradingStatus;
}

/** Фильтр истории сессий: режим + трек (оба опц.) — зеркало `?mode=&trackId=`. */
export interface TrainerSessionHistoryFilter {
  mode?: TrainerSessionMode;
  trackId?: string;
}

/**
 * Доля верных ответов по одному срезу (сложность или тема). Зеркало
 * `SessionBreakdownDto`. `Key` — difficulty-литерал (JUNIOR/...) либо topicId.
 */
export interface TrainerSessionBreakdown {
  key: string;
  /** Сколько авто-грейдимых ответов верны. */
  correct: number;
  /** Сколько авто-грейдимых (с баллом) ответов в срезе. */
  graded: number;
  /** Сколько всего item'ов в срезе (включая неотвеченные/OPEN_TEXT). */
  total: number;
}

/**
 * Разбивка результатов сессии: общий correct/total + срезы по сложности
 * (JUNIOR/MIDDLE/SENIOR) и по теме. Зеркало `SessionStatsDto`.
 */
export interface TrainerSessionStats {
  id: string;
  mode: TrainerSessionMode;
  status: TrainerSessionStatus;
  scorePercent: number | null;
  totalItems: number;
  answeredItems: number;
  correctItems: number;
  gradedItems: number;
  byDifficulty: TrainerSessionBreakdown[];
  byTopic: TrainerSessionBreakdown[];
}
