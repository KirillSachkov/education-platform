"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { courseCurriculumQueryOptions } from "@/entities/course";
import { useResolvedCourseAccess } from "@/features/course-learning";
import { scrollFadeMask, useScrollAffordance } from "@/shared/hooks";
import { routes } from "@/shared/config/routes";
import { useCourseId, useCourseSlug } from "@/shared/providers/course-id-provider";
import { cn } from "@/shared/lib/css";

type TopTab = {
  href: string;
  label: string;
  exact?: boolean;
  /** Tab visible only to users with an active enrollment (matches sidebar gating). */
  enrolledOnly?: boolean;
};

/**
 * Mobile-only sticky tab bar under the course header. Mirrors the desktop
 * `CourseSidebar` nav so students can jump between Обзор / Программа /
 * Задания / Тесты / Материалы / Закладки without opening any drawer.
 *
 * Hidden on `md+` — desktop has the sidebar.
 *
 * The FAB (`MobileCurriculumFab`) stays as a separate surface: top-tabs jump
 * between course sections; FAB jumps directly into a specific lesson inside
 * the curriculum tree.
 */
export function CourseTopTabs() {
  const pathname = usePathname();
  const courseSlug = useCourseSlug();
  const courseId = useCourseId();

  const { data: curriculum } = useQuery(courseCurriculumQueryOptions(courseId));
  const access = useResolvedCourseAccess(courseId, curriculum?.authorId);

  const tabs: TopTab[] = [
    { href: routes.courseOverview(courseSlug), label: "Обзор", exact: true },
    { href: routes.courseProgram(courseSlug), label: "Программа" },
    { href: routes.courseAssignments(courseSlug), label: "Задания" },
    { href: routes.courseTests(courseSlug), label: "Тесты" },
    { href: routes.courseKnowledgeBase(courseSlug), label: "Материалы" },
    { href: routes.courseBookmarks(courseSlug), label: "Закладки", enrolledOnly: true },
  ];

  const visible = tabs.filter((t) => !t.enrolledOnly || access.hasActiveEnrollment);
  // Key on visible.length too — the enrolled-only tab appears after access
  // resolves async (pathname unchanged), so centering must re-run then.
  const { ref, atStart, atEnd } = useScrollAffordance<HTMLUListElement>(
    `${pathname}|${visible.length}`,
  );

  return (
    <nav
      className={cn(
        "md:hidden sticky top-0 z-30",
        "border-b border-border/40 bg-background/95 backdrop-blur",
        "supports-[backdrop-filter]:bg-background/80",
      )}
      aria-label="Навигация по курсу"
    >
      <ul
        ref={ref}
        style={scrollFadeMask(atStart, atEnd)}
        className="flex gap-1 overflow-x-auto px-2 py-1 scrollbar-none"
      >
        {visible.map((tab) => {
          const isActive = tab.exact ? pathname === tab.href : pathname.startsWith(tab.href);
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
