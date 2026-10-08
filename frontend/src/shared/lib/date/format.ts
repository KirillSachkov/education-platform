/**
 * Date formatting utilities with cached Intl.DateTimeFormat instances
 * for better performance when formatting many dates.
 */

const shortDateFormatter = new Intl.DateTimeFormat("ru-RU", {
  day: "numeric",
  month: "short",
  year: "numeric",
});

const fullDateFormatter = new Intl.DateTimeFormat("ru-RU", {
  day: "numeric",
  month: "long",
  year: "numeric",
});

const monthYearFormatter = new Intl.DateTimeFormat("ru-RU", {
  month: "short",
  year: "numeric",
});

const numericDateFormatter = new Intl.DateTimeFormat("ru-RU", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
});

const shortDateWithTimeFormatter = new Intl.DateTimeFormat("ru-RU", {
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
});

const fullDateWithTimeFormatter = new Intl.DateTimeFormat("ru-RU", {
  day: "numeric",
  month: "long",
  hour: "2-digit",
  minute: "2-digit",
});

const relativeTimeFormatter = new Intl.RelativeTimeFormat("ru-RU", {
  numeric: "auto",
});

const relativeTimeUnits: Array<[Intl.RelativeTimeFormatUnit, number]> = [
  ["year", 1000 * 60 * 60 * 24 * 365],
  ["month", 1000 * 60 * 60 * 24 * 30],
  ["week", 1000 * 60 * 60 * 24 * 7],
  ["day", 1000 * 60 * 60 * 24],
  ["hour", 1000 * 60 * 60],
  ["minute", 1000 * 60],
];

/**
 * Format date as "26 янв. 2026"
 */
export function formatShortDate(date: string | Date): string {
  return shortDateFormatter.format(new Date(date));
}

/**
 * Format date as "26 января 2026"
 */
export function formatFullDate(date: string | Date): string {
  return fullDateFormatter.format(new Date(date));
}

/**
 * Format date as "янв. 2026"
 */
export function formatMonthYear(date: string | Date): string {
  return monthYearFormatter.format(new Date(date));
}

/**
 * Format date as "22.04.2026" — for badges where numeric form reads better
 * (status pills like "Изучено 22.04.2026" / "Выполнено 22.04.2026").
 */
export function formatNumericDate(date: string | Date): string {
  return numericDateFormatter.format(new Date(date));
}

/**
 * Format date as "20 февр., 00:00"
 */
export function formatShortDateWithTime(date: string | Date): string {
  return shortDateWithTimeFormatter.format(new Date(date));
}

/**
 * Format date as "20 февраля, 00:00"
 */
export function formatFullDateWithTime(date: string | Date): string {
  return fullDateWithTimeFormatter.format(new Date(date));
}

/**
 * Format date relative to now as "5 минут назад" / "только что".
 */
export function formatRelativeDate(date: string | Date): string {
  const value = new Date(date);
  const diffMs = value.getTime() - Date.now();
  const absMs = Math.abs(diffMs);

  for (const [unit, unitMs] of relativeTimeUnits) {
    if (absMs >= unitMs) {
      return relativeTimeFormatter.format(Math.round(diffMs / unitMs), unit);
    }
  }

  return "только что";
}
