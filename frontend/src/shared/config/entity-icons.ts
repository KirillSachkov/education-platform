import { Icons } from "@/shared/ui/icons";

/**
 * @deprecated Use `Icons` from `@/shared/ui/icons` directly:
 * `Icons.course`, `Icons.lesson`, `Icons.module`, etc.
 *
 * This re-export exists for back-compat with older imports. New code MUST use
 * the central `Icons` registry — see `src/shared/ui/icons/index.ts`.
 */
export const ENTITY_ICONS = {
  course: Icons.course,
  lesson: Icons.lesson,
  issue: Icons.issue,
  article: Icons.article,
  module: Icons.module,
  project: Icons.project,
} as const;

/** Единый источник лейблов для сущностей */
export const ENTITY_LABELS = {
  course: "Курс",
  lesson: "Урок",
  issue: "Задача",
  article: "Статья",
  module: "Модуль",
  project: "Проект",
} as const;

/** Каноничные цвета сущностей (text-*) */
export const ENTITY_COLORS = {
  course: "text-purple",
  module: "text-blue",
  lesson: "text-teal",
  issue: "text-orange",
  article: "text-blue",
  project: "text-orange",
} as const;

/** Фоновые цвета для иконок сущностей (роадмап, бейджи) */
export const ENTITY_BG_COLORS = {
  course: "bg-purple-500",
  module: "bg-blue-500",
  lesson: "bg-teal-500",
  issue: "bg-orange-500",
  article: "bg-blue-500",
  project: "bg-orange-500",
} as const;

/** Цвета границ карточек сущностей */
export const ENTITY_BORDER_COLORS = {
  course: "border-purple-500/20 hover:border-purple-500/40",
  module: "border-blue-500/20 hover:border-blue-500/40",
  lesson: "border-teal-500/20 hover:border-teal-500/40",
  issue: "border-orange-500/20 hover:border-orange-500/40",
  article: "border-blue-500/20 hover:border-blue-500/40",
  project: "border-orange-500/20 hover:border-orange-500/40",
} as const;
