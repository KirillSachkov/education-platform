/**
 * 6-ступенчатая шкала уровня разработчика (#528) — зеркало C#-enum
 * `DeveloperLevel` (ECS). Живёт в shared: словарь нужен и `entities/level-test`
 * (результат воронки), и `entities/quiz` (пороги level-test-конфига) —
 * кросс-слайс импорт между entities запрещён FSD.
 */

export const DEVELOPER_LEVELS = [
  "PRE_JUNIOR",
  "JUNIOR",
  "JUNIOR_PLUS",
  "MIDDLE",
  "MIDDLE_PLUS",
  "SENIOR",
] as const;

export type DeveloperLevel = (typeof DEVELOPER_LEVELS)[number];

/** Русские подписи уровней — общие для воронки и авторского редактора. */
export const DEVELOPER_LEVEL_LABELS: Record<DeveloperLevel, string> = {
  PRE_JUNIOR: "Новичок",
  JUNIOR: "Junior",
  JUNIOR_PLUS: "Junior+",
  MIDDLE: "Middle",
  MIDDLE_PLUS: "Middle+",
  SENIOR: "Senior",
};
