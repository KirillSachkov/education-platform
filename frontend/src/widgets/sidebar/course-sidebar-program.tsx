"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { usePathname, useSearchParams } from "next/navigation";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { Icons } from "@/shared/ui/icons";
import { formatDurationSecondsHuman } from "@/shared/lib/duration";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import {
  canAccessItem,
  getCourseItemHref,
  type CourseAccessLevel,
  type CurriculumSectionDto,
} from "@/entities/course";
import { issueDetailQueryOptions } from "@/entities/issue";
import { materialDetailQueryOptions } from "@/entities/material";
import { quizQueryOptions } from "@/entities/quiz";
import type { IssueProgressStatus } from "@/entities/course-progress";
import type { MaterialProgressStatus } from "@/entities/course-progress";
import { ItemProgressIndicator } from "@/shared/ui/components/item-progress-indicator";
import { cn } from "@/shared/lib/css";
import {
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/shared/ui/kit/sidebar";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/shared/ui/kit/collapsible";

// Hover-with-intent delay: pause on item this long before we prefetch.
// Skips API hits when user is just sweeping the cursor across the list.
const HOVER_PREFETCH_DELAY_MS = 250;

function prefetchCurriculumItem(queryClient: QueryClient, itemType: string, itemId: string) {
  if (itemType === "Material") {
    queryClient.prefetchQuery(materialDetailQueryOptions(itemId));
  } else if (itemType === "Issue") {
    queryClient.prefetchQuery(issueDetailQueryOptions(itemId));
  } else if (itemType === "Quiz") {
    queryClient.prefetchQuery(quizQueryOptions.studentQuizOptions(itemId));
  }
}

function appendSectionToHref(href: string, sectionId: string) {
  const separator = href.includes("?") ? "&" : "?";
  return `${href}${separator}${new URLSearchParams({ section: sectionId }).toString()}`;
}

function getItemRawStatus(
  itemType: string,
  itemId: string,
  materialStatuses: Map<string, string>,
  issueStatuses: Map<string, string>,
  passedQuizIds: ReadonlySet<string>,
): IssueProgressStatus | MaterialProgressStatus {
  if (itemType === "Material") {
    return (materialStatuses.get(itemId) as MaterialProgressStatus) ?? "NOT_VIEWED";
  }
  // Quiz переиспользует материальную семантику индикатора: пройден ⇔ «изучен».
  if (itemType === "Quiz") {
    return passedQuizIds.has(itemId) ? "VIEWED" : "NOT_VIEWED";
  }
  return (issueStatuses.get(itemId) as IssueProgressStatus) ?? "NOT_STARTED";
}

function getSectionProgress(
  section: CurriculumSectionDto,
  hasActiveEnrollment: boolean,
  materialStatuses: Map<string, string>,
  issueStatuses: Map<string, string>,
  passedQuizIds: ReadonlySet<string>,
) {
  if (!hasActiveEnrollment) return null;

  const trackableItems = section.items.filter(
    (item) =>
      item.itemType === "Material" || item.itemType === "Issue" || item.itemType === "Quiz",
  );
  if (trackableItems.length === 0) return null;

  const completed = trackableItems.filter((item) => {
    if (item.itemType === "Material") {
      return materialStatuses.get(item.id) === "VIEWED";
    }
    if (item.itemType === "Quiz") {
      return passedQuizIds.has(item.id);
    }
    return issueStatuses.get(item.id) === "COMPLETED";
  }).length;

  return {
    completed,
    total: trackableItems.length,
    percent: Math.round((completed / trackableItems.length) * 100),
  };
}

interface CourseSidebarProgramProps {
  sections: CurriculumSectionDto[];
  materialStatuses: Map<string, string>;
  issueStatuses: Map<string, string>;
  /** Passed-квизы юзера (learning-state.passedQuizIds) — галочки quiz-элементов. */
  passedQuizIds: ReadonlySet<string>;
  hasActiveEnrollment: boolean;
  accessLevel: CourseAccessLevel;
  /**
   * Last navigated material in the course (`lastPosition.entityId` from the
   * learning-state response). When set AND the user is not currently viewing
   * that exact item, the section containing this material auto-expands and the
   * item renders a "Продолжить" pill.
   *
   * Important: must come from `lastPosition` (last OPENED), not from
   * `materials[].viewedAt` (last COMPLETED). Otherwise marking a material as
   * studied makes it the "continue" target and suppresses its strikethrough.
   */
  lastPositionMaterialId?: string | null;
  /** Called after a lesson/issue link is clicked (e.g. close the mobile drawer). */
  onItemClick?: React.MouseEventHandler<HTMLAnchorElement>;
}

export function CourseSidebarProgram({
  sections,
  materialStatuses,
  issueStatuses,
  passedQuizIds,
  hasActiveEnrollment,
  accessLevel,
  lastPositionMaterialId,
  onItemClick,
}: CourseSidebarProgramProps) {
  const queryClient = useQueryClient();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const courseSlug = useCourseSlug();

  const duplicatedItemIdCounts = new Map<string, number>();
  sections.forEach((section) => {
    section.items.forEach((item) => {
      duplicatedItemIdCounts.set(item.id, (duplicatedItemIdCounts.get(item.id) ?? 0) + 1);
    });
  });
  const duplicatedItemIds = new Set(
    [...duplicatedItemIdCounts.entries()]
      .filter(([, count]) => count > 1)
      .map(([itemId]) => itemId),
  );

  const selectedSectionId = searchParams.get("section");
  let matchedKey: string | null = null;
  let selectedKey: string | null = null;

  sections.forEach((section) => {
    section.items.forEach((item) => {
      const href = getCourseItemHref(courseSlug, item.itemType, item.id);
      if (href === pathname) {
        const itemKey = `${section.id}:${item.id}`;
        if (!matchedKey) matchedKey = itemKey;
        if (selectedSectionId === section.id) selectedKey = itemKey;
      }
    });
  });

  const activeItemKey: string | null = selectedKey ?? matchedKey;

  // Auto-scroll к активному уроку. Без этого, если урок лежит глубоко
  // в программе (например, 21-й модуль), пользователь после открытия
  // должен скроллить sidebar вручную, чтобы понять где он.
  //
  // Двойной requestAnimationFrame нужен для deep-link на урок в свёрнутой
  // секции: SectionCollapsible.useEffect[isActiveSection] делает setOpen(true),
  // что планирует второй render. Один rAF не гарантирует, что React успел
  // закоммитить этот second render и Collapsible смонтировал items в DOM —
  // ref остаётся null, скролл не происходит. Второй rAF даёт гарантированный
  // commit-цикл, после чего активный <a> в DOM.
  //
  // `block: 'nearest'` — уже видимый item не «прыгает».
  const activeAnchorRef = useRef<HTMLAnchorElement | null>(null);
  useEffect(() => {
    if (!activeItemKey) return;
    let id2: number | undefined;
    const id1 = requestAnimationFrame(() => {
      id2 = requestAnimationFrame(() => {
        activeAnchorRef.current?.scrollIntoView({ block: "nearest", behavior: "auto" });
      });
    });
    return () => {
      cancelAnimationFrame(id1);
      if (id2 !== undefined) cancelAnimationFrame(id2);
    };
  }, [activeItemKey]);

  // Resolve the section that hosts the last navigated material — used to
  // auto-expand the "continue learning" module and mark the item with a pill.
  // Skip materials already marked as VIEWED: «Продолжить» имеет смысл только
  // для незавершённого item'а; иначе подсветка путалась бы с уже изученным.
  let continueSectionId: string | null = null;
  let continueItemKey: string | null = null;
  if (lastPositionMaterialId && materialStatuses.get(lastPositionMaterialId) !== "VIEWED") {
    for (const section of sections) {
      if (section.itemType !== "Module") continue;
      const hit = section.items.find((i) => i.id === lastPositionMaterialId);
      if (hit) {
        continueSectionId = section.id;
        continueItemKey = `${section.id}:${hit.id}`;
        break;
      }
    }
  }

  return (
    <SidebarGroup className="group-data-[collapsible=icon]:hidden">
      <SidebarGroupLabel className="flex items-center justify-between">
        <span>Программа</span>
      </SidebarGroupLabel>

      <SidebarGroupContent>
        {sections.length === 0 && (
          <p className="px-3 py-3 text-xs text-muted-foreground text-center">Программа пуста</p>
        )}

        {sections.map((section, index) => {
          const progress = getSectionProgress(
            section,
            hasActiveEnrollment,
            materialStatuses,
            issueStatuses,
            passedQuizIds,
          );
          const isLocked = !section.items.some((item) =>
            canAccessItem(item.accessType, accessLevel),
          );

          return (
            <SectionCollapsible
              key={section.id}
              section={section}
              index={index}
              progress={progress}
              isLocked={isLocked}
              hasActiveEnrollment={hasActiveEnrollment}
              accessLevel={accessLevel}
              materialStatuses={materialStatuses}
              issueStatuses={issueStatuses}
              passedQuizIds={passedQuizIds}
              activeItemKey={activeItemKey}
              continueItemKey={continueItemKey}
              isContinueSection={section.id === continueSectionId}
              hasContinueSection={continueSectionId !== null}
              duplicatedItemIds={duplicatedItemIds}
              queryClient={queryClient}
              onItemClick={onItemClick}
              activeAnchorRef={activeAnchorRef}
            />
          );
        })}
      </SidebarGroupContent>
    </SidebarGroup>
  );
}

function SectionCollapsible({
  section,
  index,
  progress,
  isLocked,
  hasActiveEnrollment,
  accessLevel,
  materialStatuses,
  issueStatuses,
  passedQuizIds,
  activeItemKey,
  continueItemKey,
  isContinueSection,
  hasContinueSection,
  duplicatedItemIds,
  queryClient,
  onItemClick,
  activeAnchorRef,
}: {
  section: CurriculumSectionDto;
  index: number;
  progress: { completed: number; total: number; percent: number } | null;
  isLocked: boolean;
  hasActiveEnrollment: boolean;
  accessLevel: CourseAccessLevel;
  materialStatuses: Map<string, string>;
  issueStatuses: Map<string, string>;
  passedQuizIds: ReadonlySet<string>;
  activeItemKey: string | null;
  continueItemKey: string | null;
  isContinueSection: boolean;
  hasContinueSection: boolean;
  duplicatedItemIds: Set<string>;
  queryClient: ReturnType<typeof useQueryClient>;
  onItemClick?: React.MouseEventHandler<HTMLAnchorElement>;
  activeAnchorRef: React.RefObject<HTMLAnchorElement | null>;
}) {
  const courseSlug = useCourseSlug();
  // Hint for material/issue back-nav breadcrumbs — derived from section type.
  const sectionTab: "modules" | "projects" = section.itemType === "Module" ? "modules" : "projects";
  const isActiveSection = activeItemKey
    ? section.items.some((item) => `${section.id}:${item.id}` === activeItemKey)
    : false;
  const isSectionCompleted = progress !== null && progress.completed === progress.total;
  // Prefer the "continue" section over the fallback "first section" default
  // when both could be initially open.
  const defaultOpen =
    isActiveSection ||
    isContinueSection ||
    (index === 0 && !isSectionCompleted && !hasContinueSection);
  const [open, setOpen] = useState(defaultOpen);

  // Auto-collapse when a section flips to completed (and user isn't inside it)
  useEffect(() => {
    if (isSectionCompleted && !isActiveSection && !isContinueSection) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setOpen(false);
    }
  }, [isSectionCompleted, isActiveSection, isContinueSection]);

  // Auto-expand when navigation moves to an item inside this section
  useEffect(() => {
    if (isActiveSection) {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setOpen(true);
    }
  }, [isActiveSection]);

  // Prefetch the "Продолжить" item once. This is the most likely click target
  // when the user opens the sidebar and only costs one request, regardless of
  // how many items the curriculum has.
  useEffect(() => {
    if (!continueItemKey) return;
    const continueItem = section.items.find(
      (item) => `${section.id}:${item.id}` === continueItemKey,
    );
    if (!continueItem) return;
    if (continueItem.itemType !== "Material" && continueItem.itemType !== "Issue") return;
    prefetchCurriculumItem(queryClient, continueItem.itemType, continueItem.id);
  }, [continueItemKey, section.id, section.items, queryClient]);

  return (
    <Collapsible open={open} onOpenChange={setOpen} className={index > 0 ? "mt-0.5" : ""}>
      <CollapsibleTrigger className="flex flex-col w-full px-2 py-2 rounded-md hover:bg-sidebar-accent transition-colors text-left">
        <div className="flex items-center gap-1.5 w-full">
          {/* Chevron is always rendered so completed sections can still be expanded/collapsed.
              The completion check moves next to the count on the right. */}
          {open ? (
            <Icons.chevronDown size={12} className="text-muted-foreground/70 shrink-0" />
          ) : (
            <Icons.chevronRight size={12} className="text-muted-foreground/70 shrink-0" />
          )}
          <span
            className={cn(
              "text-sm font-medium flex-1 truncate text-sidebar-foreground/85",
              isSectionCompleted && "text-sidebar-foreground/55",
            )}
            title={`${String(index + 1).padStart(2, "0")}. ${section.title}`}
          >
            {String(index + 1).padStart(2, "0")}. {section.title}
          </span>
          {isLocked ? (
            <Icons.locked size={11} className="text-muted-foreground shrink-0" />
          ) : progress ? (
            <span className="flex items-center gap-1 shrink-0">
              <span
                className={cn(
                  "text-xs tabular-nums",
                  isSectionCompleted ? "text-green" : "text-muted-foreground/60",
                )}
              >
                {progress.completed}/{progress.total}
              </span>
              {isSectionCompleted && (
                <span className="inline-flex size-3.5 items-center justify-center rounded-full border-[1.5px] border-green/70 bg-green/15">
                  <Icons.check size={9} strokeWidth={3} className="text-green" />
                </span>
              )}
            </span>
          ) : null}
        </div>
        {progress && !isSectionCompleted && (
          <div className="relative h-1 w-full rounded-full bg-primary/10 mt-1.5 overflow-hidden">
            <div
              className="absolute inset-y-0 left-0 rounded-full bg-gradient-to-r from-primary/80 via-primary to-primary/90 shadow-[0_0_8px] shadow-primary/40 transition-[width] duration-700 ease-out"
              style={{ width: `${progress.percent}%` }}
            />
            {open && progress.percent > 0 && (
              <div
                className="absolute inset-y-0 left-0 overflow-hidden rounded-full pointer-events-none"
                style={{ width: `${progress.percent}%` }}
              >
                <div
                  className="absolute inset-y-0 -left-1/2 w-1/2 animate-shimmer-sweep"
                  style={{
                    background:
                      "linear-gradient(90deg, transparent, rgba(255,255,255,0.35), transparent)",
                  }}
                />
              </div>
            )}
          </div>
        )}
      </CollapsibleTrigger>

      <CollapsibleContent>
        <SidebarMenu className="pl-2">
          {section.items
            .filter(
              (item) =>
                item.itemType === "Material" ||
                item.itemType === "Issue" ||
                item.itemType === "Quiz",
            )
            .filter((item, idx, arr) => arr.findIndex((entry) => entry.id === item.id) === idx)
            .map((item) => {
              const itemKey = `${section.id}:${item.id}`;
              const canOpenItem = canAccessItem(item.accessType, accessLevel);
              const hasDuplicateItem = duplicatedItemIds.has(item.id);
              const baseHref = getCourseItemHref(courseSlug, item.itemType, item.id, {
                tab: sectionTab,
              });
              const href = hasDuplicateItem ? appendSectionToHref(baseHref, section.id) : baseHref;
              const isActive = activeItemKey === itemKey;
              const isTask = item.itemType === "Issue";
              const isQuiz = item.itemType === "Quiz";
              const indicatorItemType: "material" | "issue" | "quiz" = isTask
                ? "issue"
                : isQuiz
                  ? "quiz"
                  : "material";
              const defaultInactiveStatus: MaterialProgressStatus | IssueProgressStatus = isTask
                ? "NOT_STARTED"
                : "NOT_VIEWED";
              const rawStatus = hasActiveEnrollment
                ? getItemRawStatus(
                    item.itemType,
                    item.id,
                    materialStatuses,
                    issueStatuses,
                    passedQuizIds,
                  )
                : defaultInactiveStatus;

              const isItemCompleted = isTask ? rawStatus === "COMPLETED" : rawStatus === "VIEWED";
              const isContinueItem = continueItemKey === itemKey && !isActive;

              const itemContent = (
                <>
                  {!canOpenItem && (
                    <Icons.locked
                      size={11}
                      strokeWidth={1.5}
                      className="shrink-0 text-muted-foreground/50"
                    />
                  )}
                  <div className="w-3.5 h-3.5 flex items-center justify-center shrink-0">
                    <ItemProgressIndicator
                      itemType={indicatorItemType}
                      status={rawStatus}
                      tier="standard"
                      isActive={isActive}
                    />
                  </div>
                  <div className="flex-1 min-w-0 flex items-baseline gap-1.5">
                    <div
                      className={cn(
                        "text-[13px] leading-snug truncate",
                        isItemCompleted &&
                          !isContinueItem &&
                          !isActive &&
                          "line-through text-sidebar-foreground/55",
                        (isContinueItem || (isActive && isItemCompleted)) &&
                          "text-sidebar-foreground font-medium",
                      )}
                      title={item.title}
                    >
                      {item.title}
                    </div>
                    {item.materialKind === "VIDEO" && !!item.durationSeconds && (
                      <span className="text-[10px] tabular-nums text-muted-foreground/50 shrink-0">
                        {formatDurationSecondsHuman(item.durationSeconds)}
                      </span>
                    )}
                    {item.viewPriority === "Supplementary" && (
                      <span className="text-[10px] italic text-muted-foreground/50 shrink-0">
                        · доп.
                      </span>
                    )}
                  </div>
                </>
              );

              return (
                <SidebarMenuItem key={itemKey}>
                  <SidebarMenuButton
                    asChild={canOpenItem}
                    isActive={isActive}
                    tooltip={item.title}
                    className={cn(
                      "gap-2 h-auto py-1.5",
                      !canOpenItem && "opacity-50 cursor-not-allowed",
                      isContinueItem &&
                        "bg-primary/10 hover:bg-primary/15 ring-1 ring-inset ring-primary/25",
                    )}
                  >
                    {canOpenItem ? (
                      <CurriculumLink
                        ref={isActive ? activeAnchorRef : undefined}
                        href={href}
                        itemType={item.itemType}
                        itemId={item.id}
                        queryClient={queryClient}
                        onClick={onItemClick}
                      >
                        {itemContent}
                      </CurriculumLink>
                    ) : (
                      <div className="flex items-center gap-2">{itemContent}</div>
                    )}
                  </SidebarMenuButton>
                </SidebarMenuItem>
              );
            })}
        </SidebarMenu>
      </CollapsibleContent>
    </Collapsible>
  );
}

