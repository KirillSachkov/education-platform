"use client";

import { useQuery } from "@tanstack/react-query";
import { courseCurriculumQueryOptions } from "@/entities/course";
import type { CurriculumItemDto } from "@/entities/course";
import { courseLearningStateQueryOptions } from "@/entities/course-progress";
import {
  ContinueLearningCard,
  CourseAccessNotice,
  CurriculumSectionCard,
  computeProgramTotals,
  findActiveSectionId,
  useResolvedCourseAccess,
} from "@/features/course-learning";
import { BookmarkStatusProvider } from "@/entities/bookmark";
import { ItemBookmarkButton } from "../_components/item-bookmark-button";
import { ProgramCollections } from "../_components/program-collections";
import { useFreeOnlyParam } from "@/shared/hooks";
import { useCourseId, useCourseSlug } from "@/shared/providers/course-id-provider";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { pluralize, pluralizeRu, RU_PLURALS } from "@/shared/lib/pluralize";
import { FreeOnlyToggle } from "@/shared/ui/components";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import Link from "next/link";
import { usePathname, useSearchParams } from "next/navigation";
import { useEffect, useRef } from "react";

// Issue #358: AccessType.FREE удалён. «Бесплатно для зарегистрированного пользователя» =
// PUBLIC + REGISTERED. Toggle FreeOnly режет выдачу до этих двух уровней.
const FREE_ACCESS_TYPES = new Set(["PUBLIC", "REGISTERED"]);

function isFreeAccessType(accessType: string | null | undefined): boolean {
  return !!accessType && FREE_ACCESS_TYPES.has(accessType);
}

