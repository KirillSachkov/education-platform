/**
 * Типы режима «Изучение» тренажёра (#568 Ф2) — зеркалят C#-контракты
 * `TrainerService.Contracts.Questions`. Свойства camelCase, enum'ы строками в
 * UPPER_SNAKE_CASE.
 *
 * Список вопросов охвата НЕ несёт ключа грейдинга (только метаданные + статус).
 * Study-payload карточки намеренно раскрывает эталон/правильные варианты/разбор —
 * это «study-mode», гейтится тиром темы на бэке.
 */

import type { QuizQuestionType } from "@/entities/quiz";
import type { LockReason } from "@/shared/lib/lock-copy";

/**
 * Статус изучения вопроса (`QuestionStudyStatus`). **`NEW` не хранится** — это
 * отсутствие строки `QuestionStudyState`. Двигается самооценкой карточки и
 * результатом learn-by-test, питает SRS.
 */
export const TRAINER_STUDY_STATUSES = ["NEW", "SEEN", "KNOWN", "REVIEW", "WRONG"] as const;

export type TrainerStudyStatus = (typeof TRAINER_STUDY_STATUSES)[number];

/**
 * Вопрос в списке охвата (тема) — студенческая проекция БЕЗ ответов: метаданные +
 * персональный статус изучения + флаг закладки. Зеркало `QuestionListItemDto`.
 */
export interface TrainerQuestionListItem {
  questionId: string;
  /**
   * Текст вопроса (стем). `null` для заблокированной (`isLocked`) строки не-PRO пользователя —
   * сервер редактирует контент (#674); фронт рисует blur-плейсхолдер вместо стема и ведёт в Pro.
   */
  stem: string | null;
  type: QuizQuestionType;
  difficulty: string | null;
  section: string | null;
  status: TrainerStudyStatus;
  isBookmarked: boolean;
  /**
   * Монетизация по типу вопроса (#623): развёрнутый (OPEN_TEXT) вопрос за PRO ИЛИ тема целиком
   * заблокирована. Закрытые тесты у free открыты. Стем перечисляется в любом случае (каталог),
   * но locked-строка не открывает карточку с эталоном. Зеркало `QuestionListItemDto.IsLocked`.
   */
  isLocked: boolean;
  lockReason: LockReason | null;
}

/**
 * Список вопросов охвата + контекст темы (заблокирована ли она целиком
 * фримиум-гейтом). Для PRO-темы без доступа метаданные всё равно перечисляются
 * (`isLocked=true`, items без ответов). Зеркало `QuestionListDto`.
 */
export interface TrainerQuestionList {
  topicId: string;
  isLocked: boolean;
  /**
   * Машинно-читаемая причина замка для paywall-копирайта (`LockReason`):
   * `"pro_required"`, когда тема заблокирована, иначе `null`. Зеркало
   * `QuestionListDto.LockReason`.
   */
  lockReason: LockReason | null;
  items: TrainerQuestionListItem[];
}

/** Вопрос «на повтор сегодня» в кросс-тематической SRS-очереди (`SrsDueItemDto`). */
export interface TrainerSrsDueItem {
  questionId: string;
  topicId: string;
  /** Стем; `null` если вопрос заблокирован (`isLocked`) и контент отредактирован сервером (#674). */
  stem: string | null;
  difficulty: string | null;
  status: TrainerStudyStatus;
  nextDueAt: string | null;
  /** Заблокирован ли вопрос фримиум-гейтом (#674). Тогда `stem === null`. */
  isLocked: boolean;
  /** Машинно-читаемая причина замка (`"pro_required"`) или `null`. */
  lockReason: LockReason | null;
}

/** Вопрос в кросс-тематическом списке «Мои ошибки» (`MistakeItemDto`). */
export interface TrainerMistakeItem {
  questionId: string;
  topicId: string;
  /** Стем; `null` если вопрос заблокирован (`isLocked`) и контент отредактирован сервером (#674). */
  stem: string | null;
  difficulty: string | null;
  status: TrainerStudyStatus;
  timesWrong: number;
  lastSeenAt: string;
  nextDueAt: string | null;
  /** Заблокирован ли вопрос фримиум-гейтом (#674). Тогда `stem === null`. */
  isLocked: boolean;
  /** Машинно-читаемая причина замка (`"pro_required"`) или `null`. */
  lockReason: LockReason | null;
}

/** Фильтр списка вопросов охвата (зеркало `?difficulty=&status=&type=&tag=`). */
export interface TrainerQuestionsFilter {
  difficulty?: string;
  status?: string;
  type?: string;
  tag?: string;
}

/** Фильтр «Моих ошибок» (зеркало `?topicId=&difficulty=&limit=`). */
export interface TrainerMistakesFilter {
  topicId?: string;
  difficulty?: string;
  limit?: number;
}
