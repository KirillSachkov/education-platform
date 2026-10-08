import type { CurriculumSectionDto } from "@/entities/course";
import type { CourseLearningStateDto } from "@/entities/course-progress";
import { findActiveSectionId } from "./active-section";

export interface ProgramPreviewSection {
  section: CurriculumSectionDto;
  sectionNumber: number;
  isActive: boolean;
}

function findLastPositionSectionId(
  modules: CurriculumSectionDto[],
  learningState: CourseLearningStateDto | null | undefined,
): string | null {
  const lastPositionId = learningState?.lastPosition?.entityId;
  if (!lastPositionId) return null;

  return (
    modules.find((section) => section.items.some((item) => item.id === lastPositionId))?.id ?? null
  );
}

/**
 * Все модули курса по порядку для блока «Программа курса» на обзоре (#662). Раньше
 * возвращалось «окно» из ≤3 модулей вокруг активного — из-за этого ученик видел
 * непонятные «случайные 3 модуля». Теперь отдаём ВЕСЬ список с модуля 01; пагинацию
 * («первые N + Показать ещё») делает UI-компонент `CourseProgramPreview`. `isActive`
 * помечает модуль с last-position (иначе — первый незавершённый): он раскрывается по
 * умолчанию, если попадает в видимую часть.
 */
export function selectProgramPreviewSections(
  sections: CurriculumSectionDto[],
  learningState?: CourseLearningStateDto | null,
): ProgramPreviewSection[] {
  const modules = sections.filter((section) => section.itemType === "Module");
  if (modules.length === 0) return [];

  const activeSectionId =
    findLastPositionSectionId(modules, learningState) ??
    findActiveSectionId(modules, "all", learningState);

  return modules.map((section, index) => ({
    section,
    sectionNumber: index + 1,
    isActive: !!activeSectionId && section.id === activeSectionId,
  }));
}