export default function CourseProgramPage() {
  const courseId = useCourseId();
  const courseSlug = useCourseSlug();
  const searchParams = useSearchParams();
  const pathname = usePathname();

  const [freeOnly, setFreeOnly] = useFreeOnlyParam();
  const { data: curriculum, isLoading } = useQuery(courseCurriculumQueryOptions(courseId));
  const access = useResolvedCourseAccess(courseId, curriculum?.authorId);
  const { data: learningState, isLoading: learningStateLoading } = useQuery({
    ...courseLearningStateQueryOptions(courseId),
    enabled: access.isAuthenticated && !!courseId,
  });

  // `?section=<id>` приходит из breadcrumb'ов модуля — должен открыть и прокрутить
  // к этому разделу. Если param'а нет, открывается «активный» раздел по прогрессу.
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

  const waitingForProgress = access.isAuthenticated && learningStateLoading;
  if (isLoading || waitingForProgress) {
    return (
      <div className="max-w-6xl mx-auto px-4 py-8 md:px-8 space-y-6">
        <div className="space-y-2">
          <Skeleton className="h-10 w-72" />
          <Skeleton className="h-4 w-96" />
        </div>
        <div className="space-y-4 pt-4">
          <Skeleton className="h-28 rounded-2xl" />
          <Skeleton className="h-16 rounded-2xl" />
          <Skeleton className="h-16 rounded-2xl" />
        </div>
      </div>
    );
  }

  if (!curriculum) return null;

  // `freeOnly` — клиентский фильтр поверх уже загруженного curriculum: backend
  // тоже умеет (`?accessFilter=free`), но фильтр на клиенте даёт мгновенный
  // отклик toggle'а без refetch и сохраняет общий запрос в react-query кеше.
  const moduleSections = curriculum.sections
    .filter((s) => s.itemType === "Module")
    .map((section) =>
      freeOnly
        ? { ...section, items: section.items.filter((i) => isFreeAccessType(i.accessType)) }
        : section,
    )
    .filter((section) => !freeOnly || section.items.length > 0);
  // Счётчики включают и материалы подборок курса (#522) — подборки учитываются в общем
  // прогрессе (#508/#496). Подборки в freeOnly не фильтруются (блок рендерится всегда),
  // поэтому их материалы остаются в X/Y и при включённом toggle'е — осознанный trade-off:
  // числитель и знаменатель консистентны (оба от одного set'а id).
  // Считаем completed только по items, видимым в текущем view'е — иначе при freeOnly
  // знаменатель сужается до бесплатных, а числитель остаётся глобальным → 167%.
  const courseCollections = curriculum.collections ?? [];
  const {
    totalMaterials,
    totalIssues,
    totalQuizzes,
    totalItems,
    materialIds: visibleMaterialIds,
    issueIds: visibleIssueIds,
    quizIds: visibleQuizIds,
  } = computeProgramTotals(moduleSections, courseCollections);
  const completedMaterials =
    learningState?.materials.filter(
      (m) => m.status === "VIEWED" && visibleMaterialIds.has(m.materialId),
    ).length ?? 0;
  const completedIssues =
    learningState?.issues.filter((i) => i.status === "COMPLETED" && visibleIssueIds.has(i.issueId))
      .length ?? 0;
  // Квизы: пройден = passed-попытка (ST-16 #495); id квиза = id curriculum-итема.
  const completedQuizzes =
    learningState?.passedQuizIds?.filter((quizId) => visibleQuizIds.has(quizId)).length ?? 0;
  const completedItems = completedMaterials + completedIssues + completedQuizzes;
  const overallPercent = totalItems > 0 ? Math.round((completedItems / totalItems) * 100) : 0;

  // Материалы курса вне программы (Лента + подборки из базы знаний): сайдбар считает
  // прогресс по ВСЕМУ курсу (blueprint), страница — только по модулям. Подсказка
  // объясняет разницу и ведёт туда, где живут остальные материалы (#496).
  // Считаем от нефильтрованной программы — freeOnly не должен раздувать разницу.
  const programMaterialsAll = computeProgramTotals(
    curriculum.sections.filter((s) => s.itemType === "Module"),
    courseCollections,
  ).totalMaterials;
  const kbExtraMaterials = learningState
    ? Math.max(0, learningState.summary.materialsTotal - programMaterialsAll)
    : 0;

  // Last-position имеет приоритет над «первым незавершённым»: если ученик уже открывал
  // конкретный материал/задание в курсе — раскрываем именно его модуль и подсвечиваем
  // item бейджем «Продолжить».
  const lastPositionItemId = learningState?.lastPosition?.entityId ?? null;
  const lastPositionSection = lastPositionItemId
    ? (moduleSections.find((s) => s.items.some((i) => i.id === lastPositionItemId)) ?? null)
    : null;
  const lastPositionSectionId = lastPositionSection?.id ?? null;
  const lastPositionItem =
    lastPositionItemId && lastPositionSection
      ? (lastPositionSection.items.find((i) => i.id === lastPositionItemId) ?? null)
      : null;
  const lastPositionCompleted = lastPositionItem
    ? lastPositionItem.itemType === "Material"
      ? learningState?.materials.find((m) => m.materialId === lastPositionItem.id)?.status ===
        "VIEWED"
      : learningState?.issues.find((i) => i.issueId === lastPositionItem.id)?.status === "COMPLETED"
    : false;
  const continueItem = lastPositionItem && !lastPositionCompleted ? lastPositionItem : null;
  const continueSectionNumber = continueItem
    ? moduleSections.findIndex((s) => s.id === lastPositionSectionId) + 1
    : 0;

  // Если есть `?section=<id>`, открываем именно этот раздел (если он в текущем view'е),
  // иначе — модуль с last-position, иначе — раздел с ближайшим незавершённым.
  const requestedSectionMatches =
    requestedSectionId && moduleSections.some((s) => s.id === requestedSectionId);
  const activeSectionId = requestedSectionMatches
    ? requestedSectionId
    : (lastPositionSectionId ?? findActiveSectionId(moduleSections, "all", learningState));

  // Все material+issue id'шники для batched-bookmark-prefetch
  const bookmarkTargets = access.isAuthenticated
    ? moduleSections.flatMap((s) =>
        s.items
          .filter((i) => i.itemType === "Material" || i.itemType === "Issue")
          .map((i) => ({
            courseId,
            entityType: i.itemType === "Issue" ? ("Issue" as const) : ("Material" as const),
            entityId: i.id,
          })),
      )
    : [];

  const renderTrailing = access.isAuthenticated
    ? (item: CurriculumItemDto) =>
        // Закладок на квизы нет (ST-16 #495) — слот рендерим только для материалов/заданий.
        item.itemType === "Quiz" ? null : (
          <ItemBookmarkButton
            courseId={courseId}
            entityType={item.itemType === "Issue" ? "Issue" : "Material"}
            entityId={item.id}
          />
        )
    : undefined;

  const showProgress = access.isAuthenticated && learningState !== undefined && totalItems > 0;
  const isComplete = overallPercent === 100;
  const stats = [
    { value: moduleSections.length, label: "модулей" },
    { value: totalMaterials, label: "материалов" },
    { value: totalIssues, label: "задач" },
    ...(totalQuizzes > 0 ? [{ value: totalQuizzes, label: "тестов" }] : []),
  ];

  return (
    <BookmarkStatusProvider items={bookmarkTargets}>
      <div className="max-w-6xl mx-auto px-4 py-8 md:px-8 pb-16">
        <header className="mb-5 md:mb-6">
          <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
            <div className="min-w-0">
              <h1 className="text-2xl md:text-3xl font-bold tracking-tight text-foreground">
                Программа курса
              </h1>
              {showProgress ? (
                <div className="mt-1.5 space-y-2 max-w-md">
                  <p className="text-sm text-muted-foreground tabular-nums">
                    <span className={cn("font-medium", isComplete ? "text-green" : "text-primary")}>
                      {completedItems} из {totalItems}
                    </span>{" "}
                    {pluralize(totalItems, "элемента", "элементов", "элементов")} программы
                    завершено
                    <span className="mx-2 text-muted-foreground/40">·</span>
                    <span
                      className={cn("font-semibold", isComplete ? "text-green" : "text-primary")}
                    >
                      {overallPercent}%
                    </span>
                  </p>
                  <div
                    className="h-1.5 w-full overflow-hidden rounded-full bg-muted"
                    role="progressbar"
                    aria-valuenow={overallPercent}
                    aria-valuemin={0}
                    aria-valuemax={100}
                  >
                    <div
                      className={cn(
                        "h-full rounded-full transition-[width] duration-700",
                        isComplete ? "bg-green" : "bg-primary",
                      )}
                      style={{ width: `${Math.min(100, overallPercent)}%` }}
                    />
                  </div>
                  {kbExtraMaterials > 0 && (
                    <p className="text-xs text-muted-foreground/70">
                      Ещё {kbExtraMaterials} {pluralizeRu(kbExtraMaterials, RU_PLURALS.material)}{" "}
                      курса — в{" "}
                      <Link
                        href={routes.courseKnowledgeBase(courseSlug)}
                        className="text-primary hover:underline"
                      >
                        базе знаний
                      </Link>
                    </p>
                  )}
                </div>
              ) : (
                <p className="mt-1.5 text-sm text-muted-foreground">
                  {totalItems} {totalItems === 1 ? "элемент" : "элементов"} в программе
                </p>
              )}
            </div>

            <dl className="flex items-stretch divide-x divide-border/50 rounded-xl border border-border/50 bg-card/30 shrink-0">
              {stats.map((s) => (
                <div
                  key={s.label}
                  className="flex flex-col items-center justify-center px-4 py-2 sm:px-5 sm:py-2.5 min-w-[72px]"
                >
                  <dt className="order-2 mt-0.5 text-[10px] uppercase tracking-[0.14em] text-muted-foreground/70">
                    {s.label}
                  </dt>
                  <dd className="order-1 text-lg sm:text-xl font-bold tabular-nums text-foreground leading-none">
                    {s.value}
                  </dd>
                </div>
              ))}
            </dl>
          </div>
        </header>

        <CourseAccessNotice accessLevel={access.accessLevel} className="mb-4" />

        {continueItem && lastPositionSection && (
          <div className="mb-3 md:mb-4">
            <ContinueLearningCard
              item={continueItem}
              section={lastPositionSection}
              sectionNumber={continueSectionNumber}
              courseSlug={courseSlug}
            />
          </div>
        )}

        <div className="mb-3 flex items-center justify-between gap-3">
          <FreeOnlyToggle active={freeOnly} onChange={setFreeOnly} />
        </div>

        {moduleSections.length === 0 ? (
          // «Нет модулей» прячем, когда у курса есть подборки (#522) — блок «Подборки
          // курса» ниже сам объясняет содержимое. freeOnly-сообщение оставляем: оно
          // про результат фильтра, а подборки им не фильтруются.
          (freeOnly || courseCollections.length === 0) && (
            <p className="text-sm text-muted-foreground text-center py-10">
              {freeOnly ? "В этом курсе нет бесплатных материалов" : "Нет модулей"}
            </p>
          )
        ) : (
          <div className="space-y-2">
            {moduleSections.map((section, index) => (
              <CurriculumSectionCard
                key={section.id}
                section={section}
                sectionNumber={index + 1}
                sectionKind={
                  section.id === curriculum.gettingStartedModuleId ? "getting-started" : "module"
                }
                itemFilter="all"
                learningState={learningState}
                accessLevel={access.accessLevel}
                courseSlug={courseSlug}
                defaultOpen={section.id === activeSectionId}
                currentPath={pathname}
                renderItemTrailing={renderTrailing}
                lastPositionItemId={lastPositionItemId}
              />
            ))}
          </div>
        )}

        {/* Подборки курса (#508) — отдельный блок ПОД модулями, типы не смешиваем.
            freeOnly-фильтр на подборки не действует (это metadata-карточки, не items). */}
        <ProgramCollections
          collections={courseCollections}
          viewedMaterialIds={
            new Set(
              (learningState?.materials ?? [])
                .filter((m) => m.status === "VIEWED")
                .map((m) => m.materialId),
            )
          }
          showProgress={access.isAuthenticated && !!learningState}
          accessLevel={access.accessLevel}
          courseSlug={courseSlug}
        />
      </div>
    </BookmarkStatusProvider>
  );
}
