/**
 * Типы тем тренажёра — зеркалят C#-контракты `TrainerService.Contracts.Topics`
 * и `.Progress`. ASP.NET сериализует свойства camelCase, enum'ы — строками в
 * UPPER_SNAKE_CASE. Issue #568 (Ф1 — DRILL).
 */

import type { LockReason } from "@/shared/lib/lock-copy";

/**
 * Тема в студенческом списке/прогресс-карте: метаданные темы + персональный
 * mastery вызывающего + фримиум-флаги. Зеркало `TopicListItemDto`.
 */
export interface TrainerTopicListItem {
  id: string;
  slug: string;
  title: string;
  area: string;
  description: string | null;
  recommendedCourseId: string | null;
  fallbackCourseId: string | null;
  /** Mastery вызывающего по теме, 0..100 (EWMA). 0 — ещё не тренировался. Внутренний сигнал «силы». */
  masteryPercent: number;
  /**
   * «Освоение» = ПОКРЫТИЕ темы, 0..100 (#664): доля вопросов темы, решённых ВЕРНО, из всех вопросов
   * её банков. Это число показывает карточка темы («Освоение N%»), а не EWMA-`masteryPercent`
   * (тот взлетает до 100% с 1-2 верных ответов). «Хорошо изучена» — только при высоком покрытии.
   */
  coveragePercent: number;
  /** Тема «хромает» (mastery < 60) — подсвечивается в карте слабых тем. */
  isWeak: boolean;
  /** Сколько ответов вызывающий дал по теме (авто-грейдимых). */
  answersCount: number;
  /** Есть ли у темы бесплатный банк вопросов (фримиум-воронка). */
  hasFreeBank: boolean;
  /** Все банки темы платные и вызывающий не admin → тема заблокирована целиком. */
  isLocked: boolean;
  /**
   * Машинно-читаемая причина замка для paywall-копирайта (`LockReason`):
   * `"pro_required"`, когда тема заблокирована (нужна подписка Trainer Pro),
   * иначе `null`. Зеркало `TopicListItemDto.LockReason`.
   */
  lockReason: LockReason | null;
}

/**
 * Mastery + study-аналитика вызывающего по одной теме. Зеркало `TopicMasteryDto`.
 * `masteryPercent`/`answersCount` — из тестов/learn-by-test; `studiedCount`
 * (любая строка study-state) и `mistakesCount` (WRONG/REVIEW) — из карточек +
 * learn-by-test (Ф2).
 */
export interface TrainerTopicMastery {
  topicId: string;
  masteryPercent: number;
  /** «Освоение» = покрытие темы 0..100 (#664): доля вопросов, решённых верно, из всех в банках темы. */
  coveragePercent: number;
  isWeak: boolean;
  answersCount: number;
  /** Сколько вопросов темы вызывающий изучил (статус ≠ NEW). */
  studiedCount: number;
  /** Сколько вопросов темы помечены как ошибка/на повтор (WRONG/REVIEW). */
  mistakesCount: number;
  lastPractisedAt: string;
}

/** Краткая запись недавней сессии в прогресс-сводке. Зеркало `RecentSessionDto`. */
export interface TrainerRecentSession {
  id: string;
  mode: string;
  status: string;
  topicIds: string[];
  scorePercent: number | null;
  startedAt: string;
  completedAt: string | null;
}

/**
 * Прогресс-сводка вызывающего: mastery по всем темам + недавние сессии.
 * Зеркало `TrainerProgressDto`.
 */
export interface TrainerProgress {
  mastery: TrainerTopicMastery[];
  recentSessions: TrainerRecentSession[];
}
