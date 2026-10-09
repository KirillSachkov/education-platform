"use client";

import { useState } from "react";
import Link from "next/link";
import { usePathname, useSearchParams } from "next/navigation";
import { useQuery } from "@tanstack/react-query";
import { Icons } from "@/shared/ui/icons";
import { NavLinkPending } from "@/shared/ui/components";
import { Badge } from "@/shared/ui/kit/badge";
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupContent,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from "@/shared/ui/kit/sidebar";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/shared/ui/kit/sheet";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/shared/ui/kit/tooltip";
import { collectionDetailQueryOptions } from "@/entities/collection";
import { courseCurriculumQueryOptions, type CourseAccessLevel } from "@/entities/course";
import { courseLearningStateQueryOptions } from "@/entities/course-progress";
import { useResolvedCourseAccess } from "@/features/course-learning";
import { parseFromContext, routes } from "@/shared/config/routes";
import { useCourseId, useCourseSlug } from "@/shared/providers/course-id-provider";
import { CourseSidebarCollection } from "./course-sidebar-collection";
import { CourseSidebarCollections } from "./course-sidebar-collections";
import { CourseSidebarProgram } from "./course-sidebar-program";

// Cap at 28rem on desktop, shrink to viewport on narrow mobile screens
// so trailing content (progress %, counts) never clips off-screen.
// Wider than the platform sidebar — items often have long titles + duration
// suffix and a chevron/check; 24rem caused frequent truncation.
const COURSE_SIDEBAR_WIDTH = "min(28rem, 100vw)";

