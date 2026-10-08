import type { QuizOptionDto } from "../types";

/**
 * Стабильное перемешивание вариантов ответа (#528): позиция правильного не должна
 * выдавать ответ (авторы часто пишут правильный первым). Сортировка по FNV-1a
 * хэшу пары (questionId, optionId) — детерминирована для попытки (порядок не
 * прыгает при навигации назад/вперёд), но различна между вопросами. Грейдинг
 * идёт по id вариантов, поэтому порядок показа на корректность не влияет.
 */
export function stableShuffleOptions<T extends QuizOptionDto>(
  questionId: string,
  options: readonly T[],
): T[] {
  return [...options].sort((a, b) => fnv1a(questionId + a.id) - fnv1a(questionId + b.id));
}

function fnv1a(input: string): number {
  let hash = 0x811c9dc5;
  for (let i = 0; i < input.length; i++) {
    hash ^= input.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193);
  }
  return hash >>> 0;
}
