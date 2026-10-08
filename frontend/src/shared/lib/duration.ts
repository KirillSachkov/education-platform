/**
 * Форматирует длительность из миллисекунд в читаемый формат
 * @param durationMs Длительность в миллисекундах
 * @returns Строка в формате "MM:SS" или "HH:MM:SS" для длинных видео
 */
export function formatDuration(durationMs: number | null | undefined): string {
  if (!durationMs) return "--:--";

  const totalSeconds = Math.floor(durationMs / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  const pad = (n: number) => n.toString().padStart(2, "0");

  if (hours > 0) {
    return `${hours}:${pad(minutes)}:${pad(seconds)}`;
  }

  return `${minutes}:${pad(seconds)}`;
}

/**
 * Форматирует длительность в человекочитаемый формат
 * @param durationMs Длительность в миллисекундах
 * @returns Строка типа "5 мин" или "1 ч 30 мин"
 */
export function formatDurationHuman(
  durationMs: number | null | undefined,
): string {
  if (!durationMs) return "—";

  const totalSeconds = Math.floor(durationMs / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);

  if (hours > 0) {
    return minutes > 0 ? `${hours} ч ${minutes} мин` : `${hours} ч`;
  }

  return minutes > 0 ? `${minutes} мин` : "< 1 мин";
}

/**
 * Форматирует длительность из секунд в MM:SS / HH:MM:SS
 * Обёртка над formatDuration для бэкенд-данных (durationSeconds)
 */
export function formatDurationSeconds(
  durationSeconds: number | null | undefined,
): string {
  if (!durationSeconds) return "--:--";
  return formatDuration(durationSeconds * 1000);
}

/**
 * Форматирует длительность из секунд в человекочитаемый формат
 * Возвращает пустую строку для null/0 — удобно для условного рендеринга
 */
export function formatDurationSecondsHuman(
  durationSeconds: number | null | undefined,
): string {
  if (!durationSeconds) return "";
  return formatDurationHuman(durationSeconds * 1000);
}
