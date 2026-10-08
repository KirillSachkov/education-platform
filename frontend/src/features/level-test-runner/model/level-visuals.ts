import type { DeveloperLevel } from "@/entities/level-test";

/**
 * Визуальный словарь уровней 6-ступенчатой шкалы (#528): цвета бейджей и
 * прогресс-баров. Один источник для difficulty-бейджа в раннере (использует
 * подмножество JUNIOR/MIDDLE/SENIOR) и level/секционных баров на странице
 * результата. Палитра — холодный→тёплый→фиолетовый градиент роста:
 * slate → emerald → teal → amber → orange → violet. Подписи уровней живут
 * в entity-словаре (`@/entities/level-test`) — общие с quiz-builder.
 */

export { DEVELOPER_LEVEL_LABELS } from "@/entities/level-test";

/** Классы цветного бейджа уровня (поверх `Badge variant="outline"`). */
export const DEVELOPER_LEVEL_BADGE_CLASSES: Record<DeveloperLevel, string> = {
  PRE_JUNIOR: "border-slate-500/40 bg-slate-500/10 text-slate-600 dark:text-slate-400",
  JUNIOR: "border-emerald-500/40 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400",
  JUNIOR_PLUS: "border-teal-500/40 bg-teal-500/10 text-teal-600 dark:text-teal-400",
  MIDDLE: "border-amber-500/40 bg-amber-500/10 text-amber-600 dark:text-amber-400",
  MIDDLE_PLUS: "border-orange-500/40 bg-orange-500/10 text-orange-600 dark:text-orange-400",
  SENIOR: "border-violet-500/40 bg-violet-500/10 text-violet-600 dark:text-violet-400",
};

/** Перекраска индикатора `ProgressBar` под уровень секции (data-slot селектор kit/progress). */
export const DEVELOPER_LEVEL_BAR_CLASSES: Record<DeveloperLevel, string> = {
  PRE_JUNIOR: "[&_[data-slot=progress-indicator]]:bg-slate-500",
  JUNIOR: "[&_[data-slot=progress-indicator]]:bg-emerald-500",
  JUNIOR_PLUS: "[&_[data-slot=progress-indicator]]:bg-teal-500",
  MIDDLE: "[&_[data-slot=progress-indicator]]:bg-amber-500",
  MIDDLE_PLUS: "[&_[data-slot=progress-indicator]]:bg-orange-500",
  SENIOR: "[&_[data-slot=progress-indicator]]:bg-violet-500",
};
