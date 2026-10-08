"use client";

import { useQuery } from "@tanstack/react-query";
import { courseCurriculumQueryOptions } from "@/entities/course";
import type { CurriculumItemDto, CurriculumSectionDto } from "@/entities/course";
import { courseLearningStateQueryOptions } from "@/entities/course-progress";
import type { CourseLearningStateDto } from "@/entities/course-progress";
import {
  CourseAccessNotice,
  CurriculumSectionCard,
  useResolvedCourseAccess,
} from "@/features/course-learning";
import { BookmarkStatusProvider } from "@/entities/bookmark";
import { useCourseId, useCourseSlug } from "@/shared/providers/course-id-provider";
import { cn } from "@/shared/lib/css";
import { ProgressDial } from "@/shared/ui/components/progress-dial";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useRef } from "react";
import { ItemBookmarkButton } from "../_components/item-bookmark-button";

type IssueFilter = "all" | "open" | "done";

const FILTER_VALUES: IssueFilter[] = ["all", "open", "done"];

function isIssueCompleted(
  itemId: string,
  learningState: CourseLearningStateDto | null | undefined,
): boolean {
  return learningState?.issues.find((i) => i.issueId === itemId)?.status === "COMPLETED";
}

export default function CourseAssignmentsPage() {
  const courseId = useCourseId();
  const courseSlug = useCourseSlug();
  const searchParams = useSearchParams();
  const pathname = usePathname();
  const router = useRouter();

  const { data: curriculum, isLoading } = useQuery(courseCurriculumQueryOptions(courseId));
  const access = useResolvedCourseAccess(courseId, curriculum?.authorId);
  const { data: learningState, isLoading: learningStateLoading } = useQuery({
    ...courseLearningStateQueryOptions(courseId),
    enabled: access.isAuthenticated && !!courseId,
  });

  const requestedSectionId = searchParams.get("section");
  const scrolledForSectionRef = useRef<string | null>(null);

  useEffect(() => {
    if (!requestedSectionId) return;
    if (scrolledForSectionRef.current === requestedSectionId) return;
    if (isLoading) return;
    const el = document.getElementById(`curriculum-section-${requestedSectionId}`);
    if (!el) return;
    el.scrollIntoView({ behavior: "smooth", block: "start" });
    scrolledForSectionRef.current = requestedSectionId;
  }, [requestedSectionId, isLoading]);

  const rawFilter = searchParams.get("filter") as IssueFilter | null;
  const filter: IssueFilter = rawFilter && FILTER_VALUES.includes(rawFilter) ? rawFilter : "all";

  const setFilter = (next: IssueFilter) => {
    const params = new URLSearchParams(searchParams.toString());
    if (next === "all") params.delete("filter");
    else params.set("filter", next);
    const qs = params.toString();
    router.replace(`${pathname}${qs ? `?${qs}` : ""}`, { scroll: false });
  };

  const waitingForProgress = access.isAuthenticated && learningStateLoading;
  if (isLoading || waitingForProgress) {
    return (
      <div className="max-w-6xl mx-auto px-4 py-8 md:px-8 space-y-6">
        <div className="space-y-2">
          <Skeleton className="h-10 w-56" />
          <Skeleton className="h-4 w-72" />
        </div>
        <div className="space-y-3 pt-4">
          <Skeleton className="h-12 w-full max-w-md rounded-full" />
          <Skeleton className="h-16 rounded-2xl" />
          <Skeleton className="h-16 rounded-2xl" />
        </div>
      </div>
    );
  }

  if (!curriculum) return null;

  const projectSections = curriculum.sections.filter((s) => s.itemType !== "Module");
  const totalIssues = projectSections.reduce(
    (sum, s) => sum + s.items.filter((i) => i.itemType === "Issue").length,
    0,
  );
  const completedItems = learningState?.issues.filter((i) => i.status === "COMPLETED").length ?? 0;
  const openItems = totalIssues - completedItems;
  const overallPercent = totalIssues > 0 ? Math.round((completedItems / totalIssues) * 100) : 0;

  const filteredSections: CurriculumSectionDto[] = projectSections
    .map((section) => {
      const items = section.items.filter((i) => {
        if (i.itemType !== "Issue") return false;
        if (filter === "all") return true;
        const done = isIssueCompleted(i.id, learningState);
        return filter === "done" ? done : !done;
      });
      return { ...section, items };
    })
    .filter((s) => s.items.length > 0);

  // Секции свёрнуты по умолчанию (#551) — авто-раскрытие «активного» раздела по
  // прогрессу убрано. Открываем только явный deep-link `?section=<id>` (крошки
  // проекта, routes.courseProject) — он же скроллится useEffect'ом выше.
  const requestedSectionMatches =
    requestedSectionId && filteredSections.some((s) => s.id === requestedSectionId);
  const openSectionId = requestedSectionMatches ? requestedSectionId : null;

  const bookmarkTargets = access.isAuthenticated
    ? projectSections.flatMap((s) =>
        s.items
          .filter((i) => i.itemType === "Issue")
          .map((i) => ({
            courseId,
            entityType: "Issue" as const,
            entityId: i.id,
          })),
      )
    : [];

  const renderTrailing = access.isAuthenticated
    ? (item: CurriculumItemDto) => (
        <ItemBookmarkButton
          courseId={courseId}
          entityType={item.itemType === "Issue" ? "Issue" : "Material"}
          entityId={item.id}
        />
      )
    : undefined;

  const showDial = access.isAuthenticated && learningState !== undefined && totalIssues > 0;

  const stats = [
    { value: projectSections.length, label: "проектов" },
    { value: totalIssues, label: "заданий" },
  ];

  const tabs: Array<{ value: IssueFilter; label: string; count: number }> = [
    { value: "all", label: "Все задания", count: totalIssues },
    { value: "open", label: "Невыполненные", count: openItems },
    { value: "done", label: "Выполненные", count: completedItems },
  ];

  return (
    <BookmarkStatusProvider items={bookmarkTargets}>
      <div className="max-w-6xl mx-auto px-4 py-8 md:px-8 pb-16">
        <header className="mb-5 md:mb-6">
          <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
            <div className="min-w-0">
              <h1 className="text-2xl md:text-3xl font-bold tracking-tight text-foreground">
                Задания
              </h1>
              <p className="mt-1.5 text-sm text-muted-foreground tabular-nums">
                {projectSections.length} {projectSections.length === 1 ? "проект" : "проектов"}
                <span className="mx-2 text-muted-foreground/40">·</span>
                {totalIssues} {totalIssues === 1 ? "задание" : "заданий"}
              </p>
            </div>

            <dl className="flex items-stretch divide-x divide-border/50 rounded-xl border border-border/50 bg-card/30 shrink-0">
              {stats.map((s) => (
                <div
                  key={s.label}
                  className="flex flex-col items-center justify-center px-4 py-2 sm:px-5 sm:py-2.5 min-w-[76px]"
                >
                  <dt className="order-2 mt-0.5 text-[10px] uppercase tracking-[0.14em] text-muted-foreground/70">
                    {s.label}
                  </dt>
                  <dd className="order-1 text-lg sm:text-xl font-bold tabular-nums leading-none text-foreground">
                    {s.value}
                  </dd>
                </div>
              ))}
              {showDial && (
                <div className="flex items-center justify-center px-3 sm:px-4">
                  <ProgressDial percent={overallPercent} size={56} strokeWidth={5} />
                </div>
              )}
            </dl>
          </div>
        </header>

        <CourseAccessNotice accessLevel={access.accessLevel} className="mb-4" />

        {totalIssues > 0 && (
          <div role="tablist" className="mb-3 flex flex-wrap items-center gap-1.5">
            {tabs.map((tab) => {
              const active = filter === tab.value;
              return (
                <button
                  key={tab.value}
                  type="button"
                  role="tab"
                  aria-selected={active}
                  onClick={() => setFilter(tab.value)}
                  className={cn(
                    "inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs sm:text-sm font-medium border transition-colors",
                    active
                      ? "bg-primary/15 border-primary/40 text-primary"
                      : "bg-card/30 border-border/50 text-muted-foreground hover:text-foreground hover:bg-muted/40",
                  )}
                >
                  {tab.label}
                  <span
                    className={cn(
                      "tabular-nums text-[10px] sm:text-[11px] px-1.5 py-px rounded-full",
                      active
                        ? "bg-primary/25 text-primary"
                        : "bg-muted/60 text-muted-foreground/80",
                    )}
                  >
                    {tab.count}
                  </span>
                </button>
              );
            })}
          </div>
        )}

        {projectSections.length === 0 ? (
          <p className="text-sm text-muted-foreground text-center py-10">Нет заданий</p>
        ) : filteredSections.length === 0 ? (
          <p className="text-sm text-muted-foreground text-center py-10">
            {filter === "done" ? "Нет выполненных заданий" : "Нет невыполненных заданий"}
          </p>
        ) : (
          <div className="space-y-2">
            {filteredSections.map((section, index) => (
              <CurriculumSectionCard
                key={section.id}
                section={section}
                sectionNumber={index + 1}
                sectionKind="project"
                itemFilter="issue"
                learningState={learningState}
                accessLevel={access.accessLevel}
                courseSlug={courseSlug}
                defaultOpen={section.id === openSectionId}
                currentPath={pathname}
                renderItemTrailing={renderTrailing}
              />
            ))}
          </div>
        )}
      </div>
    </BookmarkStatusProvider>
  );
}
