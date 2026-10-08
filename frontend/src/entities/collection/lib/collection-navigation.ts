import type { CollectionDetailDto } from "../types";

export interface CollectionNavigationItem {
  materialId: string;
  title: string;
}

/**
 * Плоский список МАТЕРИАЛОВ подборки в порядке рендера: секции и items уже
 * отсортированы бэком по sort_key, границы секций не учитываются. QUIZ-items
 * (#491) пропускаются — prev/next навигация ходит только по материалам.
 * Повторное вхождение того же материала в другой секции схлопывается по первому
 * вхождению (иначе prev/next зацикливаются). Замокнутые материалы НЕ
 * пропускаются — замок показывает целевая страница материала (#464).
 */
export function getCollectionNavigationItems(
  collection: CollectionDetailDto | null | undefined,
): CollectionNavigationItem[] {
  if (!collection) {
    return [];
  }

  const seen = new Set<string>();
  return collection.sections.flatMap((section) =>
    section.items
      .filter((item) => item.itemType !== "QUIZ" && item.material != null)
      .map((item) => ({ materialId: item.referenceId, title: item.material!.title }))
      .filter((item) => {
        if (seen.has(item.materialId)) return false;
        seen.add(item.materialId);
        return true;
      }),
  );
}

/**
 * Prev/next соседи материала внутри подборки (плоский порядок, сквозь границы
 * секций). `currentItem === null` ⇒ материала в подборке нет (или подборка ещё
 * не загружена) — навигацию рендерить не нужно.
 */
export function getAdjacentCollectionMaterials(
  collection: CollectionDetailDto | null | undefined,
  materialId: string,
) {
  const items = getCollectionNavigationItems(collection);
  const currentIndex = items.findIndex((item) => item.materialId === materialId);

  return {
    items,
    currentIndex,
    currentItem: currentIndex >= 0 ? items[currentIndex] : null,
    previousItem: currentIndex > 0 ? items[currentIndex - 1] : null,
    nextItem: currentIndex >= 0 && currentIndex < items.length - 1 ? items[currentIndex + 1] : null,
  };
}
