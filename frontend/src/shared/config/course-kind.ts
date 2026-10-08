/**
 * Тип курса (см. `Course.Kind` на бэке, issue #291).
 * Регистр совпадает с C# enum members — храним и шлём строго в UPPER_SNAKE_CASE.
 *
 * - `COURSE` — полноценный курс (модули + материалы + задания).
 * - `INTENSIVE` — мини-курс (модули + материалы, без заданий).
 * - `MARATHON` — марафон (формат с дедлайнами/когортами, issue #374).
 */
export const COURSE_KINDS = ["COURSE", "INTENSIVE", "MARATHON"] as const;

export type CourseKind = (typeof COURSE_KINDS)[number];

export const COURSE_KIND_LABELS: Record<CourseKind, string> = {
  COURSE: "Курс",
  INTENSIVE: "Интенсив",
  MARATHON: "Марафон",
};

/**
 * Единый источник правды для визуального бейджа типа курса в каталоге.
 * Цвет — НИКОГДА не единственный сигнал: всегда в паре с текстовым `badgeLabel`.
 *
 * Бейдж показываем для ВСЕХ типов — единый **приглушённый тёмно-стеклянный**
 * стиль поверх обложки: общий тёмный полупрозрачный фон (легибельность над любым
 * изображением) + тип различается только мягким тинтом текста. Намеренно тусклые,
 * минималистичные, не «кричат» цветом и не вырываются из дизайна карточки:
 * - `COURSE` — мягкий золотой тинт (флагман — премиум-акцент, см. #375).
 * - `INTENSIVE` — мягкий teal-тинт.
 * - `MARATHON` — мягкий cyan-тинт.
 *
 * Tailwind-классы согласованы с shadcn `Badge` и существующими бейджами карточек.
 */
export const COURSE_KIND_VISUALS: Record<CourseKind, { badgeLabel: string; badgeClass: string }> = {
  COURSE: { badgeLabel: COURSE_KIND_LABELS.COURSE, badgeClass: "bg-amber-950/55 text-amber-200/90 backdrop-blur-sm" },
  INTENSIVE: { badgeLabel: COURSE_KIND_LABELS.INTENSIVE, badgeClass: "bg-teal-950/55 text-teal-200/90 backdrop-blur-sm" },
  MARATHON: { badgeLabel: COURSE_KIND_LABELS.MARATHON, badgeClass: "bg-cyan-950/55 text-cyan-200/90 backdrop-blur-sm" },
};

/**
 * Хелпер для каталожного бейджа: `{ label, class }` для всех типов курса.
 */
export function getCourseKindBadge(kind: CourseKind): { label: string; class: string } {
  const visual = COURSE_KIND_VISUALS[kind];
  return { label: visual.badgeLabel, class: visual.badgeClass };
}
