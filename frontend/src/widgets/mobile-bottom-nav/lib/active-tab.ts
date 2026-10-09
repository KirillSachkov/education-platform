import { routes } from "@/shared/config/routes";

export type BottomNavTabId = "home" | "saved" | "menu";
export interface BottomNavTabDef {
  id: BottomNavTabId;
  href: string | null;
  label: string;
  match: (pathname: string) => boolean;
}

export const BOTTOM_NAV_TABS: readonly BottomNavTabDef[] = [
  {
    id: "home",
    href: routes.home,
    label: "Моё обучение",
    match: (path) =>
      path === routes.home ||
      path.startsWith(`${routes.courses}/`) ||
      path.startsWith(`${routes.knowledgeBase}/`) ||
      path.startsWith(`${routes.collections}/`) ||
      path.startsWith("/quizzes/"),
  },
  {
    id: "saved",
    href: routes.saved,
    label: "Закладки",
    match: (path) => path === routes.saved || path.startsWith(`${routes.saved}/`),
  },
  {
    id: "menu",
    href: null,
    label: "Меню",
    match: (path) =>
      [
        routes.pricing,
        routes.myPlans,
        routes.payments,
        routes.profile,
        routes.settings,
        routes.notifications,
        "/author",
        "/admin",
      ].some((prefix) => path === prefix || path.startsWith(`${prefix}/`)),
  },
];

export function resolveActiveTabIndex(pathname: string): number {
  return BOTTOM_NAV_TABS.findIndex((tab) => tab.match(pathname));
}
