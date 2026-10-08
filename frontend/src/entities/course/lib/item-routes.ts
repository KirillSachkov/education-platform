import { routes } from "@/shared/config/routes";
import type { CourseViewTab } from "../types";

function withParams(
  href: string,
  params: Record<string, string | undefined>,
) {
  const entries = Object.entries(params).filter(
    (entry): entry is [string, string] => entry[1] !== undefined,
  );
  if (entries.length === 0) {
    return href;
  }
  const query = new URLSearchParams(entries).toString();
  return `${href}?${query}`;
}

export function getCourseOverviewHref(courseSlug: string, tab?: CourseViewTab) {
  return withParams(routes.courseOverview(courseSlug), { tab });
}

export function getCourseItemHref(
  courseSlug: string,
  itemType: string,
  itemId: string,
  options: {
    tab?: CourseViewTab;
    fromIssue?: string;
  } = {},
) {
  const params = { tab: options.tab, fromIssue: options.fromIssue };

  if (itemType === "Issue") {
    return withParams(routes.courseIssue(courseSlug, itemId), params);
  }

  // Quiz-элемент модуля живёт на курсовом роуте (sidebar + breadcrumbs),
  // standalone /quizzes/{id} остаётся для подборок и прямых ссылок.
  if (itemType === "Quiz") {
    return withParams(routes.courseQuiz(courseSlug, itemId), params);
  }

  // Material (unified Lesson + Article) — default route for all non-issue learning items
  return withParams(routes.courseMaterial(courseSlug, itemId), params);
}
