import type { CourseCurriculumDto, CurriculumItemDto, CurriculumSectionDto } from "../types";
import type { AccessType } from "@/shared/config/access-type";

export interface LearningNavigationItem {
  id: string;
  title: string;
  itemType: "Material" | "Issue" | "Quiz";
  sectionId: string;
  sectionTitle: string;
  sectionType: string;
  moduleId: string | null;
  accessType: AccessType | null;
}

function mapCurriculumItem(
  section: CurriculumSectionDto,
  item: CurriculumItemDto,
): LearningNavigationItem | null {
  // Quizzes are part of the program order too — excluding them made "previous"
  // skip over a quiz that sits between two lessons and left the quiz page with
  // no footer (it had no neighbours). Issues-only navigation stays a separate
  // stream (getAdjacentIssues), so quizzes never leak there.
  if (item.itemType !== "Material" && item.itemType !== "Issue" && item.itemType !== "Quiz") {
    return null;
  }

  return {
    id: item.id,
    title: item.title,
    itemType: item.itemType,
    sectionId: section.id,
    sectionTitle: section.title,
    sectionType: section.itemType,
    moduleId: section.itemType === "Module" ? section.id : null,
    accessType: item.accessType,
  };
}

export function getLearningNavigationItems(
  curriculum: CourseCurriculumDto | null | undefined,
): LearningNavigationItem[] {
  if (!curriculum) {
    return [];
  }

  const seen = new Set<string>();
  return curriculum.sections.flatMap((section) =>
    section.items
      .map((item) => mapCurriculumItem(section, item))
      .filter((item): item is LearningNavigationItem => {
        if (item === null || seen.has(item.id)) return false;
        seen.add(item.id);
        return true;
      }),
  );
}

/**
 * Returns prev/next navigation items scoped by section type.
 *
 * - If the current item is in a Module section → navigate through all Module
 *   sections' items (across modules, in curriculum order).
 * - If the current item is in a Project section → navigate through that
 *   specific project's items only.
 *
 * Pass `sectionId` (from `?section` query param) to disambiguate items
 * that appear in multiple sections.
 */
export function getAdjacentLearningItems(
  curriculum: CourseCurriculumDto | null | undefined,
  itemId: string,
  sectionId?: string | null,
) {
  const allItems = getLearningNavigationItems(curriculum);

  // Determine which section the current item belongs to
  let currentItem = sectionId
    ? allItems.find((item) => item.id === itemId && item.sectionId === sectionId)
    : null;
  currentItem ??= allItems.find((item) => item.id === itemId) ?? null;

  // Filter items by section type context
  let items: LearningNavigationItem[];
  if (!currentItem) {
    items = allItems;
  } else if (currentItem.sectionType === "Module") {
    // Navigate through all module items in order
    items = allItems.filter((item) => item.sectionType === "Module");
  } else {
    // Navigate within the same project section
    items = allItems.filter((item) => item.sectionId === currentItem.sectionId);
  }

  const currentIndex = items.findIndex((item) => item.id === itemId);

  return {
    items,
    currentIndex,
    currentItem: currentIndex >= 0 ? items[currentIndex] : null,
    previousItem: currentIndex > 0 ? items[currentIndex - 1] : null,
    nextItem: currentIndex >= 0 && currentIndex < items.length - 1 ? items[currentIndex + 1] : null,
  };
}

/**
 * Returns prev/next issues across the whole curriculum (issues-only stream).
 *
 * Used on the issue page to let a student jump between tasks while skipping
 * intermediate materials. The same issue may appear in multiple sections
 * (Module + Project), so we dedupe by issue id and keep the first occurrence
 * in curriculum order.
 */
export function getAdjacentIssues(
  curriculum: CourseCurriculumDto | null | undefined,
  issueId: string,
) {
  const allItems = getLearningNavigationItems(curriculum);
  const issues = allItems.filter((item) => item.itemType === "Issue");
  const currentIndex = issues.findIndex((item) => item.id === issueId);

  return {
    items: issues,
    currentIndex,
    currentItem: currentIndex >= 0 ? issues[currentIndex] : null,
    previousItem: currentIndex > 0 ? issues[currentIndex - 1] : null,
    nextItem:
      currentIndex >= 0 && currentIndex < issues.length - 1 ? issues[currentIndex + 1] : null,
  };
}
