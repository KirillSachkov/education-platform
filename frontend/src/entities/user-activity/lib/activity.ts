import type { ActivityDayDto } from "../types";

/**
 * CSS-классы 4 уровней интенсивности активности дня (от пустого к максимуму).
 * Используются и в heatmap-сетке, и в её легенде, чтобы цвета не разъезжались.
 */
export const ACTIVITY_INTENSITY_CLASSES = [
  "bg-muted",
  "bg-primary/30",
  "bg-primary/60",
  "bg-primary",
] as const;

/** Активность дня для heatmap-цвета: сумма начисленного XP и изученных материалов. */
export function dayScore(day: ActivityDayDto): number {
  return day.xp + day.materialsCompleted;
}

/**
 * Квантование активности дня в индекс уровня 0..3 для heatmap.
 * Пороги под XP-номиналы платформы: один изученный материал (10 XP + 1) → 1,
 * несколько → 2, день с модулем/проектом (50+ XP) → 3.
 */
export function activityIntensityIndex(score: number): 0 | 1 | 2 | 3 {
  if (score <= 0) return 0;
  if (score <= 15) return 1;
  if (score <= 45) return 2;
  return 3;
}

/** День «активен», если в нём есть хоть какой-то XP или изученный материал. */
export function isDayActive(day: ActivityDayDto): boolean {
  return day.xp > 0 || day.materialsCompleted > 0;
}

/** Один день недельной полоски стрика. */
export interface RecentDay {
  /** Дата "yyyy-MM-dd" (UTC). */
  date: string;
  /** Была ли активность в этот день. */
  active: boolean;
  /** Короткая подпись дня недели (ru), напр. «пн». */
  weekday: string;
}

const WEEKDAY_FORMAT = new Intl.DateTimeFormat("ru-RU", { weekday: "short" });

/**
 * Последние `n` дней (по умолчанию 7) из 84-дневного ряда — для недельной
 * полоски в карточке стрика. `days` упорядочен от старых к новым, последний
 * элемент — сегодня. Возвращает максимум `n` элементов (меньше, если ряд короче).
 * Дата парсится как локальная полночь — голая "yyyy-MM-dd" трактуется как
 * UTC-полночь и в западных таймзонах сместила бы подпись на день назад.
 */
export function buildRecentStrip(days: ActivityDayDto[], n = 7): RecentDay[] {
  return days.slice(-n).map((day) => ({
    date: day.date,
    active: isDayActive(day),
    weekday: WEEKDAY_FORMAT.format(new Date(`${day.date}T00:00:00`)),
  }));
}
