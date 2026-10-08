/**
 * Форматирует offset в секундах для главы видео в `MM:SS` (или `H:MM:SS` для часовых).
 * Отличается от `shared/lib/duration.ts#formatDurationSeconds` тем, что `0` — валидное
 * значение (первая глава видео), а не «длительность отсутствует».
 */
export function formatTimecode(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (n: number) => n.toString().padStart(2, "0");
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
}
