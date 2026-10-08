"use client";

import { scrollFadeMask, useScrollAffordance } from "@/shared/hooks";
import { cn } from "@/shared/lib/css";
import type { ActiveTab } from "../model/use-course-builder";

/**
 * Mobile-only sticky tab bar for the course builder. On phones the builder's
 * section nav lives in the sidebar Sheet (`CourseBuilderSidebar`) — there was no
 * inline way to switch between Модули / Проекты / … without opening the burger.
 * This mirrors the same role `AppSectionTopTabs` plays for `/author`/`/admin`
 * and `CourseTopTabs` for course pages: a horizontally scrollable `md:hidden`
 * bar wired to the same `activeTab` state.
 *
 * Excludes «Студенты» (separate tab reworked in !292), «Роадмап» (a distinct
 * route) and «Превью» (vestigial `ActiveTab` value — not a builder panel on
 * desktop either). Hidden on `md+` — desktop uses the sidebar.
 */
const TABS: {
  id: Extract<
    ActiveTab,
    "modules" | "projects" | "materials" | "collections" | "landing" | "statistics" | "settings"
  >;
  label: string;
}[] = [
  { id: "modules", label: "Модули" },
  { id: "projects", label: "Проекты" },
  { id: "materials", label: "Материалы" },
  { id: "collections", label: "Подборки" },
  { id: "landing", label: "Лендинг" },
  { id: "statistics", label: "Статистика" },
  { id: "settings", label: "Настройки" },
];

interface CourseBuilderMobileTabsProps {
  activeTab: ActiveTab;
  onTabChange: (tab: ActiveTab) => void;
}

export function CourseBuilderMobileTabs({ activeTab, onTabChange }: CourseBuilderMobileTabsProps) {
  const { ref, atStart, atEnd } = useScrollAffordance<HTMLUListElement>(activeTab);

  return (
    <nav
      className={cn(
        "md:hidden sticky top-[49px] z-10",
        "border-b border-border/40 bg-background/95 backdrop-blur",
        "supports-[backdrop-filter]:bg-background/80",
      )}
      aria-label="Разделы курса"
    >
      <ul
        ref={ref}
        style={scrollFadeMask(atStart, atEnd)}
        className="flex gap-1 overflow-x-auto px-2 py-1 scrollbar-none"
      >
        {TABS.map((tab) => {
          const isActive = activeTab === tab.id;
          return (
            <li key={tab.id} className="shrink-0">
              <button
                type="button"
                onClick={() => onTabChange(tab.id)}
                data-active={isActive ? "true" : undefined}
                className={cn(
                  // ≥44px tap target per web.dev / Apple HIG touch guidance.
                  "relative inline-flex h-11 min-h-[44px] items-center px-3 text-sm font-medium",
                  "rounded-md transition-colors",
                  "outline-none focus-visible:ring-2 focus-visible:ring-ring/60",
                  isActive
                    ? "text-primary"
                    : "text-muted-foreground hover:text-foreground active:text-foreground",
                )}
                aria-current={isActive ? "page" : undefined}
              >
                {tab.label}
                {isActive && (
                  <span
                    aria-hidden
                    className="absolute inset-x-2 -bottom-px h-0.5 rounded-full bg-primary"
                  />
                )}
              </button>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
