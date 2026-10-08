/**
 * Rough reading-time estimate for markdown text. Counts whitespace-delimited
 * tokens (treats `**bold**` and `[label](href)` etc. as single tokens — close
 * enough for a UI badge). Returns whole minutes ≥ 1 so a short note still
 * shows "1 мин" instead of "0 мин".
 *
 * 200 wpm — typical Russian/English silent reading speed.
 */
const WORDS_PER_MINUTE = 200;

export function estimateReadingMinutes(text: string | null | undefined): number {
  if (!text) return 0;
  const tokens = text.trim().split(/\s+/).filter(Boolean);
  if (tokens.length === 0) return 0;
  return Math.max(1, Math.round(tokens.length / WORDS_PER_MINUTE));
}

export function formatReadingTime(minutes: number): string {
  if (minutes <= 0) return "";
  if (minutes < 60) return `${minutes} мин`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest > 0 ? `${hours} ч ${rest} мин` : `${hours} ч`;
}
