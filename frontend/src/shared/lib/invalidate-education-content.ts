import type { QueryClient } from "@tanstack/react-query";

// Six ECS base keys — courses, modules, materials, issues, projects, collections.
// Kept literal here (instead of importing entity baseKeys) so this helper can
// live in `shared/` without violating FSD layer boundaries.
const ECS_BASE_KEYS = [
  "courses",
  "modules",
  "materials",
  "issues",
  "projects",
  "collections",
] as const;

/**
 * Coarse ECS-domain invalidator (courses, modules, materials, issues, projects,
 * collections). After any author-side mutation inside a course, re-bake all
 * six trees.
 *
 * Why coarse: эти сущности перекрёстно отображаются друг через друга (issue
 * внутри module внутри course-builder, material в коллекции, project в
 * roadmap), и точечная инвалидация по {courseId/moduleId} стабильно
 * протекает — копится регрессия. Цена широкой инвалидации для author UI
 * низкая, refetch'и фоновые.
 *
 * Возвращает `Promise<void>` — мутации должны делать `await invalidate...`
 * в `onSuccess`, чтобы refetch успел стартовать до закрытия sheet/dialog
 * (иначе query становится `enabled: false` и инвалидация не делает refetch
 * до следующего открытия).
 */
export function invalidateEducationContent(qc: QueryClient): Promise<void> {
  return Promise.all(
    ECS_BASE_KEYS.map((key) => qc.invalidateQueries({ queryKey: [key] })),
  ).then(() => undefined);
}
