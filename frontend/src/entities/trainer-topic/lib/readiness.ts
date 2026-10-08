import type { TrainerTopicMastery } from "../types";

/**
 * Минимум практикованных тем, ниже которого «Готовность к собесу» считается недостоверной
 * (слишком узкая выборка — пара тем не отражает готовность ко всему собесу).
 */
export const MIN_TOPICS_FOR_CONFIDENCE = 3;
/**
 * Минимум авто-грейдимых ответов, ниже которого готовность недостоверна (мало сигнала —
 * 2-3 ответа легко дают шумные 100%).
 */
export const MIN_ANSWERS_FOR_CONFIDENCE = 10;

export interface InterviewReadiness {
  /**
   * Честная «готовность к собесу» 0..100 — среднее ПОКРЫТИЕ по ВСЕМ опубликованным темам,
   * где нетронутая тема считается за 0 (штраф за охват). `null` — практики ещё не было.
   */
  percent: number | null;
  /** Сколько тем реально практиковалось (есть авто-грейдимые ответы). */
  touchedTopics: number;
  /** Всего опубликованных тем в текущем scope — знаменатель охвата (breadth). */
  totalTopics: number;
  /** Суммарно авто-грейдимых ответов по практикованным темам — размер выборки. */
  answeredCount: number;
  /** `false`, когда выборка слишком мала, чтобы доверять проценту (мало тем / мало ответов). */
  isConfident: boolean;
}

function clampPercent(value: number): number {
  if (!Number.isFinite(value)) return 0;
  return Math.max(0, Math.min(100, value));
}

/**
 * «Готовность к собесу» с учётом И глубины, И охвата (#691).
 *
 * Прежняя формула брала средний mastery ТОЛЬКО по тронутым темам → раздувалась
 * (75% на 4 из 10 тем). Здесь:
 *  - сигнал глубины — `coveragePercent` (доля банка темы, отвеченная верно, #664), а НЕ
 *    EWMA-`masteryPercent` (тот прыгает к ~100% с 1-2 верных ответов);
 *  - охват — знаменатель = ВСЕ опубликованные темы; нетронутая тема вносит 0.
 *
 * Итог = `sum(coveragePercent по практикованным) / totalPublishedTopics` — честное среднее по
 * всему банку тем. Малая выборка помечается `isConfident=false`, чтобы UI не показывал
 * уверенный крупный процент, а дал приглушённое «недостаточно данных».
 */
export function computeInterviewReadiness(
  mastery: TrainerTopicMastery[],
  totalPublishedTopics: number,
): InterviewReadiness {
  const practised = mastery.filter((row) => row.answersCount > 0);
  const touchedTopics = practised.length;
  // Знаменатель охвата: никогда не меньше числа уже практикованных тем (защита от устаревшего/
  // меньшего published-счётчика — иначе % вылез бы за 100).
  const totalTopics = Math.max(totalPublishedTopics, touchedTopics);
  const answeredCount = practised.reduce((sum, row) => sum + row.answersCount, 0);

  if (touchedTopics === 0 || totalTopics === 0) {
    return { percent: null, touchedTopics, totalTopics, answeredCount, isConfident: false };
  }

  const coverageSum = practised.reduce((sum, row) => sum + clampPercent(row.coveragePercent), 0);
  const percent = Math.round(coverageSum / totalTopics);

  const isConfident =
    touchedTopics >= MIN_TOPICS_FOR_CONFIDENCE && answeredCount >= MIN_ANSWERS_FOR_CONFIDENCE;

  return { percent, touchedTopics, totalTopics, answeredCount, isConfident };
}
