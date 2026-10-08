/**
 * Парсит query-param `?t=<seconds>` для deep-link'а на конкретный момент видео.
 * Используется страницей материала чтобы открыть Kinescope-плеер на нужной секунде —
 * URL приходит из результата поиска, когда совпадение было по главе видео.
 *
 * Поведение: принимает строку или null/undefined; возвращает положительное число
 * секунд или null если значение отсутствует / невалидное / отрицательное /
 * бесконечное.
 */
export function parseStartSeconds(value: string | null | undefined): number | null {
  if (!value) return null;
  const parsed = Number.parseInt(value, 10);
  if (!Number.isFinite(parsed) || parsed <= 0) return null;
  return parsed;
}