export function CourseSidebar() {
  const pathname = usePathname();
  const { isMobile, setOpenMobile, toggleSidebar, state, canExpand } = useSidebar();
  const [progressSheetOpen, setProgressSheetOpen] = useState(false);
  const courseId = useCourseId();
  const courseSlug = useCourseSlug();

  function closeMobileSidebar() {
    if (isMobile) setOpenMobile(false);
  }

  // Auto-close progress sheet on navigation or sidebar expand (derived state, no useEffect)
  const [prevPathname, setPrevPathname] = useState(pathname);
  const [prevState, setPrevState] = useState(state);
  if (prevPathname !== pathname) {
    setPrevPathname(pathname);
    setProgressSheetOpen(false);
  }
  if (prevState !== state && state === "expanded") {
    setPrevState(state);
    setProgressSheetOpen(false);
  }

  const { data: curriculum, isLoading: isCurriculumLoading } = useQuery(
    courseCurriculumQueryOptions(courseId),
  );
  const access = useResolvedCourseAccess(courseId, curriculum?.authorId);
  const { data: learningState } = useQuery({
    ...courseLearningStateQueryOptions(courseId),
    enabled: access.isAuthenticated && !!courseId,
  });
  const summary = learningState?.summary;
  // Бэкенд считает прогресс по ВСЕМ элементам программы (материалы + задачи +
  // квизы, blueprint.TotalItems) — не пересобираем из частичных счётчиков.
  const progressPercent = summary?.progressPercent ?? 0;

  const materialStatuses = new Map(
    (learningState?.materials ?? []).map((m) => [m.materialId, m.status]),
  );
  const issueStatuses = new Map((learningState?.issues ?? []).map((i) => [i.issueId, i.status]));
  const passedQuizIds: ReadonlySet<string> = new Set(learningState?.passedQuizIds ?? []);

  // Last navigated material — drives "Продолжить" hint in the program sidebar.
  // Source must be `lastPosition` (last OPENED), not `viewedAt` (last COMPLETED):
  // отметка «Изучено» обновляет viewedAt, и недавно завершённый материал ошибочно
  // становился точкой возврата, подавляя зачёркивание.
  const lastPositionMaterialId =
    learningState?.lastPosition?.entityType === "MATERIAL"
      ? learningState.lastPosition.entityId
      : null;

  // Sidebar = program (modules) only. Project sections are accessible via the
  // dedicated "Задания" tab; surfacing them here duplicates that view and
  // clutters navigation on long courses (#196).
  const moduleSections = (curriculum?.sections ?? []).filter((s) => s.itemType === "Module");

  // Материал открыт из подборки (?from=collection:…) — показываем в дереве состав
  // подборки вместо программы (#496): материал из Ленты/подборки в программе
  // отсутствует, и дерево программы только путает («не понятно, где оно»).
  // from-контекст уже используется крошками (#276) и prev/next-навигацией (#464).
  const searchParams = useSearchParams();
  const fromContext = parseFromContext(searchParams.get("from"));
  const collectionFrom =
    pathname.includes("/learn/") &&
    fromContext?.kind === "collection" &&
    fromContext.courseSlug === courseSlug
      ? fromContext
      : null;
  const { data: fromCollection, isLoading: isFromCollectionLoading } = useQuery({
    ...collectionDetailQueryOptions(collectionFrom?.collectionId ?? ""),
    enabled: !!collectionFrom,
  });
  const currentMaterialId = pathname.match(/\/learn\/([^/?]+)/)?.[1] ?? null;
  const isCollectionTreeLoading = !!collectionFrom && isFromCollectionLoading;
  const collectionTree =
    collectionFrom && fromCollection && fromCollection.sections.some((s) => s.items.length > 0)
      ? fromCollection
      : null;

  const navItems = [
    {
      href: routes.courseOverview(courseSlug),
      icon: Icons.dashboard,
      label: "Обзор",
      exact: true,
      enrolledOnly: false,
    },
    {
      href: routes.courseProgram(courseSlug),
      icon: Icons.listTree,
      label: "Программа",
      enrolledOnly: false,
    },
    {
      href: routes.courseAssignments(courseSlug),
      icon: Icons.issue,
      label: "Задания",
      enrolledOnly: false,
    },
    {
      href: routes.courseTests(courseSlug),
      icon: Icons.quiz,
      label: "Тесты",
      enrolledOnly: false,
    },
    {
      href: routes.courseKnowledgeBase(courseSlug),
      icon: Icons.library,
      label: "Материалы",
      enrolledOnly: false,
    },

    {
      href: routes.courseBookmarks(courseSlug),
      icon: Icons.bookmark,
      label: "Закладки",
      enrolledOnly: true,
    },
  ];

  return (
    <>
      <Sidebar variant="floating" collapsible="icon" width={COURSE_SIDEBAR_WIDTH}>
        {/* Course identity header */}
        <SidebarHeader className="py-3 gap-3">
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuButton asChild tooltip="На главную" size="sm">
                <Link href={routes.home} className="gap-2" onClick={closeMobileSidebar}>
                  <Icons.back className="size-4 shrink-0" />
                  <span className="text-xs text-sidebar-foreground/50 group-data-[collapsible=icon]:hidden">
                    На главную
                  </span>
                  <NavLinkPending />
                </Link>
              </SidebarMenuButton>
            </SidebarMenuItem>
            {canExpand && (
              <SidebarMenuItem>
                <SidebarMenuButton onClick={toggleSidebar} tooltip="Свернуть" size="sm">
                  <Icons.sidebarToggle className="size-4 shrink-0" />
                  <span className="text-xs group-data-[collapsible=icon]:hidden">Свернуть</span>
                </SidebarMenuButton>
              </SidebarMenuItem>
            )}
          </SidebarMenu>

          {/* Course icon when collapsed — opens progress sheet */}
          <div className="hidden group-data-[collapsible=icon]:flex justify-center py-1">
            <Tooltip>
              <TooltipTrigger asChild>
                <button
                  type="button"
                  className="size-8 rounded-lg bg-primary/10 flex items-center justify-center cursor-pointer hover:bg-primary/20 transition-colors"
                  onClick={() => setProgressSheetOpen(true)}
                >
                  <Icons.lesson className="size-4 text-primary" />
                </button>
              </TooltipTrigger>
              <TooltipContent side="right">Прогресс курса</TooltipContent>
            </Tooltip>
          </div>

          {/* Course branding + compact progress */}
          <div className="group-data-[collapsible=icon]:hidden">
            <ProgressCard
              title={curriculum?.title ?? "Загрузка..."}
              progressPercent={progressPercent}
              summary={summary}
              hasActiveEnrollment={access.hasActiveEnrollment}
              accessLevel={access.accessLevel}
              isNew={curriculum?.isNew ?? false}
            />
          </div>
        </SidebarHeader>

        <SidebarContent>
          {/* Navigation links */}
          <SidebarGroup className="py-1.5">
            <SidebarGroupContent>
              <SidebarMenu>
                {navItems.map((item) => {
                  const isActive = item.exact
                    ? pathname === item.href
                    : pathname.startsWith(item.href);
                  const isLocked = item.enrolledOnly && !access.hasActiveEnrollment;

                  return (
                    <SidebarMenuItem key={item.href}>
                      {isLocked ? (
                        <SidebarMenuButton
                          isActive={false}
                          tooltip={item.label}
                          className="opacity-50 cursor-not-allowed"
                        >
                          <item.icon />
                          <span>{item.label}</span>
                        </SidebarMenuButton>
                      ) : (
                        <SidebarMenuButton asChild isActive={isActive} tooltip={item.label}>
                          {/* Default Next.js prefetch на 6 фиксированных nav-ссылках —
                              переход между секциями курса (Программа / Задания / База
                              знаний) ощущается мгновенным, без всплесков loading.tsx.
                              `NavLinkPending` показывает индикатор только если переход
                              всё-таки занял >100ms (медленная сеть / cold cache). */}
                          <Link href={item.href} onClick={closeMobileSidebar}>
                            <item.icon />
                            <span>{item.label}</span>
                            <NavLinkPending />
                          </Link>
                        </SidebarMenuButton>
                      )}
                    </SidebarMenuItem>
                  );
                })}
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>

          {/* Curriculum / collection tree -- hidden when sidebar is collapsed */}
          <div className="group-data-[collapsible=icon]:hidden">
            {isCurriculumLoading || isCollectionTreeLoading ? (
              <div className="flex items-center justify-center py-8">
                <Icons.loading className="size-4 animate-spin text-muted-foreground" />
              </div>
            ) : collectionTree && collectionFrom ? (
              <CourseSidebarCollection
                collection={collectionTree}
                courseSlug={courseSlug}
                from={collectionFrom}
                currentMaterialId={currentMaterialId}
                materialStatuses={materialStatuses}
                hasActiveEnrollment={access.hasActiveEnrollment}
                onItemClick={closeMobileSidebar}
              />
            ) : curriculum ? (
              <>
                <CourseSidebarProgram
                  sections={moduleSections}
                  materialStatuses={materialStatuses}
                  issueStatuses={issueStatuses}
                  passedQuizIds={passedQuizIds}
                  hasActiveEnrollment={access.hasActiveEnrollment}
                  accessLevel={access.accessLevel}
                  lastPositionMaterialId={lastPositionMaterialId}
                  onItemClick={closeMobileSidebar}
                />
                <CourseSidebarCollections
                  collections={curriculum.collections ?? []}
                  courseSlug={courseSlug}
                  materialStatuses={materialStatuses}
                  hasActiveEnrollment={access.hasActiveEnrollment}
                  accessLevel={access.accessLevel}
                  onItemClick={closeMobileSidebar}
                />
              </>
            ) : null}
          </div>
        </SidebarContent>
      </Sidebar>

      {/* Progress sheet for collapsed desktop sidebar */}
      {!isMobile && (
        <Sheet open={progressSheetOpen} onOpenChange={setProgressSheetOpen}>
          <SheetContent
            side="left"
            className="bg-sidebar text-sidebar-foreground p-0 overflow-y-auto"
          >
            <SheetHeader className="sr-only">
              <SheetTitle>Прогресс курса</SheetTitle>
              <SheetDescription>Прогресс и программа курса</SheetDescription>
            </SheetHeader>

            <div className="flex flex-col gap-3 p-3 pt-10">
              <ProgressCard
                title={curriculum?.title ?? "Загрузка..."}
                progressPercent={progressPercent}
                summary={summary}
                hasActiveEnrollment={access.hasActiveEnrollment}
                accessLevel={access.accessLevel}
                isNew={curriculum?.isNew ?? false}
              />

              {isCurriculumLoading || isCollectionTreeLoading ? (
                <div className="flex items-center justify-center py-8">
                  <Icons.loading className="size-4 animate-spin text-muted-foreground" />
                </div>
              ) : collectionTree && collectionFrom ? (
                <CourseSidebarCollection
                  collection={collectionTree}
                  courseSlug={courseSlug}
                  from={collectionFrom}
                  currentMaterialId={currentMaterialId}
                  materialStatuses={materialStatuses}
                  hasActiveEnrollment={access.hasActiveEnrollment}
                />
              ) : curriculum ? (
                <>
                  <CourseSidebarProgram
                    sections={moduleSections}
                    materialStatuses={materialStatuses}
                    issueStatuses={issueStatuses}
                    passedQuizIds={passedQuizIds}
                    hasActiveEnrollment={access.hasActiveEnrollment}
                    accessLevel={access.accessLevel}
                  />
                  <CourseSidebarCollections
                    collections={curriculum.collections ?? []}
                    courseSlug={courseSlug}
                    materialStatuses={materialStatuses}
                    hasActiveEnrollment={access.hasActiveEnrollment}
                    accessLevel={access.accessLevel}
                  />
                </>
              ) : null}
            </div>
          </SheetContent>
        </Sheet>
      )}
    </>
  );
}

