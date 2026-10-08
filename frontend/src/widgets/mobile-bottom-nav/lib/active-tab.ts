import { routes } from "@/shared/config/routes";

/**
 * Single source of truth for matching the current route to a bottom-nav tab.
 * Both `MobileBottomNav` (to light up the active pill) and the page-transition
 * wrapper (to derive slide direction by index delta) call this — keep them in
 * sync by going through `BOTTOM_NAV_TABS` instead of declaring match rules twice.
 */

const AUTHOR_SPACE_HOME_RE = /^\/@[^/]+\/?(home\/?)?$/;
const AUTHOR_COURSES_RE = /^\/@[^/]+\/courses(\/|$)/;
const AUTHOR_KB_RE = /^\/@[^/]+\/(knowledge-base|collections)(\/|$)/;
const COURSE_KB_RE = /^\/@[^/]+\/courses\/[^/]+\/(knowledge-base|collections|bookmarks)(\/|$)/;

function isHome(pathname: string): boolean {
  if (pathname === routes.home || pathname.startsWith(`${routes.home}/`)) {
    return true;
  }
  // Author-space landing (`/@slug`, `/@slug/`, `/@slug/home`) — single-tenant
  // platform treats the space landing as the home surface. Concrete sub-routes
  // (`/@slug/courses/...`, `/@slug/knowledge-base/...`) are caught by the more
  // specific predicates below.
  return AUTHOR_SPACE_HOME_RE.test(pathname);
}

function isMyCourses(pathname: string): boolean {
  if (pathname === routes.myCourses || pathname.startsWith(`${routes.myCourses}/`)) {
    return true;
  }
  // Course-scoped KB routes (`/@author/courses/X/knowledge-base|collections|bookmarks`)
  // belong to the Knowledge Base tab — explicit yield here keeps the predicate
  // self-contained instead of relying on declaration-order to disambiguate.
  if (COURSE_KB_RE.test(pathname)) return false;
  // Course-scoped routes light up "Каталог" — material/issue detail still feels
  // like part of the course rail, not an isolated section.
  return AUTHOR_COURSES_RE.test(pathname);
}

function isKnowledgeBase(pathname: string): boolean {
  return (
    pathname === routes.knowledgeBase ||
    pathname.startsWith(`${routes.knowledgeBase}/`) ||
    AUTHOR_KB_RE.test(pathname) ||
    COURSE_KB_RE.test(pathname)
  );
}

function isSaved(pathname: string): boolean {
  // Глобальные «Закладки» (/saved) — сохранённое всех курсов. Course-scoped
  // bookmarks (`/courses/X/bookmarks`) остаются за вкладкой курса/КБ — см.
  // предикаты выше.
  return pathname === routes.saved || pathname.startsWith(`${routes.saved}/`);
}

function isMenuSection(pathname: string): boolean {
  // Всё, что живёт в шторке «Меню»: доступ/планы/платежи, аккаунт и настройки,
  // уведомления, прогресс/рейтинг, author- и admin-разделы, публичные профили.
  // На этих маршрутах подсвечивается вкладка «Меню» — пользователь видит, через
  // какую вкладку он сюда попал.
  return (
    pathname === routes.pricing ||
    pathname.startsWith(`${routes.pricing}/`) ||
    pathname === routes.myPlans ||
    pathname.startsWith(`${routes.myPlans}/`) ||
    pathname === routes.payments ||
    pathname.startsWith(`${routes.payments}/`) ||
    pathname === routes.profile ||
    pathname.startsWith(`${routes.profile}/`) ||
    pathname.startsWith(routes.settings) ||
    pathname === routes.notifications ||
    pathname.startsWith(`${routes.notifications}/`) ||
    pathname === routes.progress ||
    pathname.startsWith(`${routes.progress}/`) ||
    pathname === routes.leaderboard ||
    pathname.startsWith(`${routes.leaderboard}/`) ||
    pathname.startsWith("/author/") ||
    pathname.startsWith("/admin/") ||
    pathname.startsWith("/users/")
  );
}

export type BottomNavTabId = "courses" | "knowledge-base" | "home" | "saved" | "menu";

export interface BottomNavTabDef {
  id: BottomNavTabId;
  /** `null` — action-вкладка («Меню» открывает шторку вместо навигации). */
  href: string | null;
  label: string;
  match: (pathname: string) => boolean;
}

/**
 * Пять вкладок, «Главная» — по центру (index 2) с акцентным рендером в баре.
 * «Меню» — последняя, не навигирует: открывает `MobileMenuSheet` со всеми
 * остальными разделами (аккаунт / преподавание / админка по ролям).
 */
export const BOTTOM_NAV_TABS: readonly BottomNavTabDef[] = [
  { id: "courses", href: routes.myCourses, label: "Каталог", match: isMyCourses },
  {
    id: "knowledge-base",
    href: routes.knowledgeBase,
    label: "База знаний",
    match: isKnowledgeBase,
  },
  { id: "home", href: routes.home, label: "Главная", match: isHome },
  { id: "saved", href: routes.saved, label: "Закладки", match: isSaved },
  { id: "menu", href: null, label: "Меню", match: isMenuSection },
] as const;

/**
 * Returns the index of the matched tab or `-1` if no tab claims this pathname
 * (e.g. /login, /onboarding/* — bottom nav is hidden on those routes anyway).
 * Predicates are evaluated in declaration order; the first match wins.
 */
export function resolveActiveTabIndex(pathname: string): number {
  for (let i = 0; i < BOTTOM_NAV_TABS.length; i++) {
    if (BOTTOM_NAV_TABS[i].match(pathname)) return i;
  }
  return -1;
}
