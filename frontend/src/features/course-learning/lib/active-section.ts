import type { CurriculumSectionDto } from "@/entities/course";
import type { CourseLearningStateDto } from "@/entities/course-progress";

type ItemFilter = "material" | "issue" | "all";

/**
 * Ищем секцию, внутри которой есть ближайший незавершённый элемент выбранного
 * типа. Раскрываем только её — остальные остаются свёрнутыми, чтобы ученик не
 * тонул в стене из 29 модулей на одном экране.
 */
export function findActiveSectionId(
  sections: CurriculumSectionDto[],
  itemFilter: ItemFilter,
  learningState: CourseLearningStateDto | null | undefined,
): string | null {
  // Без learningState не можем решить, какая секция активная. Возвращаем null,
  // иначе пустая карта статусов выдаёт первую попавшуюся секцию по ошибке
  // (все статусы считаются не-VIEWED / не-COMPLETED).
  if (!learningState) return null;

  const materialStatuses = new Map(learningState.materials.map((m) => [m.materialId, m.status]));
  const issueStatuses = new Map(learningState.issues.map((i) => [i.issueId, i.status]));
  const passedQuizIds = new Set(learningState.passedQuizIds ?? []);

  for (const section of sections) {
    for (const item of section.items) {
      const isMaterialMatch =
        item.itemType === "Material" && (itemFilter === "material" || itemFilter === "all");
      if (isMaterialMatch && materialStatuses.get(item.id) !== "VIEWED") return section.id;

      const isIssueMatch =
        item.itemType === "Issue" && (itemFilter === "issue" || itemFilter === "all");
      if (isIssueMatch && issueStatuses.get(item.id) !== "COMPLETED") return section.id;

      // Квиз «завершён» = есть passed-попытка (ST-16 #495).
      const isQuizMatch = item.itemType === "Quiz" && itemFilter === "all";
      if (isQuizMatch && !passedQuizIds.has(item.id)) return section.id;
    }
  }
  return null;
}