function ProgressCard({
  title,
  progressPercent,
  summary,
  hasActiveEnrollment,
  accessLevel,
  isNew,
}: {
  title: string;
  progressPercent: number;
  summary:
    | {
        materialsViewed: number;
        materialsTotal: number;
        issuesCompleted: number;
        issuesTotal: number;
      }
    | undefined;
  hasActiveEnrollment: boolean;
  accessLevel: CourseAccessLevel;
  isNew: boolean;
}) {
  const accessLabel =
    accessLevel === "admin" || accessLevel === "standard"
      ? "Полный доступ"
      : "Доступ не активирован";
  const showUpgrade = accessLevel === "authenticated";

  return (
    <div className="mx-1 rounded-xl bg-gradient-to-br from-primary/8 to-primary/3 border border-primary/10 px-3 py-2.5">
      <div className="flex items-start justify-between gap-2">
        <div className="flex items-center gap-1.5 min-w-0">
          <h2 className="text-[13px] font-semibold leading-tight line-clamp-2 text-sidebar-foreground">
            {title}
          </h2>
          {isNew && (
            <Badge className="bg-emerald-500/90 text-white border-0 text-[10px] px-1.5 py-0 leading-4 shrink-0">
              New
            </Badge>
          )}
        </div>
        {hasActiveEnrollment && summary && (
          <span className="text-xs font-semibold text-primary tabular-nums shrink-0 mt-0.5">
            {progressPercent}%
          </span>
        )}
      </div>
      <div className="mt-1.5 flex items-center gap-1.5 text-[11px] text-sidebar-foreground/55">
        <Icons.locked className="size-3 shrink-0" />
        <span className="truncate">{accessLabel}</span>
        {showUpgrade && (
          <Link
            href={routes.pricing}
            className="ml-auto shrink-0 font-medium text-primary hover:underline"
          >
            Тарифы
          </Link>
        )}
      </div>
      {hasActiveEnrollment && summary && (
        <>
          <div className="relative mt-2 h-1 rounded-full bg-primary/10 overflow-hidden">
            <div
              className="absolute inset-y-0 left-0 rounded-full bg-primary transition-[width] duration-500 ease-out"
              style={{ width: `${progressPercent}%` }}
            />
            {progressPercent > 0 && (
              <div
                className="absolute inset-y-0 left-0 overflow-hidden rounded-full pointer-events-none"
                style={{ width: `${progressPercent}%` }}
              >
                <div
                  className="absolute inset-y-0 -left-1/2 w-1/2 animate-shimmer-sweep"
                  style={{
                    background:
                      "linear-gradient(90deg, transparent, rgba(255,255,255,0.3), transparent)",
                  }}
                />
              </div>
            )}
          </div>
          <p className="mt-1.5 text-[11px] text-sidebar-foreground/50 tabular-nums">
            {summary.materialsViewed}/{summary.materialsTotal} материалов
            <span className="mx-1.5 text-sidebar-foreground/30">·</span>
            {summary.issuesCompleted}/{summary.issuesTotal} задач
          </p>
        </>
      )}
    </div>
  );
}