/**
 * Sidebar curriculum link with hover-with-intent prefetching.
 *
 * Behaviour:
 * - Sweeping the cursor across many items fires zero requests.
 * - Pausing on an item for ~250ms triggers a one-time prefetch of its
 *   detail query — gives instant feel on click without API storms.
 * - React Query's staleTime de-dupes repeat hovers within the cache window.
 */
function CurriculumLink({
  ref,
  href,
  itemType,
  itemId,
  queryClient,
  onClick,
  className,
  children,
}: {
  ref?: React.Ref<HTMLAnchorElement>;
  href: string;
  itemType: string;
  itemId: string;
  queryClient: QueryClient;
  // Принимает event, потому что в `<SidebarMenuButton asChild>` Radix Slot
  // композирует свой обработчик в этот prop через `composeEventHandlers`,
  // который читает `event.defaultPrevented`. Если зовём `onClick?.()` без
  // аргумента — TypeError → React цепочка прерывается → Next.js Link не
  // делает `preventDefault()` → браузер делает hard navigation (полная
  // перезагрузка страницы вместо soft RSC swap).
  onClick?: React.MouseEventHandler<HTMLAnchorElement>;
  className?: string;
  children: React.ReactNode;
}) {
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Clear any pending timer on unmount so we don't fire prefetch for an item
  // the user has already navigated away from.
  useEffect(
    () => () => {
      if (timerRef.current) {
        clearTimeout(timerRef.current);
      }
    },
    [],
  );

  const schedulePrefetch = () => {
    if (timerRef.current) return;
    timerRef.current = setTimeout(() => {
      timerRef.current = null;
      prefetchCurriculumItem(queryClient, itemType, itemId);
    }, HOVER_PREFETCH_DELAY_MS);
  };

  const cancelPrefetch = () => {
    if (timerRef.current) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  };

  return (
    <Link
      ref={ref}
      href={href}
      className={className}
      onMouseEnter={schedulePrefetch}
      onMouseLeave={cancelPrefetch}
      onFocus={schedulePrefetch}
      onBlur={cancelPrefetch}
      // Touch devices: schedule on press-down so a deliberate tap-and-hold
      // prefetches before the click resolves; cancel on release without click.
      onTouchStart={schedulePrefetch}
      onTouchEnd={cancelPrefetch}
      onTouchCancel={cancelPrefetch}
      onClick={(e) => {
        cancelPrefetch();
        onClick?.(e);
      }}
    >
      {children}
    </Link>
  );
}
