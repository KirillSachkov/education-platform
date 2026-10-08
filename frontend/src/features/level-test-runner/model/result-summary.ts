import type { LevelTestSectionScoreDto } from "@/entities/level-test";
import { routes } from "@/shared/config/routes";

/**
 * Чистые помощники страницы результата: выбор CTA рекомендации и резолв
 * заголовков слабых секций. Вынесены из UI ради unit-тестов. Issue #481.
 */

export interface LevelTestRecommendationCta {
  kind: "course" | "catalog";
  href: string;
}

/**
 * CTA блока рекомендаций: есть `recommendedCourseId` И курс зарезолвлен в slug →
 * ведём на страницу курса; иначе (нет рекомендации / курс не загрузился) —
 * в каталог «Усиль слабые места — выбери курс».
 */
export function resolveRecommendationCta({
  recommendedCourseId,
  courseSlug,
}: {
  recommendedCourseId: string | null;
  courseSlug: string | null;
}): LevelTestRecommendationCta {
  if (recommendedCourseId && courseSlug) {
    return { kind: "course", href: routes.courseOverview(courseSlug) };
  }
  return { kind: "catalog", href: routes.courses };
}

/**
 * «Слабые места: <titles>» — маппит `weakestSectionKeys` в заголовки секций.
 * Неизвестный ключ (рассинхрон снапшота) — показываем сам ключ, не теряем.
 */
export function resolveWeakestSectionTitles(
  sections: ReadonlyArray<Pick<LevelTestSectionScoreDto, "key" | "title">>,
  weakestSectionKeys: readonly string[],
): string[] {
  return weakestSectionKeys.map(
    (key) => sections.find((section) => section.key === key)?.title ?? key,
  );
}
