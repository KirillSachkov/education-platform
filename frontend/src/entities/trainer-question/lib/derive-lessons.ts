/**
 * Деривация «юнитов» (мини-тестов Duolingo-стиля) из списка вопросов охвата темы
 * (#568 Ф3). Гибрид: уровни сложности × нумерованные части. Группируем по
 * фиксированному порядку сложности, режем каждую группу на части по
 * `LESSON_SIZE`, прогресс считаем из персонального `status` вопроса.
 *
 * Живёт в `entities/trainer-question/lib`, чтобы виджет/фичи могли строить из
 * него урок-лист без cross-slice импорта (downward: widget → entities → shared).
 */

import {
  TRAINER_DIFFICULTIES,
  TRAINER_DIFFICULTY_VISUALS,
  type TrainerDifficulty,
} from "@/shared/config/trainer";
import type { TrainerQuestionListItem } from "../types";

/** Размер одного юнита (части уровня). Дальше — следующая часть «· Тест N». */
export const LESSON_SIZE = 8;

/** Спец-ключ группы вопросов без проставленной сложности. */
export const UNLEVELED_KEY = "UNLEVELED";

/** Ключ уровня юнита — литерал сложности или спец-«без уровня». */
export type LessonLevelKey = TrainerDifficulty | typeof UNLEVELED_KEY;

/**
 * Один юнит (мини-тест): срез вопросов одного уровня, до `LESSON_SIZE` штук,
 * с дериватом прогресса. `id` стабилен между рендерами (уровень + индекс части).
 */
export interface TrainerLesson {
  /** Стабильный ключ для key/URL (`<level>-<partIndex>`). */
  id: string;
  /** Уровень сложности юнита (или «без уровня»). */
  level: LessonLevelKey;
  /** Заголовок: «Джуниор» (одна часть) или «Джуниор · Тест 2» (нескольких частей). */
  title: string;
  /** 0-based индекс части внутри уровня. */
  partIndex: number;
  /** Сколько всего частей у этого уровня (для подписи «Тест 1 из 3»). */
  partCount: number;
  /** Вопросы юнита в порядке списка (то, что уйдёт в `questionIds` старта теста). */
  questionIds: string[];
  /** Всего вопросов в юните (= `questionIds.length`). */
  total: number;
  /** Сколько вопросов изучено (`status==='KNOWN'`). */
  known: number;
  /** Сколько вопросов хоть раз тронуто (`status!=='NEW'`). */
  attempted: number;
  /** Юнит пройден — все вопросы в статусе KNOWN. */
  isComplete: boolean;
}

/** Подпись уровня в заголовке юнита. «Без уровня» — для вопросов без difficulty. */
function levelLabel(level: LessonLevelKey): string {
  if (level === UNLEVELED_KEY) return "Без уровня";
  return TRAINER_DIFFICULTY_VISUALS[level]?.label ?? level;
}

/** Разбить массив на чанки фиксированного размера, сохраняя порядок. */
function chunk<T>(items: T[], size: number): T[][] {
  const chunks: T[][] = [];
  for (let index = 0; index < items.length; index += size) {
    chunks.push(items.slice(index, index + size));
  }
  return chunks;
}

/**
 * Развернуть список вопросов охвата темы в упорядоченные юниты:
 * `[JUNIOR, MIDDLE, SENIOR]` (пустые уровни пропускаем), затем хвостовая группа
 * «Без уровня» для вопросов без difficulty. Каждый уровень режется на части по
 * `LESSON_SIZE` с сохранением порядка списка.
 */
export function deriveLessons(items: TrainerQuestionListItem[]): TrainerLesson[] {
  const byLevel = new Map<LessonLevelKey, TrainerQuestionListItem[]>();
  for (const item of items) {
    const key: LessonLevelKey =
      item.difficulty && TRAINER_DIFFICULTIES.includes(item.difficulty as TrainerDifficulty)
        ? (item.difficulty as TrainerDifficulty)
        : UNLEVELED_KEY;
    const bucket = byLevel.get(key);
    if (bucket) {
      bucket.push(item);
    } else {
      byLevel.set(key, [item]);
    }
  }

  // Фиксированный порядок: уровни сложности, затем «без уровня» хвостом.
  const orderedKeys: LessonLevelKey[] = [...TRAINER_DIFFICULTIES, UNLEVELED_KEY];
  const lessons: TrainerLesson[] = [];

  for (const level of orderedKeys) {
    const levelItems = byLevel.get(level);
    if (!levelItems || levelItems.length === 0) continue;

    const parts = chunk(levelItems, LESSON_SIZE);
    const label = levelLabel(level);

    parts.forEach((partItems, partIndex) => {
      const questionIds = partItems.map((item) => item.questionId);
      const known = partItems.filter((item) => item.status === "KNOWN").length;
      const attempted = partItems.filter((item) => item.status !== "NEW").length;
      const title = parts.length > 1 ? `${label} · Тест ${partIndex + 1}` : label;

      lessons.push({
        id: `${level}-${partIndex}`,
        level,
        title,
        partIndex,
        partCount: parts.length,
        questionIds,
        total: questionIds.length,
        known,
        attempted,
        isComplete: questionIds.length > 0 && known === questionIds.length,
      });
    });
  }

  return lessons;
}
