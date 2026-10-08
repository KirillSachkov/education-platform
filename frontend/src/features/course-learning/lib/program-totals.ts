import type { CurriculumCollectionDto, CurriculumSectionDto } from "@/entities/course";

export interface ProgramTotals {
  totalMaterials: number;
  totalIssues: number;
  totalQuizzes: number;
  totalItems: number;
  /** id материалов, входящих в счётчики (модули + подборки) — для подсчёта completed. */
  materialIds: Set<string>;
  issueIds: Set<string>;
  quizIds: Set<string>;
}

/**
 * Счётчики страницы «Программа курса»: элементы модулей + материалы подборок (#522).
 * Подборки (#508) — отдельный блок под модулями, но их материалы учитываются в общем
 * прогрессе курса (blueprint #496), поэтому входят и в счётчики страницы. Материал
 * может лежать и в модуле, и в подборке — total'ы считаются от Set'ов (дедуп по id).
 * Контракт: caller сам передаёт только Module-секции (и фильтрует их по freeOnly) —
 * функция считает всё, что получила, без проверки section.itemType.
 */
export function computeProgramTotals(
  moduleSections: CurriculumSectionDto[],
  collections: CurriculumCollectionDto[],
): ProgramTotals {
  const materialIds = new Set<string>();
  const issueIds = new Set<string>();
  const quizIds = new Set<string>();

  for (const section of moduleSections) {
    for (const item of section.items) {
      if (item.itemType === "Material") materialIds.add(item.id);
      else if (item.itemType === "Issue") issueIds.add(item.id);
      else if (item.itemType === "Quiz") quizIds.add(item.id);
    }
  }

  for (const collection of collections) {
    for (const id of collection.materialIds) materialIds.add(id);
  }

  return {
    totalMaterials: materialIds.size,
    totalIssues: issueIds.size,
    totalQuizzes: quizIds.size,
    totalItems: materialIds.size + issueIds.size + quizIds.size,
    materialIds,
    issueIds,
    quizIds,
  };
}
