import type { TrainerLesson } from "@/entities/trainer-question";

/**
 * Прогресс прохождения мини-теста (юнита) уровня для карточки в под-режиме «Тест» (#568).
 *
 * Статус вопроса в study-state двигается результатом КАЖДОГО ответа в тесте (DRILL) и
 * тренировке (LEARN): `RecordTestResult` ставит `KNOWN` (верно) / `WRONG` (неверно) сразу,
 * без многоповторного SM-2. Поэтому `lesson.known` = сколько вопросов юнита отвечено верно
 * в ПОСЛЕДНИЙ раз, а `attempted` = сколько вопросов вообще тронуто. Тест END_OF_SESSION
 * отвечает на весь набор → завершённая попытка даёт `attempted === total`.
 */
export type LessonProgressKind =
  /** Ни один вопрос юнита не тронут — тест ещё не проходили. */
  | "untouched"
  /** Часть вопросов отвечена, но не весь набор (например, через под-режим «Вопросы»). */
  | "in_progress"
  /** Весь набор пройден, но не все верно — показываем «последнюю попытку X из Y». */
  | "attempted"
  /** Все вопросы верны — тест пройден на 100%. */
  | "passed";

export interface LessonProgress {
  kind: LessonProgressKind;
  /** Сколько вопросов юнита верно в последней попытке (`known`). */
  correct: number;
  /** Всего вопросов в юните. */
  total: number;
  /** Процент верных (`correct/total`), 0..100. */
  percent: number;
}

/**
 * Считает прогресс юнита из его дериватов (`known`/`attempted`/`total`/`isComplete`).
 * Чистая функция — без React, тестируется изолированно.
 */
export function deriveLessonProgress(
  lesson: Pick<TrainerLesson, "known" | "attempted" | "total" | "isComplete">,
): LessonProgress {
  const { known, attempted, total, isComplete } = lesson;
  const percent = total > 0 ? Math.round((known / total) * 100) : 0;

  let kind: LessonProgressKind;
  if (attempted === 0 || total === 0) {
    kind = "untouched";
  } else if (isComplete) {
    kind = "passed";
  } else if (attempted >= total) {
    // Весь набор отвечен (завершённая попытка теста), но не все верно.
    kind = "attempted";
  } else {
    // Тронута только часть набора — ещё в процессе.
    kind = "in_progress";
  }

  return { kind, correct: known, total, percent };
}
