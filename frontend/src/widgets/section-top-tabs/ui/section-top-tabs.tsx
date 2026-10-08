"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useRoles } from "@/shared/auth";
import { adminNav, canViewNavItem, teachingNav, type NavItem } from "@/shared/config/app-navigation";
import { scrollFadeMask, useScrollAffordance } from "@/shared/hooks";
import { cn } from "@/shared/lib/css";

/**
 * Mobile-only sticky tab bar mirroring the desktop `AppSidebar` author/admin
 * nav groups. On phones the sidebar collapses to a Sheet and the global bottom
 * bar only covers learning routes — so `/author/*` and `/admin/*` had no way to
 * switch sections. These tabs fill that gap, the same role `CourseTopTabs`
 * plays for course pages.
 *
 * Returns null outside author/admin: learning routes already live in the bottom
 * bar, course pages render their own `CourseTopTabs`. Hidden on `md+` — desktop
 * has the sidebar.
 */
export function AppSectionTopTabs() {
  const pathname = usePathname();
  const { isAtLeast, hasAnyRole } = useRoles();

  const section = resolveSection(pathname);
  const visible = section
    ? section.items.filter((item) => canViewNavItem(item, { isAtLeast, hasAnyRole }))
    : [];
  // Key on visible.length too: role-gated tabs appear only after `useRoles`
  // resolves async, a re-render that doesn't change `pathname` — without this
  // the active tab never gets centred once it finally renders.
  const { ref, atStart, atEnd } = useScrollAffordance<HTMLUListElement>(
    `${pathname}|${visible.length}`,
  );

  if (!section || visible.length === 0) return null;

  return (
    <nav
      className={cn(
        "md:hidden sticky top-0 z-30",
        "border-b border-border/40 bg-background/95 backdrop-blur",
        "supports-[backdrop-filter]:bg-background/80",
      )}
      aria-label={section.label}
    >
      <ul
        ref={ref}
        style={scrollFadeMask(atStart, atEnd)}
        className="flex gap-1 overflow-x-auto px-2 py-1 scrollbar-none"
      >
        {visible.map((tab) => {
          const isActive = pathname === tab.href || pathname.startsWith(`${tab.href}/`);
          return (
            <li key={tab.href} className="shrink-0">
              <Link
                href={tab.href}
                data-active={isActive ? "true" : undefined}
                className={cn(
                  // ≥44px tap target per web.dev / Apple HIG touch guidance.
                  "relative inline-flex h-11 min-h-[44px] items-center px-3 text-sm font-medium",
                  "rounded-md transition-colors",
                  // Visible keyboard focus — color-only highlight isn't enough on
                  // dark themes where muted text already approaches primary's hue.
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
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

function resolveSection(pathname: string): { items: NavItem[]; label: string } | null {
  if (pathname.startsWith("/admin")) {
    return { items: adminNav, label: "Навигация: администрирование" };
  }
  if (pathname.startsWith("/author")) {
    return { items: teachingNav, label: "Навигация: преподавание" };
  }
  return null;
}
