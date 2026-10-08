/** Направление дельты mastery «vs месяц назад» (#681 T6). `none` = нет исторического снимка. */
export type DeltaTone = "up" | "down" | "flat" | "none";

/** Классификация дельты mastery по знаку (null → «нет истории»). */
export function deltaTone(delta: number | null): DeltaTone {
  if (delta == null) return "none";
  if (delta > 0) return "up";
  if (delta < 0) return "down";
  return "flat";
}

/**
 * Подпись дельты mastery: «+N» рост / «−N» спад (настоящий минус U+2212) / «0» без изменений /
 * «новая» когда исторического снимка нет (ранний пользователь).
 */
export function formatDelta(delta: number | null): string {
  if (delta == null) return "новая";
  if (delta > 0) return `+${delta}`;
  if (delta < 0) return `−${Math.abs(delta)}`;
  return "0";
}
